namespace wrench.billing.domain.Pagamentos;

/// <summary>
/// Registro de notificações de webhook já processadas. A chave primária garante
/// a idempotência: o provedor reenvia notificações e cada uma só pode ter efeito uma vez.
/// </summary>
public class WebhookEvento
{
    private WebhookEvento() { }

    public WebhookEvento(string chave, string externalPagamentoId, DateTime recebidoEm)
    {
        Chave = chave;
        ExternalPagamentoId = externalPagamentoId;
        RecebidoEm = recebidoEm;
    }

    public string Chave { get; private set; } = string.Empty;
    public string ExternalPagamentoId { get; private set; } = string.Empty;
    public DateTime RecebidoEm { get; private set; }
}
