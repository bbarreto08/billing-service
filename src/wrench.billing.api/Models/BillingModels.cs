namespace wrench.billing.api.Models;

/// <summary>Motivo informado pelo cliente ao recusar o orçamento (opcional).</summary>
public sealed record RecusarOrcamentoRequest(string? MotivoRecusa);

public sealed record SimulacaoPagamentoResponse(int WebhookStatus, string Resultado);