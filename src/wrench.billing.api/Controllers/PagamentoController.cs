using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using wrench.billing.application;
using wrench.billing.application.Services;
using wrench.billing.application.Responses;
using wrench.billing.application.Options;
using wrench.billing.application.Webhooks;

namespace wrench.billing.api.Controllers;

/// <summary>
/// Pagamento do orçamento (Pix) e recebimento das notificações do provedor.
/// </summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
public class PagamentoController(
    PagamentoService servico,
    IOptions<PagamentoOptions> opcoes,
    ILogger<PagamentoController> logger) : ControllerBase
{
    /// <summary>
    /// Consulta o pagamento (Pix) de um orçamento.
    /// </summary>
    [HttpGet("orcamento/{orcamentoId:guid}")]
    [Authorize(Roles = "Admin,Funcionario,Cliente")]
    [ProducesResponseType<PagamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorOrcamento([FromRoute] Guid orcamentoId, CancellationToken cancellationToken)
        => Ok(await servico.ObterPorOrcamentoAsync(orcamentoId, cancellationToken));

    /// <summary>
    /// Webhook do provedor de pagamento. Público; a autenticidade é garantida pelo header x-signature.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReceberWebhook(CancellationToken cancellationToken)
    {
        var notificacao = await LerNotificacaoAsync(cancellationToken);
        var xSignature = Request.Headers["x-signature"].FirstOrDefault();
        var xRequestId = Request.Headers["x-request-id"].FirstOrDefault();

        // O data.id da query é o que o Mercado Pago usa para assinar; o corpo é só fallback.
        var dataId = Request.Query["data.id"].FirstOrDefault() ?? notificacao.DataId;

        if (!AssinaturaWebhookMercadoPago.Validar(opcoes.Value.WebhookSecret, xSignature, xRequestId, dataId))
        {
            logger.LogWarning("Webhook rejeitado: assinatura inválida (request-id {RequestId}).", xRequestId);
            return Unauthorized();
        }

        // Só tratamos eventos de pagamento; os demais são confirmados para o provedor não reenviar.
        var tipo = notificacao.Tipo ?? Request.Query["type"].FirstOrDefault();
        if (!string.Equals(tipo, "payment", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(dataId))
            return Ok();

        var chave = !string.IsNullOrWhiteSpace(xRequestId) ? xRequestId : $"{dataId}:{ExtrairTs(xSignature)}";

        await servico.ProcessarNotificacaoAsync(dataId, chave, cancellationToken);
        return Ok();
    }

    private async Task<Notificacao> LerNotificacaoAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            var raiz = doc.RootElement;

            var tipo = raiz.TryGetProperty("type", out var t) ? t.GetString() : null;
            string? id = null;
            if (raiz.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var i))
                id = i.ValueKind == JsonValueKind.String ? i.GetString() : i.ToString();

            return new Notificacao(tipo, id);
        }
        catch (JsonException)
        {
            return new Notificacao(null, null);
        }
    }

    private static string? ExtrairTs(string? xSignature)
        => xSignature?.Split(',')
            .Select(p => p.Split('=', 2))
            .FirstOrDefault(p => p.Length == 2 && p[0].Trim() == "ts")?[1].Trim();

    private sealed record Notificacao(string? Tipo, string? DataId);
}