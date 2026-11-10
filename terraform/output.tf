output "billing_database_name" {
  value = postgresql_database.billing.name
}

# Usuário e senha NÃO são expostos em output (evita vazar em logs/CLI).
output "billing_database_username" {
  value     = postgresql_role.billing.name
  sensitive = true
}
