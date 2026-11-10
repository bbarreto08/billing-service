# Estado no HCP Terraform, no mesmo padrão das demais stacks.
# Crie o workspace (ou ajuste o nome) antes do primeiro `terraform init`.
terraform {
  cloud {
    organization = "bgt3"

    workspaces {
      name = "wrench_billing_postgres"
    }
  }
}
