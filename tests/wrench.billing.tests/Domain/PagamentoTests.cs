using wrench.billing.domain;
using wrench.billing.domain.Pagamentos;

namespace wrench.billing.tests.Domain;

public class PagamentoTests
{
    private static readonly DateTime Agora = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Pagamento Novo()
        => Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), "ext-1", 100m, "pix", Agora.AddMinutes(30), Agora);

    [Fact]
    public void Criar_ComecaPendente()
    {
        var pagamento = Novo();

        Assert.Equal(PagamentoStatus.Pendente, pagamento.Status);
        Assert.True(pagamento.EstaPendente);
    }

    [Fact]
    public void Criar_ComDadosInvalidosLanca()
    {
        Assert.Throws<DomainException>(() =>
            Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), " ", 100m, "pix", Agora, Agora));
        Assert.Throws<DomainException>(() =>
            Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), "ext", 0m, "pix", Agora, Agora));
    }

    [Fact]
    public void EstaVencido_SoQuandoPendenteEAposExpiracao()
    {
        var pagamento = Novo();

        Assert.False(pagamento.EstaVencido(Agora.AddMinutes(29)));
        Assert.True(pagamento.EstaVencido(Agora.AddMinutes(30)));

        pagamento.Aprovar(Agora);
        Assert.False(pagamento.EstaVencido(Agora.AddHours(5)));
    }

    [Fact]
    public void Aprovar_EIdempotenteEBloqueiaOutrasTransicoes()
    {
        var pagamento = Novo();

        Assert.True(pagamento.Aprovar(Agora));
        Assert.False(pagamento.Aprovar(Agora));
        Assert.Throws<DomainException>(() => pagamento.Recusar("x", Agora));
        Assert.Throws<DomainException>(() => pagamento.Expirar(Agora));
        Assert.Throws<DomainException>(() => pagamento.Cancelar(Agora));
    }

    [Fact]
    public void Recusar_GuardaMotivo()
    {
        var pagamento = Novo();

        Assert.True(pagamento.Recusar("saldo", Agora));
        Assert.False(pagamento.Recusar("saldo", Agora));
        Assert.Equal("saldo", pagamento.MotivoFalha);
    }

    [Fact]
    public void Expirar_RegistraMotivo()
    {
        var pagamento = Novo();

        Assert.True(pagamento.Expirar(Agora));
        Assert.False(pagamento.Expirar(Agora));
        Assert.Equal(PagamentoStatus.Expirado, pagamento.Status);
        Assert.NotNull(pagamento.MotivoFalha);
    }

    [Fact]
    public void Cancelar_SoPendente()
    {
        var pagamento = Novo();

        Assert.True(pagamento.Cancelar(Agora));
        Assert.False(pagamento.Cancelar(Agora));
    }

    [Fact]
    public void Estornar_ExigeAprovado()
    {
        var pagamento = Novo();
        Assert.Throws<DomainException>(() => pagamento.Estornar(Agora));

        pagamento.Aprovar(Agora);
        Assert.True(pagamento.Estornar(Agora));
        Assert.False(pagamento.Estornar(Agora));
        Assert.Equal(PagamentoStatus.Estornado, pagamento.Status);
    }

    [Fact]
    public void WebhookEvento_GuardaDados()
    {
        var evento = new WebhookEvento("chave", "ext-1", Agora);

        Assert.Equal("chave", evento.Chave);
        Assert.Equal("ext-1", evento.ExternalPagamentoId);
        Assert.Equal(Agora, evento.RecebidoEm);
    }
}
