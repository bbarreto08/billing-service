namespace wrench.billing.application.Options;

public class PagamentoOptions
{
    public const string Secao = "Pagamento";

    /// <summary>Validade da cobrança Pix, em minutos.</summary>
    public int ExpiracaoPixMinutos { get; set; } = 30;

    /// <summary>Segredo da aplicação usado na validação do header x-signature do webhook.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Intervalo da consulta ativa de pagamentos pendentes (plano B do webhook), em segundos.</summary>
    public int ReconciliacaoIntervaloSegundos { get; set; } = 30;
}
