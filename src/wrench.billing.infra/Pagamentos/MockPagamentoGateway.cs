using System.Collections.Concurrent;
using wrench.billing.application.Abstractions;
using Microsoft.Extensions.Options;

namespace wrench.billing.infra.Pagamentos;

public class MockGatewayOptions
{
    public const string Secao = "Pagamento:Mock";

    /// <summary>Força a recusa do estorno (usado para demonstrar o <c>EstornoFalhou</c>).</summary>
    public bool FalharEstorno { get; set; }
}

/// <summary>
/// Substituto do Mercado Pago. Guarda o estado das cobranças em memória (singleton, 1 réplica) e
/// permite simular o resultado do pagamento. O estado local do Billing continua no banco;
/// após um restart, cobranças desconhecidas são tratadas como pendentes na consulta.
/// A implementação real deve ser outra classe atrás de <see cref="IPagamentoGateway"/>.
/// </summary>
public class MockPagamentoGateway(IOptionsMonitor<MockGatewayOptions> opcoes) : IPagamentoGateway
{
    private readonly ConcurrentDictionary<string, StatusProvedor> _cobrancas = new();

    public Task<CobrancaCriada> CriarCobrancaPixAsync(CriarCobrancaRequest request, CancellationToken ct)
    {
        // Idempotente: o mesmo orçamento sempre gera o mesmo id de cobrança.
        var externalId = $"mock-{request.OrcamentoId:N}";
        _cobrancas.TryAdd(externalId, StatusProvedor.Pendente);

        var pix = $"00020126MOCK{externalId}5204000053039865406{request.Valor:0.00}5802BR6304ABCD";
        return Task.FromResult(new CobrancaCriada(externalId, pix, request.ExpiraEm));
    }

    public Task<StatusProvedor> ConsultarStatusAsync(string externalId, CancellationToken ct)
        => Task.FromResult(_cobrancas.GetValueOrDefault(externalId, StatusProvedor.Pendente));

    public Task CancelarCobrancaAsync(string externalId, CancellationToken ct)
    {
        _cobrancas.AddOrUpdate(externalId, StatusProvedor.Cancelado,
            (_, atual) => atual == StatusProvedor.Pendente ? StatusProvedor.Cancelado : atual);
        return Task.CompletedTask;
    }

    public Task<ResultadoEstorno> EstornarAsync(string externalId, decimal valor, CancellationToken ct)
    {
        if (opcoes.CurrentValue.FalharEstorno)
            return Task.FromResult(new ResultadoEstorno(false, "Estorno recusado (simulação de falha)."));

        var atual = _cobrancas.GetValueOrDefault(externalId, StatusProvedor.Aprovado);
        if (atual is not (StatusProvedor.Aprovado or StatusProvedor.Estornado))
            return Task.FromResult(new ResultadoEstorno(false, $"Pagamento {atual} não pode ser estornado."));

        _cobrancas[externalId] = StatusProvedor.Estornado;
        return Task.FromResult(new ResultadoEstorno(true));
    }

    /// <summary>Define o resultado que o "provedor" informará na próxima consulta.</summary>
    public void Simular(string externalId, StatusProvedor status) => _cobrancas[externalId] = status;
}
