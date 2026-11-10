# Conecta no RDS com o usuário MASTER para criar o database e o role do Billing.
# superuser = false é obrigatório no RDS: o master não é um superuser Postgres de verdade.
provider "postgresql" {
  host      = var.rds_host
  port      = 5432
  database  = var.rds_master_database
  username  = var.rds_master_username
  password  = var.rds_master_password
  sslmode   = "require"
  superuser = false
}
