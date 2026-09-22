# Contrato de variáveis do ambiente. Os módulos entram na Fase 0 (ADR-0012).

variable "ambiente" {
  description = "dev | hml | prd"
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "hml", "prd"], var.ambiente)
    error_message = "Ambiente deve ser dev, hml ou prd."
  }
}

variable "regiao" {
  description = "Região do Azure. Brazil South por residência de dados (LGPD)."
  type        = string
  default     = "brazilsouth"
}

variable "versao_postgres" {
  description = "Versão maior do PostgreSQL Flexible Server (ADR-0004)."
  type        = string
  default     = "17"
}

variable "tier_de_isolamento" {
  description = <<-DESC
    padrao   — pooled: app e banco compartilhados, isolamento por RLS
    dedicado — schema ou banco próprio, app compartilhada
    soberano — stamp completo: rede, banco e app próprios
    Ver ADR-0003.
  DESC
  type        = string
  default     = "padrao"

  validation {
    condition     = contains(["padrao", "dedicado", "soberano"], var.tier_de_isolamento)
    error_message = "Tier deve ser padrao, dedicado ou soberano."
  }
}

variable "etiquetas" {
  description = "Tags obrigatórias — insumo do relatório de custo por tenant (ADR-0011)."
  type        = map(string)
  default = {
    produto    = "jornada-ddd"
    gerido_por = "terraform"
  }
}
