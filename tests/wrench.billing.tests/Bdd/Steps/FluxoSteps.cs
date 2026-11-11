using Microsoft.EntityFrameworkCore;
using Moq;
using Reqnroll;
using wrench.billing.application.Abstractions;
using wrench.billing.application.Contracts;
using wrench.billing.domain;
using wrench.billing.tests.Bdd.Support;
using wrench.billing.tests.Support;

namespace wrench.billing.tests.Bdd.Steps;

[Binding]
public class FluxoSteps(FluxoContexto contexto)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private Cenario Cenario => contexto.Cenario;

    // ----------------------------------------------------------------- Dado

    [Given("um diagnóstico com mão de obra de {int} reais e {int} peças de {int} reais")]
    public void DadoDiagnostico(int maoDeObra, int quantidade, int valorPeca)
    {
        contexto.ValorMaoDeObra = maoDeObra;
        contexto.QuantidadePecas = quantidade;
        contexto.ValorPeca = valorPeca;
    }

    [Given("um diagnóstico sem e-mail do cliente")]
    public void DadoDiagnosticoSemEmail() => contexto.EmailCliente = " ";

    [Given("que o provedor de pagamento recusa estornos")]
    public void DadoProvedorRecusaEstornos()
    {
        // Precisa vir antes de qualquer passo que use os serviços (o cenário é criado sob demanda).
        contexto.ProvedorRecusaEstorno = true;
    }

    [Given("um orçamento gerado")]
    public async Task DadoOrcamentoGerado() => await GerarOrcamentoAsync();

    [Given("um orçamento aprovado")]
    public async Task DadoOrcamentoAprovado()
    {
        await GerarOrcamentoAsync();
        await AprovarOrcamentoAsync();
    }

    [Given("um orçamento aprovado e pago")]
    public async Task DadoOrcamentoAprovadoEPago()
    {
        await GerarOrcamentoAsync();
        await AprovarOrcamentoAsync();
        await ConfirmarPagamentoAsync(novaNotificacao: true);
    }

    // ----------------------------------------------------------------- Quando

    [When("o orquestrador pede a geração do orçamento")]
    public async Task QuandoGerar() => await GerarOrcamentoAsync();

    [When("o cliente aprova o orçamento")]
    public async Task QuandoAprovar() => await AprovarOrcamentoAsync();

    [When("o cliente recusa o orçamento com o motivo {string}")]
    public async Task QuandoRecusar(string motivo)
        => await Cenario.Orcamentos().RecusarAsync(contexto.OrcamentoId, motivo, Ct);

    [When("o provedor confirma o pagamento via webhook")]
    public async Task QuandoProvedorConfirma() => await ConfirmarPagamentoAsync(novaNotificacao: true);

    [When("o provedor envia a mesma notificação novamente")]
    public async Task QuandoProvedorReenvia() => await ConfirmarPagamentoAsync(novaNotificacao: false);

    [When("o provedor informa que o pagamento foi recusado")]
    public async Task QuandoProvedorRecusa()
    {
        Cenario.Mock.Simular(contexto.ExternalId, StatusProvedor.Recusado);
        await Cenario.Pagamentos().ProcessarNotificacaoAsync(contexto.ExternalId, ProximaChave(), Ct);
    }

    [When("se passam {int} minutos sem pagamento e a conciliação roda")]
    public async Task QuandoPassamMinutos(int minutos)
    {
        Cenario.Relogio.Avancar(TimeSpan.FromMinutes(minutos));
        await Cenario.Pagamentos().ReconciliarPendentesAsync(Ct);
    }

    [When("a execução não aceita a OS e o orquestrador pede o estorno")]
    public async Task QuandoPedeEstorno() => await PedirEstornoAsync();

    [When("o orquestrador pede o estorno novamente")]
    public async Task QuandoPedeEstornoNovamente()
    {
        // Gateway estrito: qualquer nova chamada ao provedor lançaria exceção e reprovaria o passo seguinte.
        var estrito = new Mock<IPagamentoGateway>(MockBehavior.Strict);
        contexto.ErroUltimaOperacao = await CapturarAsync(() => Cenario.Pagamentos(estrito.Object).EstornarAsync(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "pedido repetido"), Ct));
    }

    [When("o orquestrador manda cancelar o orçamento")]
    public async Task QuandoMandaCancelar()
    {
        contexto.ErroUltimaOperacao = await CapturarAsync(() => Cenario.Orcamentos().CancelarAsync(
            new CancelarOrcamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "cancelamento da Saga"), Ct));
    }

    // ----------------------------------------------------------------- Então

    [Then("o orçamento é gerado com o valor total de {int} reais")]
    public async Task EntaoOrcamentoGerado(int total)
    {
        var orcamento = await Cenario.Db.Orcamentos.AsNoTracking().SingleAsync();
        Assert.Equal(total, orcamento.ValorTotal);
    }

    [Then("a cobrança Pix fica {string}")]
    [Then("o pagamento fica {string}")]
    public async Task EntaoPagamentoFica(string status)
    {
        var pagamento = await Cenario.Db.Pagamentos.AsNoTracking().SingleAsync();
        Assert.Equal(status, pagamento.Status.ToString());
    }

    [Then("o orçamento fica {string}")]
    public async Task EntaoOrcamentoFica(string status)
    {
        var orcamento = await Cenario.Db.Orcamentos.AsNoTracking().SingleAsync();
        Assert.Equal(status, orcamento.Status.ToString());
    }

    [Then("o provedor registra o estorno")]
    public async Task EntaoProvedorEstornou()
        => Assert.Equal(StatusProvedor.Estornado, await Cenario.Mock.ConsultarStatusAsync(contexto.ExternalId, Ct));

    [Then("nenhum novo estorno é solicitado ao provedor")]
    public void EntaoSemNovoEstorno() => Assert.Null(contexto.ErroUltimaOperacao);

    [Then("o evento {string} é publicado")]
    public void EntaoEventoPublicado(string nome) => Assert.True(Contar(nome) >= 1, $"Evento {nome} não foi publicado.");

    [Then("o evento {string} não é publicado")]
    public void EntaoEventoNaoPublicado(string nome) => Assert.Equal(0, Contar(nome));

    [Then("o evento {string} é publicado {int} vez")]
    [Then("o evento {string} é publicado {int} vezes")]
    public void EntaoEventoPublicadoVezes(string nome, int vezes) => Assert.Equal(vezes, Contar(nome));

    [Then("o evento {string} é publicado com o valor de {int} reais")]
    public void EntaoEventoComValor(string nome, int valor)
    {
        var mensagem = Cenario.Publicadas.FirstOrDefault(m => m.GetType().Name == nome);
        Assert.NotNull(mensagem);

        var propriedade = mensagem.GetType().GetProperty("Valor") ?? mensagem.GetType().GetProperty("ValorTotal");
        Assert.NotNull(propriedade);
        Assert.Equal((decimal)valor, (decimal)propriedade.GetValue(mensagem)!);
    }

    [Then("a operação é rejeitada por conflito de estado")]
    public void EntaoOperacaoRejeitada() => Assert.IsType<DomainException>(contexto.ErroUltimaOperacao);

    [Then("nenhum orçamento é criado")]
    public void EntaoNenhumOrcamento() => Assert.Empty(Cenario.Db.Orcamentos);

    // ----------------------------------------------------------------- Apoio

    private async Task GerarOrcamentoAsync()
    {
        await Cenario.Orcamentos().GerarAsync(contexto.ComandoGerar(), Ct);

        var orcamento = await Cenario.Db.Orcamentos.AsNoTracking().FirstOrDefaultAsync();
        if (orcamento is not null)
            contexto.OrcamentoId = orcamento.Id;
    }

    private async Task AprovarOrcamentoAsync()
    {
        var pagamento = await Cenario.Orcamentos().AprovarAsync(contexto.OrcamentoId, Ct);
        contexto.ExternalId = pagamento.ExternalId;
    }

    private async Task ConfirmarPagamentoAsync(bool novaNotificacao)
    {
        Cenario.Mock.Simular(contexto.ExternalId, StatusProvedor.Aprovado);

        if (novaNotificacao)
            contexto.NotificacoesEnviadas++;

        await Cenario.Pagamentos().ProcessarNotificacaoAsync(contexto.ExternalId, $"req-{contexto.NotificacoesEnviadas}", Ct);
    }

    private string ProximaChave() => $"req-{++contexto.NotificacoesEnviadas}";

    private async Task PedirEstornoAsync()
    {
        await Cenario.Pagamentos().EstornarAsync(
            new EstornarPagamento(Cenario.OrdemServicoId, Cenario.CorrelationId, "execução não aceitou a OS"), Ct);
    }

    private int Contar(string nomeEvento) => Cenario.Publicadas.Count(m => m.GetType().Name == nomeEvento);

    private static async Task<Exception?> CapturarAsync(Func<Task> acao)
    {
        try
        {
            await acao();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
