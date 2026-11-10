variable "rds_host" {
  description = "Endpoint do RDS compartilhado (ex.: prod-db.bgt3.com.br)."
  type        = string
}

variable "rds_master_database" {
  description = "Database existente usado apenas para a conexão administrativa."
  type        = string
  default     = "postgres"
}

variable "rds_master_username" {
  description = "Usuário master do RDS."
  type        = string
  sensitive   = true
}

variable "rds_master_password" {
  description = "Senha do usuário master do RDS."
  type        = string
  sensitive   = true
}

variable "billing_database_name" {
  description = "Nome do database exclusivo do Billing Service."
  type        = string
  default     = "wrench_billing"
}

variable "billing_database_username" {
  description = "Role de menor privilégio usado pelo Billing Service."
  type        = string
  sensitive   = true
}

variable "billing_database_password" {
  description = "Senha do role do Billing Service."
  type        = string
  sensitive   = true
}
