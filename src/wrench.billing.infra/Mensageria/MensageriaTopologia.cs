using MassTransit;
using wrench.billing.application.Contracts;

namespace wrench.billing.infra.Mensageria;

/// <summary>
/// Nomes da topologia SNS/SQS, todos configuráveis em <c>Messaging:*</c>. Os valores padrão são a SUPOSIÇÃO
/// do Billing a partir do plano da Fase 4 (um tópico por publicador, uma fila com DLQ por consumidor) e
/// devem ser confirmados com quem provisiona a mensageria.
/// </summary>
public class MensageriaOptions
{
    public const string Secao = "Messaging";

    /// <summary>Tópico SNS onde o Billing publica TODOS os seus eventos (um tópico por publicador).</summary>
    public const string TopicoEventosPadrao = "wrench-billing-events";

    /// <summary>Tópico SNS do qual o Billing consome os comandos (publicados pelo orquestrador no OS Service).</summary>
    public const string TopicoComandosPadrao = "wrench-os-events";

    /// <summary>Prefixo das filas SQS do Billing (uma por consumer, ex.: billing-gerar-orcamento).</summary>
    public const string PrefixoFilasPadrao = "billing";

    public string TopicoEventos { get; set; } = TopicoEventosPadrao;
    public string TopicoComandos { get; set; } = TopicoComandosPadrao;
    public string PrefixoFilas { get; set; } = PrefixoFilasPadrao;
}

public static class MensageriaTopologia
{
    /// <summary>
    /// Aponta os eventos do Billing para um único tópico e os comandos consumidos para o tópico do orquestrador.
    /// Valor vazio mantém o padrão do MassTransit (um tópico por tipo de mensagem).
    /// A outra ponta precisa usar os MESMOS nomes (idealmente definidos no pacote Wrench.Contracts).
    /// </summary>
    public static void Aplicar(IAmazonSqsBusFactoryConfigurator cfg, MensageriaOptions opcoes)
    {
        if (!string.IsNullOrWhiteSpace(opcoes.TopicoEventos))
        {
            var t = opcoes.TopicoEventos.Trim();
            Topico<OrcamentoGerado>(cfg, t);
            Topico<OrcamentoFalhou>(cfg, t);
            Topico<OrcamentoAprovado>(cfg, t);
            Topico<OrcamentoRecusado>(cfg, t);
            Topico<OrcamentoCancelado>(cfg, t);
            Topico<PagamentoConfirmado>(cfg, t);
            Topico<PagamentoFalhou>(cfg, t);
            Topico<PagamentoEstornado>(cfg, t);
            Topico<EstornoFalhou>(cfg, t);
        }

        if (!string.IsNullOrWhiteSpace(opcoes.TopicoComandos))
        {
            var t = opcoes.TopicoComandos.Trim();
            Topico<GerarOrcamento>(cfg, t);
            Topico<CancelarOrcamento>(cfg, t);
            Topico<EstornarPagamento>(cfg, t);
        }
    }

    private static void Topico<T>(IAmazonSqsBusFactoryConfigurator cfg, string nome) where T : class
        => cfg.Message<T>(m => m.SetEntityName(nome));
}
