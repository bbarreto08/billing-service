namespace wrench.billing.domain.Orcamentos;

/// <summary>
/// Orçamento de uma Ordem de Serviço. Os métodos de transição retornam
/// <c>true</c> quando o estado mudou e <c>false</c> quando a operação já havia sido
/// aplicada (idempotência: reprocessar a mesma mensagem não gera efeito colateral).
/// </summary>
public class Orcamento
{
    private readonly List<ItemOrcamento> _itens = [];

    private Orcamento() { }

    public Guid Id { get; private set; }
    public Guid OrdemServicoId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public Guid ClienteId { get; private set; }
    public string ClienteEmail { get; private set; } = string.Empty;
    public string SolucaoProposta { get; private set; } = string.Empty;
    public decimal ValorMaoDeObra { get; private set; }
    public decimal ValorTotal { get; private set; }
    public OrcamentoStatus Status { get; private set; }
    public string? MotivoRecusa { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    public IReadOnlyCollection<ItemOrcamento> Itens => _itens;

    public static Orcamento Criar(
        Guid ordemServicoId,
        string correlationId,
        Guid clienteId,
        string clienteEmail,
        string solucaoProposta,
        decimal valorMaoDeObra,
        IEnumerable<ItemOrcamento> itens,
        DateTime agora)
    {
        if (ordemServicoId == Guid.Empty)
            throw new DomainException("A ordem de serviço é obrigatória.");
        if (string.IsNullOrWhiteSpace(clienteEmail))
            throw new DomainException("O e-mail do cliente é obrigatório.");
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new DomainException("O correlationId é obrigatório.");
        if (valorMaoDeObra < 0)
            throw new DomainException("O valor da mão de obra não pode ser negativo.");

        var orcamento = new Orcamento
        {
            Id = Guid.NewGuid(),
            OrdemServicoId = ordemServicoId,
            CorrelationId = correlationId.Trim(),
            ClienteId = clienteId,
            ClienteEmail = clienteEmail.Trim(),
            SolucaoProposta = solucaoProposta ?? string.Empty,
            ValorMaoDeObra = valorMaoDeObra,
            Status = OrcamentoStatus.Gerado,
            CriadoEm = agora,
            AtualizadoEm = agora
        };

        orcamento._itens.AddRange(itens);
        orcamento.ValorTotal = valorMaoDeObra + orcamento._itens.Sum(i => i.Subtotal);

        if (orcamento.ValorTotal <= 0)
            throw new DomainException("O valor total do orçamento deve ser maior que zero.");

        return orcamento;
    }

    public bool Aprovar(DateTime agora)
    {
        if (Status == OrcamentoStatus.Aprovado) return false;
        ExigirStatus(OrcamentoStatus.Gerado, "aprovado");
        Mudar(OrcamentoStatus.Aprovado, agora);
        return true;
    }

    public bool Recusar(string motivo, DateTime agora)
    {
        if (Status == OrcamentoStatus.Recusado) return false;
        ExigirStatus(OrcamentoStatus.Gerado, "recusado");
        MotivoRecusa = motivo;
        Mudar(OrcamentoStatus.Recusado, agora);
        return true;
    }

    public bool MarcarComoPago(DateTime agora)
    {
        if (Status == OrcamentoStatus.Pago) return false;
        ExigirStatus(OrcamentoStatus.Aprovado, "pago");
        Mudar(OrcamentoStatus.Pago, agora);
        return true;
    }

    /// <summary>Compensação: cancela o orçamento. Orçamento já pago deve ser estornado, não cancelado.</summary>
    public bool Cancelar(string motivo, DateTime agora)
    {
        if (Status == OrcamentoStatus.Cancelado) return false;
        if (Status is OrcamentoStatus.Pago or OrcamentoStatus.Estornado)
            throw new DomainException(
                $"Orçamento {Status} não pode ser cancelado; utilize o estorno do pagamento.");

        MotivoCancelamento = motivo;
        Mudar(OrcamentoStatus.Cancelado, agora);
        return true;
    }

    public bool Estornar(DateTime agora)
    {
        if (Status == OrcamentoStatus.Estornado) return false;
        ExigirStatus(OrcamentoStatus.Pago, "estornado");
        Mudar(OrcamentoStatus.Estornado, agora);
        return true;
    }

    private void ExigirStatus(OrcamentoStatus esperado, string acao)
    {
        if (Status != esperado)
            throw new DomainException(
                $"O orçamento está {Status} e não pode ser {acao} (esperado: {esperado}).");
    }

    private void Mudar(OrcamentoStatus novo, DateTime agora)
    {
        Status = novo;
        AtualizadoEm = agora;
    }
}
