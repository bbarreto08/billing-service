namespace wrench.billing.application.Abstractions;

public enum StatusProvedor
{
    Pendente = 1,
    Aprovado = 2,
    Recusado = 3,
    Expirado = 4,
    Cancelado = 5,
    Estornado = 6
}

public record CriarCobrancaRequest(
    Guid OrcamentoId,
    Guid OrdemServicoId,
    decimal Valor,
    string EmailPagador,
    string Descricao,
    DateTime ExpiraEm);

public record CobrancaCriada(string ExternalId, string PixCopiaECola, DateTime ExpiraEm);

public record ResultadoEstorno(bool Sucesso, string? Erro = null);

/// <summary>
/// Porta para o provedor de pagamento (Mercado Pago). Hoje a implementação é um mock;
/// a implementação real deve usar o orçamento como chave de idempotência (X-Idempotency-Key).
/// </summary>
public interface IPagamentoGateway
{
    /// <summary>Cria a cobrança Pix. Deve ser idempotente para o mesmo <c>OrcamentoId</c>.</summary>
    Task<CobrancaCriada> CriarCobrancaPixAsync(CriarCobrancaRequest request, CancellationToken ct);

    Task<StatusProvedor> ConsultarStatusAsync(string externalId, CancellationToken ct);

    Task CancelarCobrancaAsync(string externalId, CancellationToken ct);

    /// <summary>
    /// Retorna <c>Sucesso = false</c> quando o provedor recusa definitivamente o estorno.
    /// Falhas transitórias devem lançar exceção para o retry da mensageria.
    /// </summary>
    Task<ResultadoEstorno> EstornarAsync(string externalId, decimal valor, CancellationToken ct);
}
