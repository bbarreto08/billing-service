# Database próprio do Billing: nenhum outro serviço recebe acesso a ele.
resource "postgresql_database" "billing" {
  name              = var.billing_database_name
  allow_connections = true
}

# Role de menor privilégio (em vez do master). Não é superuser, não cria role nem database.
resource "postgresql_role" "billing" {
  name             = var.billing_database_username
  login            = true
  password         = var.billing_database_password
  superuser        = false
  create_database  = false
  create_role      = false
  connection_limit = 20
}

resource "postgresql_grant" "database" {
  database    = postgresql_database.billing.name
  role        = postgresql_role.billing.name
  object_type = "database"
  privileges  = ["CONNECT", "CREATE"]
}

resource "postgresql_grant" "schema" {
  database    = postgresql_database.billing.name
  schema      = "public"
  role        = postgresql_role.billing.name
  object_type = "schema"
  privileges  = ["USAGE", "CREATE"]
}

# Sem default privileges: a aplicação é Code First (EF Migrations) e cria as tabelas com o
# próprio role, que passa a ser o dono delas. O Billing nunca conecta no database de outro serviço.
