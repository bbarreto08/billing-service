namespace wrench.billing.domain.Pagamentos;

public enum PagamentoStatus
{
    Pendente = 1,
    Aprovado = 2,
    Recusado = 3,
    Expirado = 4,
    Cancelado = 5,
    Estornado = 6
}

/// <summary>Cobrança Pix de um orçamento aprovado.</summary>
public class Pagamento
{
    private Pagamento() { }

    public Guid Id { get; private set; }
    public Guid OrcamentoId { get; private set; }
    public Guid OrdemServicoId { get; private set; }

    /// <summary>Identificador da cobrança no provedor (Mercado Pago).</summary>
    public string ExternalId { get; private set; } = string.Empty;

    public decimal Valor { get; private set; }
    public PagamentoStatus Status { get; private set; }
    public string PixCopiaECola { get; private set; } = string.Empty;
    public DateTime ExpiraEm { get; private set; }
    public string? MotivoFalha { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    public static Pagamento Criar(
        Guid orcamentoId,
        Guid ordemServicoId,
        string externalId,
        decimal valor,
        string pixCopiaECola,
        DateTime expiraEm,
        DateTime agora)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new DomainException("O identificador externo do pagamento é obrigatório.");
        if (valor <= 0)
            throw new DomainException("O valor do pagamento deve ser maior que zero.");

        return new Pagamento
        {
            Id = Guid.NewGuid(),
            OrcamentoId = orcamentoId,
            OrdemServicoId = ordemServicoId,
            ExternalId = externalId,
            Valor = valor,
            PixCopiaECola = pixCopiaECola,
            ExpiraEm = expiraEm,
            Status = PagamentoStatus.Pendente,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
    }

    public bool EstaPendente => Status == PagamentoStatus.Pendente;

    public bool EstaVencido(DateTime agora) => EstaPendente && agora >= ExpiraEm;

    public bool Aprovar(DateTime agora)
    {
        if (Status == PagamentoStatus.Aprovado) return false;
        Exigir(PagamentoStatus.Pendente, "aprovado");
        Mudar(PagamentoStatus.Aprovado, agora);
        return true;
    }

    public bool Recusar(string motivo, DateTime agora)
    {
        if (Status == PagamentoStatus.Recusado) return false;
        Exigir(PagamentoStatus.Pendente, "recusado");
        MotivoFalha = motivo;
        Mudar(PagamentoStatus.Recusado, agora);
        return true;
    }

    public bool Expirar(DateTime agora)
    {
        if (Status == PagamentoStatus.Expirado) return false;
        Exigir(PagamentoStatus.Pendente, "expirado");
        MotivoFalha = "Cobrança Pix expirada.";
        Mudar(PagamentoStatus.Expirado, agora);
        return true;
    }

    public bool Cancelar(DateTime agora)
    {
        if (Status == PagamentoStatus.Cancelado) return false;
        Exigir(PagamentoStatus.Pendente, "cancelado");
        Mudar(PagamentoStatus.Cancelado, agora);
        return true;
    }

    public bool Estornar(DateTime agora)
    {
        if (Status == PagamentoStatus.Estornado) return false;
        Exigir(PagamentoStatus.Aprovado, "estornado");
        Mudar(PagamentoStatus.Estornado, agora);
        return true;
    }

    private void Exigir(PagamentoStatus esperado, string acao)
    {
        if (Status != esperado)
            throw new DomainException(
                $"O pagamento está {Status} e não pode ser {acao} (esperado: {esperado}).");
    }

    private void Mudar(PagamentoStatus novo, DateTime agora)
    {
        Status = novo;
        AtualizadoEm = agora;
    }
}
