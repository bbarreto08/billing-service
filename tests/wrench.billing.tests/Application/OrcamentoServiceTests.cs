using wrench.billing.application;
using wrench.billing.domain;
using wrench.billing.domain.Orcamentos;
using wrench.billing.domain.Pagamentos;
using wrench.billing.tests.Support;
using Microsoft.EntityFrameworkCore;
using Moq;
using wrench.billing.application.Contracts;
using wrench.billing.application.Exceptions;

namespace wrench.billing.tests.Application;

public class OrcamentoServiceTests
{
    [Fact]
    public async Task Gerar_CriaOrcamentoPublicaEventoEEnviaAoCliente()
    {
        var c = new Cenario();

        await c.Orcamentos().GerarAsync(Cenario.ComandoGerar(), CancellationToken.None);

        var orcamento = await c.Db.Orcamentos.Include(o => o.Itens).SingleAsync();
        Assert.Equal(400m, orcamento.ValorTotal); // 100 de mão de obra + 2 x 150
        var evento = Assert.Single(c.Eventos<OrcamentoGerado>());
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
        Assert.Equal(Cenario.OrdemServicoId, evento.OrdemServicoId);
        Assert.Equal(Cenario.CorrelationId, evento.CorrelationId);
        c.Notificador.Verify(n => n.EnviarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gerar_ComandoRepetidoNaoDuplicaOrcamento()
    {
        var c = new Cenario();

        await c.Orcamentos().GerarAsync(Cenario.ComandoGerar(), CancellationToken.None);
        await c.Orcamentos().GerarAsync(Cenario.ComandoGerar(), CancellationToken.None);

        Assert.Equal(1, await c.Db.Orcamentos.CountAsync());
        Assert.Equal(2, c.Eventos<OrcamentoGerado>().Count());
        c.Notificador.Verify(n => n.EnviarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gerar_ComDadosInvalidosPublicaOrcamentoFalhou()
    {
        var c = new Cenario();

        await c.Orcamentos().GerarAsync(Cenario.ComandoGerar(email: " "), CancellationToken.None);

        Assert.Empty(c.Db.Orcamentos);
        var falha = Assert.Single(c.Eventos<OrcamentoFalhou>());
        Assert.Equal(Cenario.OrdemServicoId, falha.OrdemServicoId);
        Assert.Empty(c.Eventos<OrcamentoGerado>());
    }

    [Fact]
    public async Task Gerar_FalhaNoEnvioDoEmailNaoDerrubaASaga()
    {
        var c = new Cenario();
        c.Notificador.Setup(n => n.EnviarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SES indisponível"));

        await c.Orcamentos().GerarAsync(Cenario.ComandoGerar(), CancellationToken.None);

        Assert.Equal(1, await c.Db.Orcamentos.CountAsync());
        Assert.Single(c.Eventos<OrcamentoGerado>());
    }

    [Fact]
    public async Task Gerar_ComItensNulosUsaApenasMaoDeObra()
    {
        var c = new Cenario();
        var comando = Cenario.ComandoGerar() with { Itens = null! };

        await c.Orcamentos().GerarAsync(comando, CancellationToken.None);

        Assert.Equal(100m, (await c.Db.Orcamentos.SingleAsync()).ValorTotal);
    }

    [Fact]
    public async Task Aprovar_CriaCobrancaPixEPublicaOrcamentoAprovado()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();

        var pagamento = await c.Orcamentos().AprovarAsync(orcamento.Id, CancellationToken.None);

        Assert.Equal("Pendente", pagamento.Status);
        Assert.Equal(400m, pagamento.Valor);
        Assert.Equal(c.Relogio.GetUtcNow().UtcDateTime.AddMinutes(30), pagamento.ExpiraEm);
        var evento = Assert.Single(c.Eventos<OrcamentoAprovado>());
        Assert.Equal(pagamento.PixCopiaECola, evento.PixCopiaECola);
        Assert.Equal(OrcamentoStatus.Aprovado, (await c.Db.Orcamentos.SingleAsync()).Status);
    }

    [Fact]
    public async Task Aprovar_Repetido_DevolveMesmaCobrancaSemRepublicar()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();

        var primeiro = await c.Orcamentos().AprovarAsync(orcamento.Id, CancellationToken.None);
        var segundo = await c.Orcamentos().AprovarAsync(orcamento.Id, CancellationToken.None);

        Assert.Equal(primeiro.Id, segundo.Id);
        Assert.Equal(1, await c.Db.Pagamentos.CountAsync());
        Assert.Single(c.Eventos<OrcamentoAprovado>());
    }

    [Fact]
    public async Task Aprovar_OrcamentoInexistenteLanca()
    {
        var c = new Cenario();

        await Assert.ThrowsAsync<NaoEncontradoException>(
            () => c.Orcamentos().AprovarAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Aprovar_OrcamentoRecusadoLancaConflito()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();
        await c.Orcamentos().RecusarAsync(orcamento.Id, "caro", CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(
            () => c.Orcamentos().AprovarAsync(orcamento.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Aprovar_FalhaNoGatewayNaoGravaNada()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();
        var gateway = new Mock<global::wrench.billing.application.Abstractions.IPagamentoGateway>();
        gateway.Setup(g => g.CriarCobrancaPixAsync(It.IsAny<global::wrench.billing.application.Abstractions.CriarCobrancaRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("MP fora do ar"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => c.Orcamentos(gateway.Object).AprovarAsync(orcamento.Id, CancellationToken.None));

        Assert.Empty(c.Db.Pagamentos);
        Assert.Empty(c.Eventos<OrcamentoAprovado>());
        Assert.Equal(OrcamentoStatus.Gerado, (await c.Db.Orcamentos.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Recusar_PublicaEventoUmaVez()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();

        var resposta = await c.Orcamentos().RecusarAsync(orcamento.Id, " caro demais ", CancellationToken.None);
        await c.Orcamentos().RecusarAsync(orcamento.Id, "caro demais", CancellationToken.None);

        Assert.Equal("Recusado", resposta.Status);
        Assert.Equal("caro demais", resposta.MotivoRecusa);
        var evento = Assert.Single(c.Eventos<OrcamentoRecusado>());
        Assert.Equal("caro demais", evento.Motivo);
    }

    [Fact]
    public async Task Recusar_SemMotivoUsaTextoPadrao()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();

        var resposta = await c.Orcamentos().RecusarAsync(orcamento.Id, "  ", CancellationToken.None);

        Assert.Equal("Não informado", resposta.MotivoRecusa);
    }

    [Fact]
    public async Task Cancelar_CancelaOrcamentoECobrancaPendente()
    {
        var c = new Cenario();
        var (orcamento, externalId) = await c.AprovarAsync();

        await c.Orcamentos().CancelarAsync(
            new CancelarOrcamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "saga"), CancellationToken.None);

        Assert.Equal(OrcamentoStatus.Cancelado, (await c.Db.Orcamentos.SingleAsync()).Status);
        Assert.Equal(PagamentoStatus.Cancelado, (await c.Db.Pagamentos.SingleAsync()).Status);
        Assert.Equal(global::wrench.billing.application.Abstractions.StatusProvedor.Cancelado,
            await c.Mock.ConsultarStatusAsync(externalId, CancellationToken.None));
        var evento = Assert.Single(c.Eventos<OrcamentoCancelado>());
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
    }

    [Fact]
    public async Task Cancelar_OrcamentoRecusadoPublicaCancelado()
    {
        var c = new Cenario();
        var orcamento = await c.GerarOrcamentoAsync();
        await c.Orcamentos().RecusarAsync(orcamento.Id, "caro", CancellationToken.None);
        c.Publicadas.Clear();

        await c.Orcamentos().CancelarAsync(
            new CancelarOrcamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "cliente recusou"), CancellationToken.None);

        Assert.Equal(OrcamentoStatus.Cancelado, (await c.Db.Orcamentos.SingleAsync()).Status);
        Assert.Single(c.Eventos<OrcamentoCancelado>());
    }

    [Fact]
    public async Task Cancelar_OrcamentoPagoLancaParaIrParaDlq()
    {
        var c = new Cenario();
        var (_, externalId) = await c.AprovarAsync();
        c.Mock.Simular(externalId, global::wrench.billing.application.Abstractions.StatusProvedor.Aprovado);
        await c.Pagamentos().ProcessarNotificacaoAsync(externalId, "k1", CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(() => c.Orcamentos().CancelarAsync(
            new CancelarOrcamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "x"), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelar_SemOrcamentoNaoFazNada()
    {
        var c = new Cenario();

        await c.Orcamentos().CancelarAsync(
            new CancelarOrcamento(Guid.NewGuid(), Cenario.CorrelationId, "x"), CancellationToken.None);

        Assert.Empty(c.Publicadas);
    }
}
