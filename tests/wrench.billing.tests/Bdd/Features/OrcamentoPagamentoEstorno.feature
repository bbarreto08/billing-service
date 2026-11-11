# language: pt
Funcionalidade: Orçamento, pagamento e estorno da ordem de serviço
  Como oficina, quero cobrar o cliente com segurança e devolver o valor
  quando a execução da ordem de serviço não puder ser feita (compensação da Saga)

  Contexto:
    Dado um diagnóstico com mão de obra de 100 reais e 2 peças de 150 reais

  Cenário: Fluxo completo até o pagamento confirmado
    Quando o orquestrador pede a geração do orçamento
    Então o orçamento é gerado com o valor total de 400 reais
    E o evento "OrcamentoGerado" é publicado
    Quando o cliente aprova o orçamento
    Então a cobrança Pix fica "Pendente"
    E o evento "OrcamentoAprovado" é publicado
    Quando o provedor confirma o pagamento via webhook
    Então o pagamento fica "Aprovado"
    E o orçamento fica "Pago"
    E o evento "PagamentoConfirmado" é publicado com o valor de 400 reais

  Cenário: Execução não aceita a OS e o pagamento é estornado
    Dado um orçamento aprovado e pago
    Quando a execução não aceita a OS e o orquestrador pede o estorno
    Então o pagamento fica "Estornado"
    E o orçamento fica "Estornado"
    E o provedor registra o estorno
    E o evento "PagamentoEstornado" é publicado com o valor de 400 reais
    E o evento "EstornoFalhou" não é publicado

  Cenário: O provedor recusa o estorno
    Dado que o provedor de pagamento recusa estornos
    E um orçamento aprovado e pago
    Quando a execução não aceita a OS e o orquestrador pede o estorno
    Então o evento "EstornoFalhou" é publicado
    E o evento "PagamentoEstornado" não é publicado
    E o pagamento fica "Aprovado"
    E o orçamento fica "Pago"

  Cenário: O pedido de estorno repetido não devolve o valor duas vezes
    Dado um orçamento aprovado e pago
    Quando a execução não aceita a OS e o orquestrador pede o estorno
    E o orquestrador pede o estorno novamente
    Então o evento "PagamentoEstornado" é publicado 2 vezes
    E nenhum novo estorno é solicitado ao provedor

  Cenário: Cliente recusa o orçamento e a Saga cancela
    Dado um orçamento gerado
    Quando o cliente recusa o orçamento com o motivo "caro demais"
    Então o orçamento fica "Recusado"
    E o evento "OrcamentoRecusado" é publicado
    Quando o orquestrador manda cancelar o orçamento
    Então o orçamento fica "Cancelado"
    E o evento "OrcamentoCancelado" é publicado

  Cenário: Pagamento recusado pelo provedor
    Dado um orçamento aprovado
    Quando o provedor informa que o pagamento foi recusado
    Então o pagamento fica "Recusado"
    E o orçamento fica "Cancelado"
    E o evento "PagamentoFalhou" é publicado
    E o evento "PagamentoConfirmado" não é publicado

  Cenário: Cobrança Pix expira sem pagamento
    Dado um orçamento aprovado
    Quando se passam 31 minutos sem pagamento e a conciliação roda
    Então o pagamento fica "Expirado"
    E o orçamento fica "Cancelado"
    E o evento "PagamentoFalhou" é publicado

  Cenário: Notificação duplicada do provedor tem efeito único
    Dado um orçamento aprovado
    Quando o provedor confirma o pagamento via webhook
    E o provedor envia a mesma notificação novamente
    Então o evento "PagamentoConfirmado" é publicado 1 vez

  Cenário: Orçamento já pago não pode ser cancelado, só estornado
    Dado um orçamento aprovado e pago
    Quando o orquestrador manda cancelar o orçamento
    Então a operação é rejeitada por conflito de estado
    E o orçamento fica "Pago"

  Cenário: Orçamento com dados inválidos falha sem criar nada
    Dado um diagnóstico sem e-mail do cliente
    Quando o orquestrador pede a geração do orçamento
    Então nenhum orçamento é criado
    E o evento "OrcamentoFalhou" é publicado
    E o evento "OrcamentoGerado" não é publicado
