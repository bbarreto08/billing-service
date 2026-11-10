using wrench.billing.application.Abstractions;
using wrench.billing.domain.Orcamentos;
using Microsoft.Extensions.Logging;

namespace wrench.billing.infra.Notificacoes;

/// <summary>
/// Notificador provisório: apenas registra o envio. A troca por Amazon SES (IRSA, como no monólito)
/// é uma nova implementação de <see cref="IOrcamentoNotificador"/>.
/// </summary>
public class LogOrcamentoNotificador(ILogger<LogOrcamentoNotificador> logger) : IOrcamentoNotificador
{
    public Task EnviarAsync(Orcamento orcamento, CancellationToken ct)
    {
        logger.LogInformation(
            "Orçamento {OrcamentoId} (OS {OrdemServicoId}) de {Valor:C} enviado para {Email}.",
            orcamento.Id, orcamento.OrdemServicoId, orcamento.ValorTotal, orcamento.ClienteEmail);
        return Task.CompletedTask;
    }
}
