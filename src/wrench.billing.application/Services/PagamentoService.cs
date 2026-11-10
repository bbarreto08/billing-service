using wrench.billing.application.Abstractions;
using wrench.billing.domain;
using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using wrench.billing.application.Contracts;
using wrench.billing.application.Responses;
using wrench.billing.application.Exceptions;
using wrench.billing.application.Extensions;

namespace wrench.billing.application.Services;

public enum ResultadoWebhook
{
    Processado = 1,
    Duplicado = 2,
    PagamentoDesconhecido = 3
}

public class PagamentoService(
    IBillingDb db,
    IPagamentoGateway gateway,
    IPublishEndpoint publisher,
    TimeProvider relogio,
    ILogger<PagamentoService> logger)
{
    /// <summary>
    /// Processa a notificação do provedor. O corpo do webhook não é confiável para decidir o status:
    /// consultamos o pagamento no provedor e sincronizamos o estado local a partir dele.
    /// </summary>
    public async Task<ResultadoWebhook> ProcessarNotificacaoAsync(
        string externalId, string chaveEvento, CancellationToken ct)
    {
        if (await db.WebhookEventos.AnyAsync(e => e.Chave == chaveEvento, ct))
            return ResultadoWebhook.Duplicado;

        db.WebhookEventos.Add(new WebhookEvento(chaveEvento, externalId, relogio.GetUtcNow().UtcDateTime));

        var pagamento = await db.Pagamentos.FirstOrDefaultAsync(p => p.ExternalId == externalId, ct);
        if (pagamento is null)
        {
            logger.LogWarning("Webhook para pagamento desconhecido {ExternalId}.", externalId);
            await db.SaveChangesAsync(ct);
            return ResultadoWebhook.PagamentoDesconhecido;
        }

        var status = await gateway.ConsultarStatusAsync(externalId, ct);
        await SincronizarAsync(pagamento, status, ct);
        await db.SaveChangesAsync(ct);

        return ResultadoWebhook.Processado;
    }

    public async Task<PagamentoResponse> ObterPorOrcamentoAsync(Guid orcamentoId, CancellationToken ct)
        => PagamentoResponse.De(await db.Pagamentos.AsNoTracking()
               .Where(p => p.OrcamentoId == orcamentoId)
               .OrderByDescending(p => p.CriadoEm)
               .FirstOrDefaultAsync(ct)
           ?? throw new NaoEncontradoException($"O orçamento {orcamentoId} não possui pagamento."));

    /// <summary>
    /// Plano B do webhook: consulta ativamente os pagamentos pendentes e expira os vencidos.
    /// Retorna quantos pagamentos mudaram de estado.
    /// </summary>
    public async Task<int> ReconciliarPendentesAsync(CancellationToken ct)
    {
        var pendentes = await db.Pagamentos
            .Where(p => p.Status == PagamentoStatus.Pendente)
            .OrderBy(p => p.CriadoEm)
            .Take(100)
            .ToListAsync(ct);

        var alterados = 0;

        foreach (var pagamento in pendentes)
        {
            var status = await gateway.ConsultarStatusAsync(pagamento.ExternalId, ct);

            if (status == StatusProvedor.Pendente && pagamento.EstaVencido(relogio.GetUtcNow().UtcDateTime))
                status = StatusProvedor.Expirado;

            if (await SincronizarAsync(pagamento, status, ct))
                alterados++;
        }

        if (alterados > 0)
            await db.SaveChangesAsync(ct);

        return alterados;
    }

    /// <summary>
    /// Compensação <see cref="EstornarPagamento"/>: devolve o valor ao cliente.
    /// Recusa definitiva do provedor vira <see cref="EstornoFalhou"/>; falha transitória lança exceção (retry/DLQ).
    /// </summary>
    public async Task EstornarAsync(EstornarPagamento comando, CancellationToken ct)
    {
        var pagamento = await db.Pagamentos
            .Where(p => p.OrdemServicoId == comando.OrdemServicoId
                        && (p.Status == PagamentoStatus.Aprovado || p.Status == PagamentoStatus.Estornado))
            .OrderByDescending(p => p.CriadoEm)
            .FirstOrDefaultAsync(ct);

        if (pagamento is null)
        {
            await publisher.PublishComCorrelacao(
                new EstornoFalhou(comando.OrdemServicoId, comando.CorrelationId, Guid.Empty,
                    "Nenhum pagamento aprovado encontrado para a ordem de serviço."),
                comando.CorrelationId, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        if (pagamento.Status == PagamentoStatus.Estornado)
        {
            // Reentrega: o estorno já foi feito; reafirma o resultado para a Saga.
            await publisher.PublishComCorrelacao(
                new PagamentoEstornado(pagamento.OrdemServicoId, comando.CorrelationId, pagamento.OrcamentoId, pagamento.Valor),
                comando.CorrelationId, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        var resultado = await gateway.EstornarAsync(pagamento.ExternalId, pagamento.Valor, ct);

        if (!resultado.Sucesso)
        {
            logger.LogError("Estorno recusado para a OS {OrdemServicoId}: {Erro}", comando.OrdemServicoId, resultado.Erro);
            await publisher.PublishComCorrelacao(
                new EstornoFalhou(pagamento.OrdemServicoId, comando.CorrelationId, pagamento.OrcamentoId,
                    resultado.Erro ?? "Estorno recusado pelo provedor."),
                comando.CorrelationId, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        pagamento.Estornar(agora);

        var orcamento = await db.Orcamentos.FirstOrDefaultAsync(o => o.Id == pagamento.OrcamentoId, ct);
        orcamento?.Estornar(agora);

        await publisher.PublishComCorrelacao(
            new PagamentoEstornado(pagamento.OrdemServicoId, comando.CorrelationId, pagamento.OrcamentoId, pagamento.Valor),
            comando.CorrelationId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Aplica o status do provedor ao pagamento local. Retorna <c>true</c> se houve mudança.</summary>
    private async Task<bool> SincronizarAsync(Pagamento pagamento, StatusProvedor status, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;

        try
        {
            switch (status)
            {
                case StatusProvedor.Aprovado:
                    if (!pagamento.Aprovar(agora)) return false;
                    var orcamento = await db.Orcamentos.FirstAsync(o => o.Id == pagamento.OrcamentoId, ct);
                    orcamento.MarcarComoPago(agora);
                    await publisher.PublishComCorrelacao(
                        new PagamentoConfirmado(pagamento.OrdemServicoId, orcamento.CorrelationId,
                            pagamento.OrcamentoId, pagamento.Valor),
                        orcamento.CorrelationId, ct);
                    return true;

                case StatusProvedor.Recusado:
                case StatusProvedor.Cancelado:
                    if (!pagamento.Recusar($"Pagamento {status.ToString().ToLowerInvariant()} pelo provedor.", agora)) return false;
                    await PublicarFalhaAsync(pagamento, agora, ct);
                    return true;

                case StatusProvedor.Expirado:
                    if (!pagamento.Expirar(agora)) return false;
                    await PublicarFalhaAsync(pagamento, agora, ct);
                    return true;

                default:
                    return false;
            }
        }
        catch (DomainException ex)
        {
            // Ex.: provedor informa "aprovado" para um pagamento que já foi cancelado/expirado localmente.
            // Exige intervenção (possível estorno manual); não pode ser silenciado.
            logger.LogError(ex,
                "Estado do provedor ({Status}) incompatível com o pagamento {PagamentoId} ({Local}).",
                status, pagamento.Id, pagamento.Status);
            return false;
        }
    }

    /// <summary>Pagamento recusado/expirado: o Billing cancela a cobrança e o orçamento e avisa a Saga.</summary>
    private async Task PublicarFalhaAsync(Pagamento pagamento, DateTime agora, CancellationToken ct)
    {
        var orcamento = await db.Orcamentos.FirstAsync(o => o.Id == pagamento.OrcamentoId, ct);
        orcamento.Cancelar(pagamento.MotivoFalha ?? "Pagamento não concluído.", agora);

        var correlacao = orcamento.CorrelationId;
        await publisher.PublishComCorrelacao(
            new PagamentoFalhou(pagamento.OrdemServicoId, correlacao, pagamento.OrcamentoId,
                pagamento.MotivoFalha ?? "Pagamento não concluído."),
            correlacao, ct);
    }
}
