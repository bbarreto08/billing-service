# Terraform: database do Billing Service

Cria no RDS compartilhado o database `wrench_billing` e um role de menor privilégio para o serviço.
Segue o padrão da stack `terraform/database` do repositório da Fase 2.

> Pelo plano da Fase 4 este código pertence ao repositório `infra-db`. Ele está aqui para viajar junto com o
> serviço; ao levá-lo para o `infra-db`, ajuste o `backend.tf` ao workspace de lá.

## Recursos

| Recurso | O que faz |
|---|---|
| `postgresql_database.billing` | Database exclusivo `wrench_billing` |
| `postgresql_role.billing` | Role de login, sem superuser, sem criar role/database |
| `postgresql_grant.database` | `CONNECT` e `CREATE` no database do Billing |
| `postgresql_grant.schema` | `USAGE` e `CREATE` no schema `public` |

O role é dono das tabelas que as migrations criam, então não são necessários grants de tabela nem default privileges.

## Aplicar

```bash
terraform -chdir=terraform init
terraform -chdir=terraform apply \
  --var="rds_host=<ENDPOINT_RDS>" \
  --var="rds_master_username=<MASTER_USER>" \
  --var="rds_master_password=<MASTER_PASS>" \
  --var="billing_database_username=<BILLING_USER>" \
  --var="billing_database_password=<BILLING_PASS>"
```

Pré-requisitos: RDS acessível pelo endpoint informado, workspace HCP `wrench_billing_postgres` criado e as variáveis
sensíveis marcadas como _sensitive_ (ou `TF_VAR_...`). Nunca versione senhas.

O usuário e a senha do Billing devem ser os mesmos passados ao chart Helm (`database.user` e `database.password`).
