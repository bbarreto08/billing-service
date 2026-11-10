using wrench.billing.application.Abstractions;
using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace wrench.billing.infra.Persistence;

public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options), IBillingDb
{
    public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<WebhookEvento> WebhookEventos => Set<WebhookEvento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Orcamento>(b =>
        {
            b.ToTable("orcamentos");
            b.HasKey(o => o.Id);
            b.Property(o => o.ClienteEmail).HasMaxLength(254).IsRequired();
            b.Property(o => o.CorrelationId).HasMaxLength(100).IsRequired();
            b.Property(o => o.SolucaoProposta).HasMaxLength(2000);
            b.Property(o => o.MotivoRecusa).HasMaxLength(500);
            b.Property(o => o.MotivoCancelamento).HasMaxLength(500);
            b.Property(o => o.ValorMaoDeObra).HasPrecision(18, 2);
            b.Property(o => o.ValorTotal).HasPrecision(18, 2);
            b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(o => o.OrdemServicoId);

            b.OwnsMany(o => o.Itens, i =>
            {
                i.ToTable("orcamento_itens");
                i.WithOwner().HasForeignKey("OrcamentoId");
                i.Property<int>("Id");
                i.HasKey("Id");
                i.Property(x => x.Descricao).HasMaxLength(200).IsRequired();
                i.Property(x => x.ValorUnitario).HasPrecision(18, 2);
                i.Ignore(x => x.Subtotal);
            });
            b.Navigation(o => o.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Pagamento>(b =>
        {
            b.ToTable("pagamentos");
            b.HasKey(p => p.Id);
            b.Property(p => p.ExternalId).HasMaxLength(100).IsRequired();
            b.Property(p => p.PixCopiaECola).HasMaxLength(1000);
            b.Property(p => p.MotivoFalha).HasMaxLength(500);
            b.Property(p => p.Valor).HasPrecision(18, 2);
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            // Um id do provedor só pode existir uma vez: base da idempotência do pagamento.
            b.HasIndex(p => p.ExternalId).IsUnique();
            b.HasIndex(p => p.OrcamentoId);
            b.HasIndex(p => p.OrdemServicoId);
        });

        modelBuilder.Entity<WebhookEvento>(b =>
        {
            b.ToTable("webhook_eventos");
            b.HasKey(e => e.Chave);
            b.Property(e => e.Chave).HasMaxLength(200);
            b.Property(e => e.ExternalPagamentoId).HasMaxLength(100).IsRequired();
        });

        // Tabelas do MassTransit: outbox transacional (publicação) e inbox (deduplicação de consumo).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
