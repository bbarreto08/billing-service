using Amazon.SimpleNotificationService;
using Amazon.SQS;
using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.application.Consumers;
using wrench.billing.infra.Mensageria;
using wrench.billing.domain;
using wrench.billing.infra.Notificacoes;
using wrench.billing.infra.Pagamentos;
using wrench.billing.infra.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using wrench.billing.application.Options;
using wrench.billing.application.Exceptions;

namespace wrench.billing.infra;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingInfra(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PagamentoOptions>(configuration.GetSection(PagamentoOptions.Secao));
        services.Configure<MockGatewayOptions>(configuration.GetSection(MockGatewayOptions.Secao));

        AddPersistencia(services, configuration);

        services.AddScoped<IBillingDb>(sp => sp.GetRequiredService<BillingDbContext>());

        // Provedor de pagamento: hoje apenas o mock (singleton para manter o estado simulado).
        services.AddSingleton<MockPagamentoGateway>();
        services.AddSingleton<IPagamentoGateway>(sp => sp.GetRequiredService<MockPagamentoGateway>());
        services.AddSingleton<IOrcamentoNotificador, LogOrcamentoNotificador>();

        if (configuration.GetValue("Pagamento:ReconciliacaoHabilitada", true))
            services.AddHostedService<PagamentoReconciliacaoWorker>();

        AddMensageria(services, configuration);

        return services;
    }

    private static void AddPersistencia(IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue("Database:UseInMemory", false))
        {
            services.AddDbContext<BillingDbContext>(o => o.UseInMemoryDatabase("billing"));
            return;
        }

        var connectionString = configuration.GetConnectionString("Billing")
            ?? throw new InvalidOperationException("ConnectionStrings:Billing não configurada.");

        services.AddDbContext<BillingDbContext>(o => o.UseNpgsql(connectionString));
    }

    private static void AddMensageria(IServiceCollection services, IConfiguration configuration)
    {
        var transporte = configuration.GetValue("Messaging:Transport", "Sqs");
        var inMemoryDb = configuration.GetValue("Database:UseInMemory", false);

        services.AddMassTransit(x =>
        {
            x.AddConsumer<GerarOrcamentoConsumer>();
            x.AddConsumer<CancelarOrcamentoConsumer>();
            x.AddConsumer<EstornarPagamentoConsumer>();

            // Outbox transacional: eventos só saem se o estado do banco foi gravado (e vice-versa).
            // Inbox: mensagem reentregue (SQS é at-least-once) não é processada duas vezes.
            if (!inMemoryDb)
            {
                x.AddEntityFrameworkOutbox<BillingDbContext>(o =>
                {
                    o.UsePostgres();
                    o.UseBusOutbox();
                });
            }

            if (transporte.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
                return;
            }

            x.UsingAmazonSqs((context, cfg) =>
            {
                var regiao = configuration["Messaging:Region"] ?? "us-east-1";
                var serviceUrl = configuration["Messaging:ServiceUrl"]; // LocalStack em dev

                cfg.Host(regiao, h =>
                {
                    // Sem chaves explícitas: usa a cadeia padrão da AWS (IRSA no EKS).
                    if (!string.IsNullOrWhiteSpace(serviceUrl))
                    {
                        h.Config(new AmazonSQSConfig { ServiceURL = serviceUrl });
                        h.Config(new AmazonSimpleNotificationServiceConfig { ServiceURL = serviceUrl });
                    }
                });

                cfg.UseMessageRetry(r =>
                {
                    // Violação de regra de negócio não melhora com retry: vai direto para a fila de erro/DLQ.
                    r.Ignore<DomainException>();
                    r.Ignore<NaoEncontradoException>();
                    r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
                });

                // Nomes de tópicos e filas vêm da configuração (Messaging:*), pois dependem de como a
                // plataforma provisiona o SNS/SQS. Ver "Topologia de mensagens" no README.
                var topologia = configuration.GetSection(MensageriaOptions.Secao).Get<MensageriaOptions>()
                                ?? new MensageriaOptions();
                MensageriaTopologia.Aplicar(cfg, topologia);

                cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(
                    string.IsNullOrWhiteSpace(topologia.PrefixoFilas) ? null : topologia.PrefixoFilas.Trim(), false));
            });
        });
    }
}
