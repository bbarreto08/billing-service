using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wrench.billing.api.Models;
using wrench.billing.application;
using wrench.billing.application.Services;
using wrench.billing.application.Responses;

namespace wrench.billing.api.Controllers;

/// <summary>
/// Orçamento da ordem de serviço: consulta e decisão do cliente (aprovar ou recusar).
/// </summary>
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
public class OrcamentoController(OrcamentoService servico) : ControllerBase
{
    /// <summary>
    /// Consulta um orçamento.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Funcionario,Cliente")]
    [ProducesResponseType<OrcamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter([FromRoute] Guid id, CancellationToken cancellationToken)
        => Ok(await servico.ObterAsync(id, cancellationToken));

    /// <summary>
    /// Consulta o orçamento mais recente de uma ordem de serviço.
    /// </summary>
    [HttpGet("ordem-servico/{ordemServicoId:guid}")]
    [Authorize(Roles = "Admin,Funcionario,Cliente")]
    [ProducesResponseType<OrcamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorOrdemServico([FromRoute] Guid ordemServicoId, CancellationToken cancellationToken)
        => Ok(await servico.ObterPorOrdemServicoAsync(ordemServicoId, cancellationToken));

    /// <summary>
    /// Aprova o orçamento e devolve a cobrança Pix para pagamento.
    /// </summary>
    [HttpPut("{id:guid}/aprovar")]
    [Authorize(Roles = "Cliente")]
    [ProducesResponseType<PagamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aprovar([FromRoute] Guid id, CancellationToken cancellationToken)
        => Ok(await servico.AprovarAsync(id, cancellationToken));

    /// <summary>
    /// Recusa o orçamento. A Saga cancela a ordem de serviço.
    /// </summary>
    [HttpPut("{id:guid}/recusar")]
    [Authorize(Roles = "Cliente")]
    [ProducesResponseType<OrcamentoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Recusar(
        [FromRoute] Guid id,
        [FromBody] RecusarOrcamentoRequest? request,
        CancellationToken cancellationToken)
        => Ok(await servico.RecusarAsync(id, request?.MotivoRecusa ?? string.Empty, cancellationToken));
}