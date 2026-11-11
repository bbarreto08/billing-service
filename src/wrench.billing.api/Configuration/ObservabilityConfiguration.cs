using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace wrench.billing.api.Configuration;

public static class ObservabilityConfiguration
{
    public const string DefaultServiceName = "wrench-billing-api";

    /// <summary>Nome da fonte de atividades do MassTransit (spans de publish/consume, com propagação de trace).</summary>
    public const string MassTransitSource = "MassTransit";

    /// <summary>
    /// Traces e métricas via OpenTelemetry. O destino (New Relic OTLP) e as credenciais vêm das variáveis
    /// de ambiente padrão OTEL_EXPORTER_OTLP_*; sem endpoint o serviço instrumenta, mas não exporta nada.
    /// Os spans do MassTransit ligam a requisição HTTP ao consumo da mensagem no mesmo trace.
    /// </summary>
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var serviceName = configuration["Observability:ServiceName"] ?? DefaultServiceName;
        var serviceVersion = typeof(ObservabilityConfiguration).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var exportar = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)]));

        otel.WithTracing(tracing =>
        {
            tracing
                .AddSource(MassTransitSource)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation();

            if (exportar)
                tracing.AddOtlpExporter();
        });

        otel.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(MassTransitSource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            if (exportar)
                metrics.AddOtlpExporter();
        });

        return services;
    }
}
