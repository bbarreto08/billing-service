using wrench.billing.domain.Pagamentos;

namespace wrench.billing.application.Responses;

public record PagamentoResponse(
    Guid Id,
    Guid OrcamentoId,
    Guid OrdemServicoId,
    string ExternalId,
    decimal Valor,
    string Status,
    string PixCopiaECola,
    DateTime ExpiraEm,
    string? MotivoFalha)
{
    public static PagamentoResponse De(Pagamento p) => new(
        p.Id, p.OrcamentoId, p.OrdemServicoId, p.ExternalId, p.Valor,
        p.Status.ToString(), p.PixCopiaECola, p.ExpiraEm, p.MotivoFalha);
}
