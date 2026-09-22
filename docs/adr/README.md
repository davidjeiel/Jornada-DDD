# Registros de Decisão de Arquitetura (ADR)

Um ADR registra **uma** decisão arquitetural: o contexto que a forçou, o que foi
decidido, o que se abriu mão e o que se ganhou. Não é documentação de como o
sistema funciona (isso é [`../ARQUITETURA.md`](../ARQUITETURA.md)) — é o registro
de *por que* ele funciona assim.

## Por que manter ADRs neste projeto

Este catálogo existe para provar que decisão de arquitetura sem rastro vira
folclore corporativo. Seria incoerente construí-lo sem rastro. Além disso, os
ADRs são a evidência de tipo `adr` que o próprio modelo de qualidade do produto
já pontua (`qualidade.py`, dimensão *evidência*) — dogfooding.

## Regras

- **Um ADR por decisão.** Se você precisa de "e também", provavelmente são duas.
- **ADR é imutável depois de aceito.** Mudou de ideia? Novo ADR, com
  `Substitui: ADR-XXXX`, e o antigo passa a `Status: substituído por ADR-YYYY`.
- **Numeração sequencial**, nunca reaproveitada — mesmo para ADR rejeitado.
- **Escreva a alternativa descartada com honestidade.** Um ADR que só tem
  argumentos a favor da opção escolhida não ajuda ninguém daqui a dois anos.
- **Consequências negativas são obrigatórias.** Toda decisão tem custo. Se você
  não consegue nomear o custo, você não entendeu a decisão.

## Status possíveis

`proposto` · `aceito` · `rejeitado` · `substituído por ADR-XXXX` · `obsoleto`

## Índice

| # | Título | Status |
|---|---|---|
| [0001](0001-arquitetura-hexagonal-ddd.md) | Arquitetura hexagonal com DDD tático e guardas automatizadas | proposto |
| [0002](0002-plataforma-de-execucao.md) | .NET 10 no núcleo e TypeScript/React no front | proposto |
| [0003](0003-modelo-multi-tenancy.md) | Empresa como fronteira de isolamento, unidade como escopo | proposto |
| [0004](0004-persistencia-poliglota.md) | PostgreSQL como sistema de registro, poliglota no entorno | proposto |
| [0005](0005-metamodelo-por-tenant.md) | Metamodelo do catálogo configurável por empresa | proposto |
| [0006](0006-topologia-de-servicos.md) | Control plane separado e monólito modular extraível | proposto |
| [0007](0007-identidade-autenticacao-autorizacao.md) | Entra External ID e autorização de domínio centralizada | proposto |
| [0008](0008-outbox-mensageria.md) | Outbox transacional com Azure Service Bus | proposto |
| [0009](0009-api-publica-versionamento.md) | API pública versionada com contrato gerado do código | proposto |
| [0010](0010-monetizacao-medicao-uso.md) | Planos, entitlements e medição de uso | proposto |
| [0011](0011-infraestrutura-como-codigo.md) | Terraform, Azure Container Apps e deployment stamps | proposto |
| [0012](0012-estrategia-de-migracao.md) | Strangler Fig e migração de dados SQLite → PostgreSQL | proposto |
| [0013](0013-ambiente-local-podman.md) | Podman como runtime de containers no ambiente local | proposto |

## Modelo

```markdown
# ADR-XXXX — Título no imperativo

- **Status:** proposto
- **Data:** AAAA-MM-DD
- **Decisores:**
- **Relacionados:** ADR-YYYY

## Contexto
O que é verdade hoje, que restrição existe, o que forçou a decisão.

## Decisão
O que foi decidido, no imperativo. Concreto o bastante para virar código.

## Consequências
### Positivas
### Negativas
### Neutras / a monitorar

## Alternativas consideradas
Cada uma com o motivo real da recusa.

## Revisitar quando
O gatilho objetivo que obriga a reabrir esta decisão.
```
