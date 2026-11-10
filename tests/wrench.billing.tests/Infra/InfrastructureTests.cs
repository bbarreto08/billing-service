using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.infra;
using wrench.billing.infra.Pagamentos;
using wrench.billing.infra.Persistence;
using wrench.billing.tests.Support;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wrench.billing.application.Contracts;
using wrench.billing.application.Services;
using wrench.billing.application.Options;

namespace wrench.billing.tests.Infra;

public class InfrastructureTests
{
    private static ServiceProvider Montar(Dictionary<string, string?> config)
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBillingApplication();
        services.AddBillingInfra(configuracao);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Composicao_ComSqsEPostgresResolveTodosOsServicosSemConectar()
    {
        await using var provider = Montar(new()
        {
            ["ConnectionStrings:Billing"] = "Host=localhost;Database=wrench_billing;Username=u;Password=p",
            ["Messaging:Transport"] = "Sqs",
            ["Messaging:Region"] = "us-east-1",
            ["Messaging:ServiceUrl"] = "http://localhost:4566",
            ["Pagamento:ReconciliacaoHabilitada"] = "false"
        });

        using var escopo = provider.CreateScope();
        Assert.NotNull(escopo.ServiceProvider.GetRequiredService<OrcamentoService>());
        Assert.NotNull(escopo.ServiceProvider.GetRequiredService<PagamentoService>());
        Assert.NotNull(escopo.ServiceProvider.GetRequiredService<IBillingDb>());
        Assert.NotNull(provider.GetRequiredService<IBusControl>());
        Assert.IsType<MockPagamentoGateway>(provider.GetRequiredService<IPagamentoGateway>());
        Assert.DoesNotContain(provider.GetServices<IHostedService>(), s => s is PagamentoReconciliacaoWorker);
    }

    [Fact]
    public async Task Composicao_RegistraWorkerDeReconciliacaoPorPadrao()
    {
        await using var provider = Montar(new()
        {
            ["Database:UseInMemory"] = "true",
            ["Messaging:Transport"] = "InMemory"
        });

        Assert.Contains(provider.GetServices<IHostedService>(), s => s is PagamentoReconciliacaoWorker);
    }

    [Fact]
    public void Composicao_SemConnectionStringLanca()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Montar(new() { ["Messaging:Transport"] = "InMemory" }));

        Assert.Contains("ConnectionStrings:Billing", ex.Message);
    }

    [Fact]
    public async Task Worker_ReconciliaPagamentosPendentesEmSegundoPlano()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBillingDb>(c.Db);
        services.AddSingleton<IPagamentoGateway>(c.Mock);
        services.AddSingleton<IPublishEndpoint>(new RegistradorPublish(c.Publicadas));
        services.AddSingleton<TimeProvider>(c.Relogio);
        services.AddScoped<PagamentoService>();
        await using var provider = services.BuildServiceProvider();

        var worker = new PagamentoReconciliacaoWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PagamentoOptions { ReconciliacaoIntervaloSegundos = 1 }),
            NullLogger<PagamentoReconciliacaoWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 40 && !c.Publicadas.OfType<PagamentoConfirmado>().Any(); i++)
            await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(c.Publicadas.OfType<PagamentoConfirmado>());
    }

    [Fact]
    public async Task Worker_ErroNoCicloNaoDerrubaOServico()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // PagamentoService não registrado => cada ciclo falha
        await using var provider = services.BuildServiceProvider();

        var worker = new PagamentoReconciliacaoWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PagamentoOptions { ReconciliacaoIntervaloSegundos = 1 }),
            NullLogger<PagamentoReconciliacaoWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(1500);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.ExecuteTask is { IsFaulted: false });
    }

    [Fact]
    public void Modelo_MapeiaItensDoOrcamentoPeloCampoPrivado()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase("modelo").Options;
        using var db = new BillingDbContext(options);

        var entidade = db.Model.FindEntityType(typeof(global::wrench.billing.domain.Orcamentos.Orcamento))!;

        Assert.NotNull(entidade.FindNavigation(nameof(global::wrench.billing.domain.Orcamentos.Orcamento.Itens)));
    }
}
