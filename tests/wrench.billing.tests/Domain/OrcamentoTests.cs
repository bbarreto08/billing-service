using wrench.billing.domain;
using wrench.billing.domain.Orcamentos;

namespace wrench.billing.tests.Domain;

public class OrcamentoTests
{
    private static readonly DateTime Agora = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Orcamento Novo(decimal maoDeObra = 100m, params ItemOrcamento[] itens)
        => Orcamento.Criar(Guid.NewGuid(), "corr-1", Guid.NewGuid(), " a@b.com ", "Troca de óleo",
            maoDeObra, itens, Agora);

    [Fact]
    public void Criar_CalculaTotalSomandoMaoDeObraEItens()
    {
        var orcamento = Novo(100m, new ItemOrcamento(Guid.NewGuid(), "Filtro", 2, 25m));

        Assert.Equal(150m, orcamento.ValorTotal);
        Assert.Equal(OrcamentoStatus.Gerado, orcamento.Status);
        Assert.Equal("a@b.com", orcamento.ClienteEmail);
        Assert.Single(orcamento.Itens);
    }

    [Fact]
    public void Criar_SemValorLanca()
        => Assert.Throws<DomainException>(() => Novo(0m));

    [Fact]
    public void Criar_ComMaoDeObraNegativaLanca()
        => Assert.Throws<DomainException>(() => Novo(-1m, new ItemOrcamento(Guid.NewGuid(), "Filtro", 1, 50m)));

    [Fact]
    public void Criar_SemEmailLanca()
        => Assert.Throws<DomainException>(() => Orcamento.Criar(
            Guid.NewGuid(), "corr-1", Guid.NewGuid(), " ", "x", 10m, [], Agora));

    [Fact]
    public void Criar_SemOrdemServicoLanca()
        => Assert.Throws<DomainException>(() => Orcamento.Criar(
            Guid.Empty, "corr-1", Guid.NewGuid(), "a@b.com", "x", 10m, [], Agora));

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("Peça", 0, 1)]
    [InlineData("Peça", 1, -1)]
    public void Item_InvalidoLanca(string descricao, int quantidade, decimal valor)
        => Assert.Throws<DomainException>(() => new ItemOrcamento(Guid.NewGuid(), descricao, quantidade, valor));

    [Fact]
    public void Aprovar_EIdempotente()
    {
        var orcamento = Novo();

        Assert.True(orcamento.Aprovar(Agora));
        Assert.False(orcamento.Aprovar(Agora));
        Assert.Equal(OrcamentoStatus.Aprovado, orcamento.Status);
    }

    [Fact]
    public void Aprovar_OrcamentoRecusadoLanca()
    {
        var orcamento = Novo();
        orcamento.Recusar("caro", Agora);

        Assert.Throws<DomainException>(() => orcamento.Aprovar(Agora));
    }

    [Fact]
    public void Recusar_GuardaMotivoEEIdempotente()
    {
        var orcamento = Novo();

        Assert.True(orcamento.Recusar("caro", Agora));
        Assert.False(orcamento.Recusar("caro", Agora));
        Assert.Equal("caro", orcamento.MotivoRecusa);
    }

    [Fact]
    public void Recusar_OrcamentoAprovadoLanca()
    {
        var orcamento = Novo();
        orcamento.Aprovar(Agora);

        Assert.Throws<DomainException>(() => orcamento.Recusar("x", Agora));
    }

    [Fact]
    public void MarcarComoPago_ExigeAprovacaoEEIdempotente()
    {
        var orcamento = Novo();
        Assert.Throws<DomainException>(() => orcamento.MarcarComoPago(Agora));

        orcamento.Aprovar(Agora);
        Assert.True(orcamento.MarcarComoPago(Agora));
        Assert.False(orcamento.MarcarComoPago(Agora));
    }

    [Fact]
    public void Cancelar_OrcamentoRecusadoOuAprovadoFunciona()
    {
        var recusado = Novo();
        recusado.Recusar("caro", Agora);
        Assert.True(recusado.Cancelar("saga", Agora));
        Assert.False(recusado.Cancelar("saga", Agora));
        Assert.Equal("saga", recusado.MotivoCancelamento);

        var aprovado = Novo();
        aprovado.Aprovar(Agora);
        Assert.True(aprovado.Cancelar("saga", Agora));
    }

    [Fact]
    public void Cancelar_OrcamentoPagoLanca()
    {
        var orcamento = Novo();
        orcamento.Aprovar(Agora);
        orcamento.MarcarComoPago(Agora);

        Assert.Throws<DomainException>(() => orcamento.Cancelar("x", Agora));
    }

    [Fact]
    public void Estornar_ExigePagoEEIdempotente()
    {
        var orcamento = Novo();
        Assert.Throws<DomainException>(() => orcamento.Estornar(Agora));

        orcamento.Aprovar(Agora);
        orcamento.MarcarComoPago(Agora);
        Assert.True(orcamento.Estornar(Agora));
        Assert.False(orcamento.Estornar(Agora));
        Assert.Throws<DomainException>(() => orcamento.Cancelar("x", Agora));
    }
}
