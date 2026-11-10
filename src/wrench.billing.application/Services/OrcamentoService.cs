using wrench.billing.application.Abstractions;
using wrench.billing.domain;
using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using wrench.billing.application.Contracts;
using wrench.billing.application.Responses;
using wrench.billing.application.Options;
using wrench.billing.application.Exceptions;
using wrench.billing.application.Extensions;

namespace wrench.billing.application.Services;

/// <summary>
/// Casos de uso do orçamento. Cada mudança de estado publica o evento da Saga na mesma
/// transação (outbox transacional), então estado e mensagem nunca divergem.
/// </summary>
public class OrcamentoService(
    IBillingDb db,
    IPagamentoGateway gateway,
    IOrcamentoNotificador notificador,
    IPublishEndpoint publisher,
    TimeProvider relogio,
    IOptions<PagamentoOptions> opcoes,
    ILogger<OrcamentoService> logger)
{
    /// <summary>Comando <see cref="GerarOrcamento"/>: cria o orçamento e o envia ao cliente.</summary>
    public async Task GerarAsync(GerarOrcamento comando, CancellationToken ct)
    {
        var existente = await db.Orcamentos.AsNoTracking()
            .Where(o => o.OrdemServicoId == comando.OrdemServicoId && o.Status != OrcamentoStatus.Cancelado)
            .OrderByDescending(o => o.CriadoEm)
            .FirstOrDefaultAsync(ct);

        if (existente is not null)
        {
            // Reentrega do comando: devolve o mesmo resultado, sem criar outro orçamento.
            logger.LogInformation("Orçamento da OS {OrdemServicoId} já existe; reenviando evento.", comando.OrdemServicoId);
            await publisher.PublishComCorrelacao(
                new OrcamentoGerado(existente.OrdemServicoId, comando.CorrelationId, existente.Id, existente.ValorTotal),
                comando.CorrelationId, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        Orcamento orcamento;
        try
        {
            var itens = (comando.Itens ?? [])
                .Select(i => new ItemOrcamento(i.PecaId, i.Descricao, i.Quantidade, i.ValorUnitario));

            orcamento = Orcamento.Criar(
                comando.OrdemServicoId, comando.CorrelationId, comando.ClienteId, comando.ClienteEmail,
                comando.SolucaoProposta, comando.ValorMaoDeObra, itens, relogio.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "Orçamento da OS {OrdemServicoId} rejeitado: {Motivo}", comando.OrdemServicoId, ex.Message);
            await publisher.PublishComCorrelacao(
                new OrcamentoFalhou(comando.OrdemServicoId, comando.CorrelationId, ex.Message),
                comando.CorrelationId, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        db.Orcamentos.Add(orcamento);
        await publisher.PublishComCorrelacao(
            new OrcamentoGerado(orcamento.OrdemServicoId, orcamento.CorrelationId, orcamento.Id, orcamento.ValorTotal),
            orcamento.CorrelationId, ct);
        await db.SaveChangesAsync(ct);

        await EnviarAoClienteAsync(orcamento, ct);
    }

    /// <summary>O cliente aprova: cria a cobrança Pix e publica <see cref="OrcamentoAprovado"/>.</summary>
    public async Task<PagamentoResponse> AprovarAsync(Guid orcamentoId, CancellationToken ct)
    {
        var orcamento = await ObterOuFalharAsync(orcamentoId, ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var mudou = orcamento.Aprovar(agora);

        if (!mudou)
        {
            // Aprovação repetida: devolve a cobrança já criada.
            var existente = await db.Pagamentos.AsNoTracking()
                .Where(p => p.OrcamentoId == orcamentoId)
                .OrderByDescending(p => p.CriadoEm)
                .FirstOrDefaultAsync(ct);

            return existente is null
                ? throw new DomainException("Orçamento aprovado sem cobrança associada.")
                : PagamentoResponse.De(existente);
        }

        var expiraEm = agora.AddMinutes(opcoes.Value.ExpiracaoPixMinutos);

        // Chamada externa antes de persistir: se falhar, nada é gravado e o cliente pode tentar de novo.
        // O gateway usa o orçamento como chave de idempotência.
        var cobranca = await gateway.CriarCobrancaPixAsync(new CriarCobrancaRequest(
            orcamento.Id, orcamento.OrdemServicoId, orcamento.ValorTotal, orcamento.ClienteEmail,
            $"Ordem de serviço {orcamento.OrdemServicoId}", expiraEm), ct);

        var pagamento = Pagamento.Criar(
            orcamento.Id, orcamento.OrdemServicoId, cobranca.ExternalId, orcamento.ValorTotal,
            cobranca.PixCopiaECola, cobranca.ExpiraEm, agora);

        db.Pagamentos.Add(pagamento);
        await publisher.PublishComCorrelacao(
            new OrcamentoAprovado(
                orcamento.OrdemServicoId, orcamento.CorrelationId, orcamento.Id,
                pagamento.PixCopiaECola, pagamento.ExpiraEm),
            orcamento.CorrelationId, ct);
        await db.SaveChangesAsync(ct);

        return PagamentoResponse.De(pagamento);
    }

    /// <summary>O cliente recusa: publica <see cref="OrcamentoRecusado"/>; o orquestrador cancela a OS.</summary>
    public async Task<OrcamentoResponse> RecusarAsync(Guid orcamentoId, string motivo, CancellationToken ct)
    {
        var orcamento = await ObterOuFalharAsync(orcamentoId, ct);

        var motivoFinal = string.IsNullOrWhiteSpace(motivo) ? "Não informado" : motivo.Trim();

        if (orcamento.Recusar(motivoFinal, relogio.GetUtcNow().UtcDateTime))
        {
            await publisher.PublishComCorrelacao(
                new OrcamentoRecusado(orcamento.OrdemServicoId, orcamento.CorrelationId, orcamento.Id, motivoFinal),
                orcamento.CorrelationId, ct);
            await db.SaveChangesAsync(ct);
        }

        return OrcamentoResponse.De(orcamento);
    }

    /// <summary>
    /// Compensação <see cref="CancelarOrcamento"/>: cancela o orçamento e a cobrança pendente.
    /// Lança <see cref="DomainException"/> se o orçamento já foi pago (nesse caso o caminho é o estorno).
    /// </summary>
    public async Task CancelarAsync(CancelarOrcamento comando, CancellationToken ct)
    {
        var orcamento = await db.Orcamentos
            .Include(o => o.Itens)
            .Where(o => o.OrdemServicoId == comando.OrdemServicoId)
            .OrderByDescending(o => o.CriadoEm)
            .FirstOrDefaultAsync(ct);

        if (orcamento is null)
        {
            logger.LogWarning("Cancelamento ignorado: OS {OrdemServicoId} não possui orçamento.", comando.OrdemServicoId);
            return;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;

        // Cancela a cobrança pendente (se houver) antes de cancelar o orçamento.
        var pendentes = await db.Pagamentos
            .Where(p => p.OrcamentoId == orcamento.Id && p.Status == PagamentoStatus.Pendente)
            .ToListAsync(ct);

        foreach (var pagamento in pendentes)
        {
            await gateway.CancelarCobrancaAsync(pagamento.ExternalId, ct);
            pagamento.Cancelar(agora);
        }

        orcamento.Cancelar(comando.Motivo, agora);

        await publisher.PublishComCorrelacao(
            new OrcamentoCancelado(orcamento.OrdemServicoId, comando.CorrelationId, orcamento.Id, comando.Motivo),
            comando.CorrelationId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OrcamentoResponse> ObterAsync(Guid id, CancellationToken ct)
        => OrcamentoResponse.De(await db.Orcamentos.AsNoTracking().Include(o => o.Itens)
               .FirstOrDefaultAsync(o => o.Id == id, ct)
           ?? throw new NaoEncontradoException($"Orçamento {id} não encontrado."));

    public async Task<OrcamentoResponse> ObterPorOrdemServicoAsync(Guid ordemServicoId, CancellationToken ct)
        => OrcamentoResponse.De(await db.Orcamentos.AsNoTracking().Include(o => o.Itens)
               .Where(o => o.OrdemServicoId == ordemServicoId)
               .OrderByDescending(o => o.CriadoEm)
               .FirstOrDefaultAsync(ct)
           ?? throw new NaoEncontradoException($"A ordem de serviço {ordemServicoId} não possui orçamento."));

    private async Task<Orcamento> ObterOuFalharAsync(Guid id, CancellationToken ct)
        => await db.Orcamentos.Include(o => o.Itens).FirstOrDefaultAsync(o => o.Id == id, ct)
           ?? throw new NaoEncontradoException($"Orçamento {id} não encontrado.");

    private async Task EnviarAoClienteAsync(Orcamento orcamento, CancellationToken ct)
    {
        try
        {
            await notificador.EnviarAsync(orcamento, ct);
        }
        catch (Exception ex)
        {
            // O envio do e-mail não pode derrubar a Saga: o orçamento já existe e o cliente o consulta pela API.
            logger.LogError(ex, "Falha ao enviar o orçamento {OrcamentoId} ao cliente.", orcamento.Id);
        }
    }
}
