namespace wrench.billing.api.Options;

public class SimulacaoOptions
{
    public const string Secao = "Simulacao";

    /// <summary>
    /// Habilita os endpoints que simulam o resultado do pagamento (somente com o gateway mock),
    /// usados para demonstrar o fluxo e as compensações da Saga. Deve ficar desligado em produção.
    /// </summary>
    public bool Habilitada { get; set; }

    /// <summary>Endereço pelo qual a simulação chama o webhook público do próprio serviço.</summary>
    public string WebhookBaseUrl { get; set; } = "http://localhost:8080";
}