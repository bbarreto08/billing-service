namespace wrench.billing.domain;

/// <summary>Violação de regra de negócio (ex.: transição de estado inválida).</summary>
public class DomainException(string message) : Exception(message);
