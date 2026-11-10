namespace wrench.billing.domain.Orcamentos;

public enum OrcamentoStatus
{
    /// <summary>Gerado e enviado ao cliente, aguardando decisão.</summary>
    Gerado = 1,
    Aprovado = 2,
    Recusado = 3,
    Pago = 4,
    Cancelado = 5,
    Estornado = 6
}
