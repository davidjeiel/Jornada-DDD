# Infraestrutura como código

Terraform sobre Azure Container Apps, conforme
[ADR-0011](../docs/adr/0011-infraestrutura-como-codigo.md).

**Esqueleto.** Nesta primeira versão só existe a estrutura de pastas e o
contrato de variáveis — os módulos são escritos na Fase 0 do roteiro
([ADR-0012](../docs/adr/0012-estrategia-de-migracao.md)).

```
infra/
├── modules/          rede, dados, aplicacao, gateway, identidade, segredos, observabilidade
├── envs/
│   ├── dev/          escala mínima, sem redundância de zona
│   ├── hml/          espelho reduzido de produção
│   └── prd/          zonas redundantes, réplicas de leitura
└── stamps/           unidade de escala replicável — tier Soberano (ADR-0003)
```

Regras que não se negociam:

- **Estado remoto** em Azure Storage com bloqueio. Perder o state é incidente sério.
- **OIDC federado** com o GitHub Actions: nenhum segredo de nuvem no repositório.
- **`envs/prd` e `stamps/<cliente>` chamam os MESMOS módulos.** Se um stamp de
  cliente virar um fork do Terraform, a decisão do ADR-0011 falhou.
- `terraform plan` comentado no PR, com Checkov ou tfsec falhando em severidade alta.
