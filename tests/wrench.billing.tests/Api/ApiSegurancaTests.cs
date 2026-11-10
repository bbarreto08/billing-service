using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using wrench.billing.application.Abstractions;

namespace wrench.billing.tests.Api;

public class ApiSegurancaTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private static HttpClient ComToken(WebApplicationFactory<Program> f, string papel)
    {
        var cliente = f.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.GerarToken(papel));
        return cliente;
    }

    [Fact]
    public async Task HealthLiveEReady_RespondemOk()
    {
        var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Raiz_RedirecionaParaDocumentacao()
    {
        var cliente = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resposta = await cliente.GetAsync("/");

        Assert.Equal(HttpStatusCode.MovedPermanently, resposta.StatusCode);
        Assert.Equal("/docs-ui", resposta.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Funcionario")]
    public async Task AprovarERecusar_SaoExclusivosDoCliente(string papel)
    {
        var cliente = fabrica.ComPapel(papel);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsync($"/api/v1/orcamento/{id}/aprovar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsync($"/api/v1/orcamento/{id}/recusar", null)).StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Funcionario")]
    [InlineData("Cliente")]
    public async Task Consultas_AceitamTodosOsPerfis(string papel)
    {
        var orcamentoId = await fabrica.CriarOrcamentoAsync();

        var resposta = await fabrica.ComPapel(papel).GetAsync($"/api/v1/orcamento/{orcamentoId}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulacao_ClienteNaoPodeSimular()
    {
        var resposta = await fabrica.Cliente()
            .PostAsync($"/api/v1/simulacao/pagamentos/{Guid.NewGuid()}/aprovado", null);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulacao_DesabilitadaRetorna404()
    {
        using var semSimulacao = fabrica.WithWebHostBuilder(b => b.UseSetting("Simulacao:Habilitada", "false"));

        var resposta = await ComToken(semSimulacao, "Admin")
            .PostAsync($"/api/v1/simulacao/pagamentos/{Guid.NewGuid()}/aprovado", null);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulacao_ComGatewayRealRetorna404()
    {
        using var comGatewayReal = fabrica.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddSingleton(new Mock<IPagamentoGateway>().Object)));

        var resposta = await ComToken(comGatewayReal, "Admin")
            .PostAsync($"/api/v1/simulacao/pagamentos/{Guid.NewGuid()}/aprovado", null);

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task TokenInvalidoRetorna401()
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token.invalido.qualquer");

        var resposta = await cliente.GetAsync($"/api/v1/orcamento/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public void SemConfiguracaoDeJwt_AAplicacaoNaoSobe()
    {
        using var semJwt = fabrica.WithWebHostBuilder(b => b.UseSetting("JwtOptions:Secret", ""));

        Assert.ThrowsAny<Exception>(() => semJwt.CreateClient());
    }
}