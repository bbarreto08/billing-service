using wrench.billing.application;
using wrench.billing.application.Abstractions;
using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using wrench.billing.tests.Support;
using Microsoft.EntityFrameworkCore;
using Moq;
using wrench.billing.application.Contracts;
using wrench.billing.application.Services;

namespace wrench.billing.tests.Application;

public class PagamentoServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Webhook_PagamentoAprovado_ConfirmaPagamentoEMarcaOrcamentoComoPago()
    {
        var c = new Cenario();
        var (orcamento, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);

        var resultado = await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);

        Assert.Equal(ResultadoWebhook.Processado, resultado);
        Assert.Equal(PagamentoStatus.Aprovado, (await c.Db.Pagamentos.SingleAsync()).Status);
        Assert.Equal(OrcamentoStatus.Pago, (await c.Db.Orcamentos.SingleAsync()).Status);
        var evento = Assert.Single(c.Eventos<PagamentoConfirmado>());
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
        Assert.Equal(400m, evento.Valor);
        Assert.Equal(Cenario.CorrelationId, evento.CorrelationId);
    }

    [Fact]
    public async Task Webhook_MesmaNotificacaoDuasVezesTemEfeitoUnico()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);

        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        var segundo = await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);

        Assert.Equal(ResultadoWebhook.Duplicado, segundo);
        Assert.Single(c.Eventos<PagamentoConfirmado>());
    }

    [Fact]
    public async Task Webhook_NotificacaoDiferenteParaPagamentoJaConfirmadoNaoRepublica()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);

        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        var resultado = await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-2", Ct);

        Assert.Equal(ResultadoWebhook.Processado, resultado);
        Assert.Single(c.Eventos<PagamentoConfirmado>());
    }

    [Fact]
    public async Task Webhook_PagamentoRecusado_CancelaOrcamentoEPublicaFalha()
    {
        var c = new Cenario();
        var (orcamento, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Recusado);

        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);

        Assert.Equal(PagamentoStatus.Recusado, (await c.Db.Pagamentos.SingleAsync()).Status);
        Assert.Equal(OrcamentoStatus.Cancelado, (await c.Db.Orcamentos.SingleAsync()).Status);
        var falha = Assert.Single(c.Eventos<PagamentoFalhou>());
        Assert.Equal(orcamento.Id, falha.OrcamentoId);
        Assert.Empty(c.Eventos<PagamentoConfirmado>());
    }

    [Fact]
    public async Task Webhook_CobrancaCanceladaNoProvedorTratadaComoRecusa()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Cancelado);

        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);

        Assert.Single(c.Eventos<PagamentoFalhou>());
    }

    [Fact]
    public async Task Webhook_PagamentoAindaPendenteNaoMudaNada()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();

        var resultado = await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);

        Assert.Equal(ResultadoWebhook.Processado, resultado);
        Assert.Empty(c.Publicadas);
        Assert.Equal(PagamentoStatus.Pendente, (await c.Db.Pagamentos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_PagamentoDesconhecidoEhRegistradoSemErro()
    {
        var c = new Cenario();

        var resultado = await c.Pagamentos().ProcessarNotificacaoAsync("nao-existe", "req-1", Ct);

        Assert.Equal(ResultadoWebhook.PagamentoDesconhecido, resultado);
        Assert.Equal(1, await c.Db.WebhookEventos.CountAsync());
    }

    [Fact]
    public async Task Webhook_AprovadoParaPagamentoJaExpirado_NaoPublicaConfirmacao()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Expirado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        c.Publicadas.Clear();

        c.Mock.Simular(externalId, StatusProvedor.Aprovado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-2", Ct);

        Assert.Empty(c.Publicadas);
        Assert.Equal(PagamentoStatus.Expirado, (await c.Db.Pagamentos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reconciliar_PagamentoVencidoExpiraEPublicaFalha()
    {
        var c = new Cenario();
        await c.AprovarAsync();
        c.Relogio.Avancar(TimeSpan.FromMinutes(31));

        var alterados = await c.Pagamentos().ReconciliarPendentesAsync(Ct);

        Assert.Equal(1, alterados);
        Assert.Equal(PagamentoStatus.Expirado, (await c.Db.Pagamentos.SingleAsync()).Status);
        Assert.Equal(OrcamentoStatus.Cancelado, (await c.Db.Orcamentos.SingleAsync()).Status);
        Assert.Single(c.Eventos<PagamentoFalhou>());
    }

    [Fact]
    public async Task Reconciliar_PagamentoDentroDoPrazoNaoMuda()
    {
        var c = new Cenario();
        await c.AprovarAsync();
        c.Relogio.Avancar(TimeSpan.FromMinutes(5));

        Assert.Equal(0, await c.Pagamentos().ReconciliarPendentesAsync(Ct));
        Assert.Empty(c.Publicadas);
    }

    [Fact]
    public async Task Reconciliar_PlanoBDetectaPagamentoAprovadoSemWebhook()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);

        var alterados = await c.Pagamentos().ReconciliarPendentesAsync(Ct);

        Assert.Equal(1, alterados);
        Assert.Single(c.Eventos<PagamentoConfirmado>());
    }

    [Fact]
    public async Task Estornar_DevolveValorEPublicaPagamentoEstornado()
    {
        var c = new Cenario();
        var (orcamento, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        c.Publicadas.Clear();

        await c.Pagamentos().EstornarAsync(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "execução recusou"), Ct);

        Assert.Equal(PagamentoStatus.Estornado, (await c.Db.Pagamentos.SingleAsync()).Status);
        Assert.Equal(OrcamentoStatus.Estornado, (await c.Db.Orcamentos.SingleAsync()).Status);
        Assert.Equal(StatusProvedor.Estornado, await c.Mock.ConsultarStatusAsync(externalId, Ct));
        var evento = Assert.Single(c.Eventos<PagamentoEstornado>());
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
        Assert.Equal(400m, evento.Valor);
    }

    [Fact]
    public async Task Estornar_Repetido_ReafirmaResultadoSemNovoEstorno()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        var comando = new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "x");
        await c.Pagamentos().EstornarAsync(comando, Ct);
        c.Publicadas.Clear();

        var gateway = new Mock<IPagamentoGateway>(MockBehavior.Strict);
        await c.Pagamentos(gateway.Object).EstornarAsync(comando, Ct);

        Assert.Single(c.Eventos<PagamentoEstornado>());
    }

    [Fact]
    public async Task Estornar_ProvedorRecusa_PublicaEstornoFalhouESemMudarEstado()
    {
        var c = new Cenario(falharEstorno: true);
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        c.Publicadas.Clear();

        await c.Pagamentos().EstornarAsync(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "x"), Ct);

        var falha = Assert.Single(c.Eventos<EstornoFalhou>());
        Assert.Contains("recusado", falha.Motivo, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PagamentoStatus.Aprovado, (await c.Db.Pagamentos.SingleAsync()).Status);
        Assert.Equal(OrcamentoStatus.Pago, (await c.Db.Orcamentos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Estornar_FalhaTransitoriaPropagaExcecaoParaRetry()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, StatusProvedor.Aprovado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "req-1", Ct);
        var gateway = new Mock<IPagamentoGateway>();
        gateway.Setup(g => g.EstornarAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));

        await Assert.ThrowsAsync<HttpRequestException>(() => c.Pagamentos(gateway.Object).EstornarAsync(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "x"), Ct));

        Assert.Equal(PagamentoStatus.Aprovado, (await c.Db.Pagamentos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Estornar_SemPagamentoAprovadoPublicaEstornoFalhou()
    {
        var c = new Cenario();
        await c.AprovarAsync(); // cobrança ainda pendente: nada a estornar

        await c.Pagamentos().EstornarAsync(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "x"), Ct);

        Assert.Single(c.Eventos<EstornoFalhou>());
        Assert.Empty(c.Eventos<PagamentoEstornado>());
    }
}
