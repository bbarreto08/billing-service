using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog.Sinks.OpenTelemetry;
using wrench.billing.api.Configuration;
using wrench.billing.api.Middlewares;

namespace wrench.billing.tests.Api;

public class ObservabilidadeTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private static IConfiguration Config(Dictionary<string, string?>? valores = null)
        => new ConfigurationBuilder().AddInMemoryCollection(valores ?? []).Build();

    private static IHostEnvironment Ambiente()
    {
        var ambiente = new Mock<IHostEnvironment>();
        ambiente.SetupGet(a => a.EnvironmentName).Returns("Testing");
        return ambiente.Object;
    }

    // ------------------------------------------------------------ CorrelationId

    [Fact]
    public async Task CorrelationId_GeradoQuandoNaoEnviado()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var valor = Assert.Single(resposta.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.True(Guid.TryParse(valor, out _));
    }

    [Fact]
    public async Task CorrelationId_ReaproveitadoQuandoEnviado()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "corr-OS-1234");

        var resposta = await cliente.GetAsync("/health");

        Assert.Equal("corr-OS-1234", Assert.Single(resposta.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task CorrelationId_EmBrancoGeraUmNovo()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, " ");

        var resposta = await cliente.GetAsync("/health");

        Assert.True(Guid.TryParse(Assert.Single(resposta.Headers.GetValues(CorrelationIdMiddleware.HeaderName)), out _));
    }

    // ------------------------------------------------------------ Serilog

    [Theory]
    [InlineData("https://otlp.nr-data.net:4318", "https://otlp.nr-data.net:4318/v1/logs")]
    [InlineData("https://otlp.nr-data.net:4318/", "https://otlp.nr-data.net:4318/v1/logs")]
    [InlineData("https://otlp.nr-data.net:4318/v1/logs", "https://otlp.nr-data.net:4318/v1/logs")]
    public void Serilog_CompletaOCaminhoDeLogsNoProtocoloHttp(string entrada, string esperado)
        => Assert.Equal(esperado, SerilogConfiguration.BuildHttpLogsEndpoint(entrada));

    [Theory]
    [InlineData(null, OtlpProtocol.HttpProtobuf)]
    [InlineData("", OtlpProtocol.HttpProtobuf)]
    [InlineData("http/protobuf", OtlpProtocol.HttpProtobuf)]
    [InlineData("grpc", OtlpProtocol.Grpc)]
    [InlineData("GRPC", OtlpProtocol.Grpc)]
    public void Serilog_ResolveOProtocolo(string? entrada, OtlpProtocol esperado)
        => Assert.Equal(esperado, SerilogConfiguration.ResolveProtocol(entrada));

    [Fact]
    public void Serilog_InterpretaOsHeadersOtlp()
    {
        var headers = SerilogConfiguration.ParseHeaders("api-key=abc%20123, x-extra = valor ,invalido");

        Assert.Equal("abc 123", headers["api-key"]);
        Assert.Equal("valor", headers["x-extra"]);
        Assert.Equal(2, headers.Count);
        Assert.Empty(SerilogConfiguration.ParseHeaders(null));
        Assert.Empty(SerilogConfiguration.ParseHeaders("  "));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http://localhost:1", "http/protobuf")]
    [InlineData("http://localhost:1", "grpc")]
    public void Serilog_ConfiguraComESemExportacaoOtlp(string? endpoint, string? protocolo)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = protocolo,
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "api-key=teste"
        });

        builder.ConfigureSerilog();

        using var app = builder.Build();
        Assert.NotNull(app.Services.GetService<Serilog.ILogger>());
    }

    // ------------------------------------------------------------ OpenTelemetry

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenTelemetry_RegistraTracesEMetricasComEmSemExportador(bool comEndpoint)
    {
        var config = Config(comEndpoint
            ? new() { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318", ["Observability:ServiceName"] = "svc-teste" }
            : null);

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddObservability(config, Ambiente())
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<TracerProvider>());
        Assert.NotNull(provider.GetRequiredService<MeterProvider>());
    }

    [Fact]
    public void OpenTelemetry_EscutaAFonteDoMassTransit()
        => Assert.Equal("MassTransit", ObservabilityConfiguration.MassTransitSource);
}
