namespace wrench.billing.application.Contracts;

// Contratos locais do serviço. Devem ser substituídos pelo pacote compartilhado
// Wrench.Contracts quando a versão 1 for publicada (mudanças apenas aditivas).
// CorrelationId é string, como no execucao-service.

public record ItemOrcamentoDto(Guid PecaId, string Descricao, int Quantidade, decimal ValorUnitario);

// ---------------------------------------------------------------------------
// Comandos consumidos pelo Billing (enviados pelo orquestrador no OS Service)
// ---------------------------------------------------------------------------

/// <summary>Pede ao Billing a geracao e o envio do orcamento ao cliente.</summary>
public record GerarOrcamento(
    Guid OrdemServicoId,
    string CorrelationId,
    Guid ClienteId,
    string ClienteEmail,
    string SolucaoProposta,
    decimal ValorMaoDeObra,
    IReadOnlyList<ItemOrcamentoDto> Itens);

/// <summary>Compensacao: cancela o orcamento e a cobranca pendente (se houver).</summary>
public record CancelarOrcamento(Guid OrdemServicoId, string CorrelationId, string Motivo);

/// <summary>Compensacao: estorna o pagamento ja confirmado (ex.: Execucao nao aceitou a OS).</summary>
public record EstornarPagamento(Guid OrdemServicoId, string CorrelationId, string Motivo);

// ---------------------------------------------------------------------------
// Eventos publicados pelo Billing
// ---------------------------------------------------------------------------

public record OrcamentoGerado(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, decimal ValorTotal);

public record OrcamentoFalhou(Guid OrdemServicoId, string CorrelationId, string Motivo);

/// <summary>Cliente aprovou; a cobranca Pix foi criada e aguarda pagamento.</summary>
public record OrcamentoAprovado(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, string PixCopiaECola, DateTime ExpiraEm);

public record OrcamentoRecusado(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, string Motivo);

public record OrcamentoCancelado(Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, string Motivo);

public record PagamentoConfirmado(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, decimal Valor);

public record PagamentoFalhou(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, string Motivo);

public record PagamentoEstornado(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, decimal Valor);

/// <summary>O estorno falhou; exige intervencao (vai tambem para alerta/DLQ).</summary>
public record EstornoFalhou(
    Guid OrdemServicoId, string CorrelationId, Guid OrcamentoId, string Motivo);

