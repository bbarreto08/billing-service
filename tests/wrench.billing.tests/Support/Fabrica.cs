using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.domain.Orcamentos;
using wrench.billing.infra.Pagamentos;
using wrench.billing.infra.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using wrench.billing.application.Contracts;
using wrench.billing.application.Services;
using wrench.billing.application.Options;

namespace wrench.billing.tests.Support;

public class RelogioFixo(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _agora = inicio;
    public override DateTimeOffset GetUtcNow() => _agora;
    public void Avancar(TimeSpan tempo) => _agora = _agora.Add(tempo);
}

/// <summary>Monta os serviços com banco em memória e publisher falso, sem tocar em infraestrutura externa.</summary>
public class Cenario
{
    public static readonly Guid OrdemServicoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string CorrelationId = "corr-OS-0001";

    public Cenario(bool falharEstorno = false)
    {
        Db = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        Mock = new MockPagamentoGateway(Monitor(falharEstorno));
        Notificador = new Mock<IOrcamentoNotificador>();
        Relogio = new RelogioFixo(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        Opcoes = Options.Create(new PagamentoOptions { ExpiracaoPixMinutos = 30, WebhookSecret = "segredo" });

        Publicadas = [];
    }

    public BillingDbContext Db { get; }
    public MockPagamentoGateway Mock { get; }
    public Mock<IOrcamentoNotificador> Notificador { get; }
    public RelogioFixo Relogio { get; }
    public IOptions<PagamentoOptions> Opcoes { get; }
    public List<object> Publicadas { get; }

    public OrcamentoService Orcamentos(IPagamentoGateway? gateway = null) => new(
        Db, gateway ?? Mock, Notificador.Object, new RegistradorPublish(Publicadas), Relogio, Opcoes,
        NullLogger<OrcamentoService>.Instance);

    public PagamentoService Pagamentos(IPagamentoGateway? gateway = null) => new(
        Db, gateway ?? Mock, new RegistradorPublish(Publicadas), Relogio, NullLogger<PagamentoService>.Instance);

    public IEnumerable<T> Eventos<T>() where T : class => Publicadas.OfType<T>();

    public static GerarOrcamento ComandoGerar(
        Guid? ordemServicoId = null, decimal maoDeObra = 100m, string email = "cliente@wrench.com.br")
        => new(ordemServicoId ?? OrdemServicoId, CorrelationId, Guid.NewGuid(), email, "Trocar embreagem",
            maoDeObra, [new ItemOrcamentoDto(Guid.NewGuid(), "Kit embreagem", 2, 150m)]);

    /// <summary>Gera e persiste um orçamento pelo caso de uso real; retorna o id.</summary>
    public async Task<Orcamento> GerarOrcamentoAsync(GerarOrcamento? comando = null)
    {
        comando ??= ComandoGerar();
        await Orcamentos().GerarAsync(comando, CancellationToken.None);
        Publicadas.Clear();
        return await Db.Orcamentos.Include(o => o.Itens).SingleAsync(o => o.OrdemServicoId == comando.OrdemServicoId);
    }

    /// <summary>Gera, aprova e devolve orçamento + id externo da cobrança.</summary>
    public async Task<(Orcamento Orcamento, string ExternalId)> AprovarAsync(GerarOrcamento? comando = null)
    {
        var orcamento = await GerarOrcamentoAsync(comando);
        var pagamento = await Orcamentos().AprovarAsync(orcamento.Id, CancellationToken.None);
        Publicadas.Clear();
        return (orcamento, pagamento.ExternalId);
    }

    private static IOptionsMonitor<MockGatewayOptions> Monitor(bool falharEstorno)
    {
        var monitor = new Mock<IOptionsMonitor<MockGatewayOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MockGatewayOptions { FalharEstorno = falharEstorno });
        return monitor.Object;
    }
}

/// <summary>IPublishEndpoint que apenas registra as mensagens publicadas.</summary>
public class RegistradorPublish(List<object> destino) : IPublishEndpoint
{
    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
        where T : class
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        where T : class
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, CancellationToken cancellationToken = default)
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe,
        CancellationToken cancellationToken = default)
    {
        destino.Add(message);
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        => throw new NotSupportedException();

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
        where T : class
        => throw new NotSupportedException();

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        where T : class
        => throw new NotSupportedException();

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
}

