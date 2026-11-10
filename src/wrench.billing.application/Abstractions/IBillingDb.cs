using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using Microsoft.EntityFrameworkCore;

namespace wrench.billing.application.Abstractions;

/// <summary>Acesso ao banco próprio do Billing (wrench_billing). Nenhum outro serviço o acessa.</summary>
public interface IBillingDb
{
    DbSet<Orcamento> Orcamentos { get; }
    DbSet<Pagamento> Pagamentos { get; }
    DbSet<WebhookEvento> WebhookEventos { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
