using wrench.billing.application.Abstractions;
using wrench.billing.infra.Pagamentos;
using Microsoft.Extensions.Options;
using Moq;

namespace wrench.billing.tests.Infra;

public class MockPagamentoGatewayTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static MockPagamentoGateway Novo(bool falharEstorno = false)
    {
        var monitor = new Mock<IOptionsMonitor<MockGatewayOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MockGatewayOptions { FalharEstorno = falharEstorno });
        return new MockPagamentoGateway(monitor.Object);
    }

    private static CriarCobrancaRequest Request(Guid orcamentoId)
        => new(orcamentoId, Guid.NewGuid(), 50m, "a@b.com", "OS", DateTime.UtcNow.AddMinutes(30));

    [Fact]
    public async Task CriarCobranca_EIdempotentePorOrcamento()
    {
        var gateway = Novo();
        var orcamentoId = Guid.NewGuid();

        var primeira = await gateway.CriarCobrancaPixAsync(Request(orcamentoId), Ct);
        gateway.Simular(primeira.ExternalId, StatusProvedor.Aprovado);
        var segunda = await gateway.CriarCobrancaPixAsync(Request(orcamentoId), Ct);

        Assert.Equal(primeira.ExternalId, segunda.ExternalId);
        // a recriação não pode "desfazer" o status simulado
        Assert.Equal(StatusProvedor.Aprovado, await gateway.ConsultarStatusAsync(primeira.ExternalId, Ct));
    }

    [Fact]
    public async Task ConsultarStatus_CobrancaDesconhecidaEPendente()
        => Assert.Equal(StatusProvedor.Pendente, await Novo().ConsultarStatusAsync("x", Ct));

    [Fact]
    public async Task CancelarCobranca_NaoSobrescrevePagamentoAprovado()
    {
        var gateway = Novo();
        var cobranca = await gateway.CriarCobrancaPixAsync(Request(Guid.NewGuid()), Ct);
        gateway.Simular(cobranca.ExternalId, StatusProvedor.Aprovado);

        await gateway.CancelarCobrancaAsync(cobranca.ExternalId, Ct);

        Assert.Equal(StatusProvedor.Aprovado, await gateway.ConsultarStatusAsync(cobranca.ExternalId, Ct));
    }

    [Fact]
    public async Task CancelarCobranca_CancelaPendente()
    {
        var gateway = Novo();
        var cobranca = await gateway.CriarCobrancaPixAsync(Request(Guid.NewGuid()), Ct);

        await gateway.CancelarCobrancaAsync(cobranca.ExternalId, Ct);

        Assert.Equal(StatusProvedor.Cancelado, await gateway.ConsultarStatusAsync(cobranca.ExternalId, Ct));
    }

    [Fact]
    public async Task Estornar_PagamentoAprovadoComSucesso()
    {
        var gateway = Novo();
        var cobranca = await gateway.CriarCobrancaPixAsync(Request(Guid.NewGuid()), Ct);
        gateway.Simular(cobranca.ExternalId, StatusProvedor.Aprovado);

        var resultado = await gateway.EstornarAsync(cobranca.ExternalId, 50m, Ct);

        Assert.True(resultado.Sucesso);
        Assert.Equal(StatusProvedor.Estornado, await gateway.ConsultarStatusAsync(cobranca.ExternalId, Ct));
    }

    [Fact]
    public async Task Estornar_PagamentoNaoAprovadoFalha()
    {
        var gateway = Novo();
        var cobranca = await gateway.CriarCobrancaPixAsync(Request(Guid.NewGuid()), Ct);

        var resultado = await gateway.EstornarAsync(cobranca.ExternalId, 50m, Ct);

        Assert.False(resultado.Sucesso);
    }

    [Fact]
    public async Task Estornar_ComFalhaForcadaPorConfiguracao()
    {
        var gateway = Novo(falharEstorno: true);

        var resultado = await gateway.EstornarAsync("qualquer", 50m, Ct);

        Assert.False(resultado.Sucesso);
        Assert.NotNull(resultado.Erro);
    }
}
