using System.Net.Http.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using wrench.billing.api.Models;
using wrench.billing.api.Options;
using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.infra.Pagamentos;
using wrench.billing.application.Services;
using wrench.billing.application.Options;
using wrench.billing.application.Webhooks;

namespace wrench.billing.api.Controllers;

/// <summary>
/// Simulação do provedor de pagamento (somente com o gateway mock e a opção Simulacao:Habilitada).
/// Define o resultado no mock e dispara o MESMO webhook público, com assinatura válida.
/// </summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,Funcionario")]
public class SimulacaoController(
    PagamentoService pagamentos,
    IPagamentoGateway gateway,
    IHttpClientFactory httpFactory,
    IOptions<SimulacaoOptions> simulacao,
    IOptions<PagamentoOptions> pagamentoOpcoes) : ControllerBase
{
    public const string ClienteWebhook = "webhook-self";

    /// <summary>
    /// Simula o resultado do pagamento de um orçamento aprovado.
    /// </summary>
    /// <param name="orcamentoId">Orçamento aprovado (com cobrança criada).</param>
    /// <param name="resultado">aprovado, recusado ou expirado.</param>
    [HttpPost("pagamentos/{orcamentoId:guid}/{resultado}")]
    [ProducesResponseType<SimulacaoPagamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Simular(
        [FromRoute] Guid orcamentoId,
        [FromRoute] string resultado,
        CancellationToken cancellationToken)
    {
        if (!simulacao.Value.Habilitada || gateway is not MockPagamentoGateway mock)
            return Problem("Simulação indisponível neste ambiente.", statusCode: StatusCodes.Status404NotFound);

        StatusProvedor? status = resultado.ToLowerInvariant() switch
        {
            "aprovado" => StatusProvedor.Aprovado,
            "recusado" => StatusProvedor.Recusado,
            "expirado" => StatusProvedor.Expirado,
            _ => null
        };

        if (status is null)
            return Problem("Resultado inválido. Use: aprovado, recusado ou expirado.",
                statusCode: StatusCodes.Status400BadRequest);

        var pagamento = await pagamentos.ObterPorOrcamentoAsync(orcamentoId, cancellationToken);
        mock.Simular(pagamento.ExternalId, status.Value);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var requestId = Guid.NewGuid().ToString();

        using var mensagem = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/pagamento/webhook?data.id={Uri.EscapeDataString(pagamento.ExternalId)}&type=payment")
        {
            Content = JsonContent.Create(new
            {
                action = "payment.updated",
                type = "payment",
                data = new { id = pagamento.ExternalId }
            })
        };
        mensagem.Headers.Add("x-request-id", requestId);
        mensagem.Headers.Add("x-signature",
            AssinaturaWebhookMercadoPago.Assinar(pagamentoOpcoes.Value.WebhookSecret, pagamento.ExternalId, requestId, ts));

        var resposta = await httpFactory.CreateClient(ClienteWebhook).SendAsync(mensagem, cancellationToken);
        return Ok(new SimulacaoPagamentoResponse((int)resposta.StatusCode, status.ToString()!));
    }
}