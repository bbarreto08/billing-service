using wrench.billing.application.Contracts;
using wrench.billing.tests.Support;

namespace wrench.billing.tests.Bdd.Support;

/// <summary>
/// Estado compartilhado entre os passos de um cenário (o Reqnroll injeta uma instância por cenário).
/// Usa os serviços reais com banco em memória, gateway mock e publisher que registra as mensagens.
/// </summary>
public class FluxoContexto
{
    private Cenario? _cenario;

    /// <summary>Precisa ser definido antes do primeiro uso do <see cref="Cenario"/>.</summary>
    public bool ProvedorRecusaEstorno { get; set; }

    public Cenario Cenario => _cenario ??= new Cenario(ProvedorRecusaEstorno);

    public decimal ValorMaoDeObra { get; set; }
    public int QuantidadePecas { get; set; }
    public decimal ValorPeca { get; set; }
    public string EmailCliente { get; set; } = "cliente@wrench.com.br";

    public Guid OrcamentoId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public int NotificacoesEnviadas { get; set; }
    public Exception? ErroUltimaOperacao { get; set; }

    public GerarOrcamento ComandoGerar() => new(
        Cenario.OrdemServicoId,
        Cenario.CorrelationId,
        Guid.NewGuid(),
        EmailCliente,
        "Troca de embreagem",
        ValorMaoDeObra,
        [new ItemOrcamentoDto(Guid.NewGuid(), "Kit embreagem", QuantidadePecas, ValorPeca)]);
}
