namespace wrench.billing.domain.Orcamentos;

public class ItemOrcamento
{
    private ItemOrcamento() { }

    public ItemOrcamento(Guid pecaId, string descricao, int quantidade, decimal valorUnitario)
    {
        if (string.IsNullOrWhiteSpace(descricao))
            throw new DomainException("A descrição do item é obrigatória.");
        if (quantidade <= 0)
            throw new DomainException("A quantidade do item deve ser maior que zero.");
        if (valorUnitario < 0)
            throw new DomainException("O valor unitário do item não pode ser negativo.");

        PecaId = pecaId;
        Descricao = descricao;
        Quantidade = quantidade;
        ValorUnitario = valorUnitario;
    }

    public Guid PecaId { get; private set; }
    public string Descricao { get; private set; } = string.Empty;
    public int Quantidade { get; private set; }
    public decimal ValorUnitario { get; private set; }

    public decimal Subtotal => Quantidade * ValorUnitario;
}
