using wrench.billing.domain.Orcamentos;

namespace wrench.billing.application.Abstractions;

/// <summary>Envia o orçamento ao cliente (e-mail). Falhas aqui não devem derrubar a Saga.</summary>
public interface IOrcamentoNotificador
{
    Task EnviarAsync(Orcamento orcamento, CancellationToken ct);
}
