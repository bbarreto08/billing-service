using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using wrench.billing.application;
using wrench.billing.domain;
using wrench.billing.application.Exceptions;

namespace wrench.billing.api.Middlewares;

/// <summary>Traduz exceções de negócio em respostas HTTP (404, 409) sem vazar detalhes internos.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, titulo, detalhe) = exception switch
        {
            NaoEncontradoException e => (StatusCodes.Status404NotFound, "Recurso não encontrado", e.Message),
            DomainException e => (StatusCodes.Status409Conflict, "Conflito de estado", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno", "Ocorreu um erro inesperado.")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Erro não tratado em {Metodo} {Caminho}", context.Request.Method, context.Request.Path);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = titulo, Detail = detalhe }, cancellationToken);

        return true;
    }
}