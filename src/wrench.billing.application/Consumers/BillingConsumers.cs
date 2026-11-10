using MassTransit;
using wrench.billing.application.Contracts;
using wrench.billing.application.Services;

namespace wrench.billing.application.Consumers;

// Consumers finos: toda a regra fica nos serviços. A idempotência vem do inbox do MassTransit
// (deduplicação por MessageId) e dos próprios serviços, que toleram reprocessamento.

public class GerarOrcamentoConsumer(OrcamentoService servico) : IConsumer<GerarOrcamento>
{
    public Task Consume(ConsumeContext<GerarOrcamento> context)
        => servico.GerarAsync(context.Message, context.CancellationToken);
}

public class CancelarOrcamentoConsumer(OrcamentoService servico) : IConsumer<CancelarOrcamento>
{
    public Task Consume(ConsumeContext<CancelarOrcamento> context)
        => servico.CancelarAsync(context.Message, context.CancellationToken);
}

public class EstornarPagamentoConsumer(PagamentoService servico) : IConsumer<EstornarPagamento>
{
    public Task Consume(ConsumeContext<EstornarPagamento> context)
        => servico.EstornarAsync(context.Message, context.CancellationToken);
}
