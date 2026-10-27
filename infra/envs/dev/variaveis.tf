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

variable "nivel_de_consistencia_cosmos" {
  description = "Nível de consistência padrão da conta Cosmos DB (ADR-0016)."
  type        = string
  default     = "Session"
}

variable "tier_de_isolamento" {
  description = <<-DESC
    padrao   — pooled: app e conta compartilhadas, isolamento pela chave de partição
    dedicado — container ou conta Cosmos própria, app compartilhada
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
