using wrench.billing.application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using wrench.billing.application.Services;
using wrench.billing.application.Options;

namespace wrench.billing.infra.Pagamentos;

/// <summary>
/// Consulta ativa dos pagamentos pendentes: plano B caso o webhook não chegue e
/// responsável por expirar cobranças Pix vencidas (dispara <c>PagamentoFalhou</c>).
/// </summary>
public class PagamentoReconciliacaoWorker(
    IServiceScopeFactory escopos,
    IOptions<PagamentoOptions> opcoes,
    ILogger<PagamentoReconciliacaoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(1, opcoes.Value.ReconciliacaoIntervaloSegundos));
        using var timer = new PeriodicTimer(intervalo);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                var servico = escopo.ServiceProvider.GetRequiredService<PagamentoService>();
                var alterados = await servico.ReconciliarPendentesAsync(stoppingToken);

                if (alterados > 0)
                    logger.LogInformation("Reconciliação atualizou {Quantidade} pagamento(s).", alterados);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na reconciliação de pagamentos; nova tentativa no próximo ciclo.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
