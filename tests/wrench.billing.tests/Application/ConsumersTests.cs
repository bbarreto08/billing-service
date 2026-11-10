using wrench.billing.application.Consumers;
using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using wrench.billing.tests.Support;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using wrench.billing.application.Contracts;

namespace wrench.billing.tests.Application;

public class ConsumersTests
{
    private static ConsumeContext<T> Contexto<T>(T mensagem) where T : class
    {
        var contexto = new Mock<ConsumeContext<T>>();
        contexto.SetupGet(c => c.Message).Returns(mensagem);
        contexto.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return contexto.Object;
    }

    [Fact]
    public async Task GerarOrcamentoConsumer_DelegaParaOServico()
    {
        var c = new Cenario();

        await new GerarOrcamentoConsumer(c.Orcamentos()).Consume(Contexto(Cenario.ComandoGerar()));

        Assert.Single(c.Eventos<OrcamentoGerado>());
    }

    [Fact]
    public async Task CancelarOrcamentoConsumer_DelegaParaOServico()
    {
        var c = new Cenario();
        await c.GerarOrcamentoAsync();

        await new CancelarOrcamentoConsumer(c.Orcamentos()).Consume(Contexto(
            new CancelarOrcamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "saga")));

        Assert.Equal(OrcamentoStatus.Cancelado, (await c.Db.Orcamentos.SingleAsync()).Status);
    }

    [Fact]
    public async Task EstornarPagamentoConsumer_DelegaParaOServico()
    {
        var c = new Cenario();
        await c.AprovarAsync();

        await new EstornarPagamentoConsumer(c.Pagamentos()).Consume(Contexto(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "x")));

        // cobrança ainda pendente => nada a estornar => EstornoFalhou
        Assert.Single(c.Eventos<EstornoFalhou>());
        Assert.Equal(PagamentoStatus.Pendente, (await c.Db.Pagamentos.SingleAsync()).Status);
    }
}
