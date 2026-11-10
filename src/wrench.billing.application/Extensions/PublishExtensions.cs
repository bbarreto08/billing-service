using MassTransit;

namespace wrench.billing.application.Extensions;

public static class PublishExtensions
{
    public const string HeaderCorrelationId = "correlationId";

    /// <summary>
    /// Publica a mensagem propagando o <c>correlationId</c> da Saga. Ele vai sempre no header
    /// <c>correlationId</c> (texto livre, como no execucao-service) e, quando for um GUID,
    /// também como CorrelationId do transporte do MassTransit.
    /// </summary>
    public static Task PublishComCorrelacao<T>(
        this IPublishEndpoint publisher, T mensagem, string correlationId, CancellationToken ct)
        where T : class
        => publisher.Publish(mensagem, ctx =>
        {
            if (Guid.TryParse(correlationId, out var guid))
                ctx.CorrelationId = guid;

            if (!string.IsNullOrWhiteSpace(correlationId))
                ctx.Headers.Set(HeaderCorrelationId, correlationId);
        }, ct);
}