using wrench.billing.domain.Orcamentos;

namespace wrench.billing.application.Responses;

public record ItemOrcamentoResponse(Guid PecaId, string Descricao, int Quantidade, decimal ValorUnitario, decimal Subtotal);

public record OrcamentoResponse(
    Guid Id,
    Guid OrdemServicoId,
    Guid ClienteId,
    string ClienteEmail,
    string SolucaoProposta,
    decimal ValorMaoDeObra,
    decimal ValorTotal,
    string Status,
    string? MotivoRecusa,
    DateTime CriadoEm,
    IReadOnlyList<ItemOrcamentoResponse> Itens)
{
    public static OrcamentoResponse De(Orcamento o) => new(
        o.Id, o.OrdemServicoId, o.ClienteId, o.ClienteEmail, o.SolucaoProposta,
        o.ValorMaoDeObra, o.ValorTotal, o.Status.ToString(), o.MotivoRecusa, o.CriadoEm,
        o.Itens.Select(i => new ItemOrcamentoResponse(
            i.PecaId, i.Descricao, i.Quantidade, i.ValorUnitario, i.Subtotal)).ToList());
}
