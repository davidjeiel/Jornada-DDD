# ADR-0009 — Expor uma API pública versionada, com contrato gerado do código e publicado no APIM

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0006, ADR-0007, ADR-0010

## Contexto

Já existe uma API JSON em `catalogo/api.py` sob `/api/v1`, com 12 endpoints
(`/itens`, `/itens/{id}/precheck`, `/itens/{id}/vizinhanca`, `/validacoes`,
`/indicadores`, `/snapshots`…). Ela tem o desenho certo — usa os mesmos
`servicos`/`governanca` da UI, sem estado de sessão — mas foi feita para consumo
interno e mostra isso:

```python
@bp.post("/itens")
def criar_item():
    corpo = request.get_json(force=True)
    id_item = servicos.criar_item(..., usuario=corpo.get("usuario", "api"))
```

Três problemas que só aparecem quando há cliente externo:

1. **`usuario` vem do corpo da requisição.** Quem chama declara quem é. Numa API
   pública multiempresa isso é falsificação de identidade por design.
2. **Sem contrato publicado.** Não há OpenAPI; o cliente descobre a API lendo o
   código-fonte. Qualquer mudança de campo quebra integrações sem aviso.
3. **`id_item` sequencial na URL.** Expõe volume de negócio e permite enumeração —
   numa API multiempresa, é vazamento de informação comercial.

E o que falta por completo: autenticação, cota, paginação com cursor, idempotência
em escrita, tratamento de erro padronizado (hoje todo erro de negócio é `422` com
`{"erro": "..."}`), rate limit e portal do desenvolvedor.

Como a API é o que será vendido (ADR-0010), ela deixa de ser detalhe de
implementação e passa a ser **o produto**.

## Decisão

### 1. Três superfícies distintas, com contratos distintos

| Superfície | Consumidor | Estabilidade | Autenticação |
|---|---|---|---|
| **API Pública** `/v1` | Clientes, parceiros, CI/CD | **Contrato firme**, versionada | OAuth 2.0 `client_credentials`, chave de assinatura do APIM |
| **BFF** `/bff` | A SPA React | Livre, evolui com o front | Sessão OIDC (PKCE) |
| **Interno** | Entre módulos | Nenhuma garantia | Confiança de rede + mTLS |

Confundir as três é o erro que congela o produto: se a SPA consome a API pública,
toda mudança de tela vira mudança de contrato com clientes externos. O BFF existe
para que o front possa evoluir livremente, agregando o que a tela precisa (é onde
`visao_360` e `minha_mesa` fazem sentido — são respostas montadas para uma tela).

### 2. Contrato gerado do código, verificado no CI

O OpenAPI 3.1 é gerado do código .NET, **versionado no repositório**, e um teste
falha o build se o arquivo gerado divergir do commitado:

```
contratos/
  publico/v1/openapi.yaml      ← commitado; mudança exige revisão explícita no PR
```

Isso inverte o hábito: alterar a assinatura de um endpoint deixa de ser mudança
invisível e passa a aparecer como diff revisável. O mesmo arquivo alimenta o portal
do desenvolvedor, os SDKs gerados e os testes de contrato.

### 3. Regras da API pública

**Identidade vem do token, sempre.** O `usuario` some do corpo da requisição — é
derivado do `sub` e do `tid_empresa` (ADR-0007). Esta é a correção mais importante
deste ADR.

**Identificador público é opaco.** `id_item` sequencial fica interno; a API expõe
UUID v7 (`codigo_publico`, ADR-0004), que é ordenável por tempo sem revelar
contagem.

**Erro em RFC 9457 (Problem Details)**, substituindo o `{"erro": "..."}` atual:

```json
{
  "type": "https://api.catalogo.exemplo/erros/precheck-reprovado",
  "title": "Ativo não atende à política de publicação",
  "status": 422,
  "detail": "Score 54 abaixo do mínimo 70 para criticidade alta.",
  "instance": "/v1/ativos/019429.../submissoes",
  "bloqueios": ["SCORE_ABAIXO_DO_MINIMO", "EVIDENCIA_INSUFICIENTE"],
  "traceId": "00-4bf92f...-01"
}
```

Os `bloqueios` são os códigos que `governanca.pre_check` já produz e que o
checklist de `caminho_publicacao` já reaproveita. Torná-los parte do contrato
público permite que o cliente automatize a correção — é o tipo de detalhe que
transforma uma API em produto.

**Paginação por cursor**, não por offset. O `buscar_pagina(pagina, por_pagina)`
atual degrada e produz resultado inconsistente sob escrita concorrente.

**Idempotência em escrita**: header `Idempotency-Key`, com a resposta armazenada em
Redis por 24 h. Sem isso, um retry de rede duplica ativo.

**Recursos, não procedimentos.** `POST /v1/ativos/{id}/submissoes` em vez de
`POST /itens/{id}/submeter`; a submissão é um recurso com estado, consultável.

**Campos esparsos e expansão** (`?campos=`, `?expandir=trilha,relacoes`), porque
`visao_360` devolve muita coisa e nem todo cliente quer tudo.

### 4. Versionamento

- Versão maior no caminho: `/v1`, `/v2`. Explícito, cacheável, visível no log.
- **Dentro de uma versão maior, só mudança aditiva.** Adicionar campo ou endpoint
  opcional: permitido. Remover, renomear, tornar obrigatório ou restringir enum:
  proibido.
- Depreciação com **12 meses** de aviso, header `Deprecation` + `Sunset` (RFC 8594)
  e comunicação ativa aos consumidores medidos (o serviço de medição sabe
  exatamente quem chama o quê — use isso).
- Duas versões maiores em paralelo, no máximo.

### 5. O gateway

**Azure API Management** na frente, fazendo o que não deve estar no código:

| Responsabilidade | Onde |
|---|---|
| Validar token, assinatura, chave | APIM |
| Cota e rate limit por plano | APIM (`quota`, `rate-limit-by-key`) |
| Emitir log de uso para medição | APIM → Event Hubs (ADR-0010) |
| Portal do desenvolvedor, autoatendimento de chave | APIM |
| Validação de schema de requisição | APIM (a partir do OpenAPI) |
| **Autorização de negócio** | **Aplicação** — o gateway não sabe quem é owner de um ativo |
| **Isolamento entre empresas** | **Banco (RLS)** — ADR-0003 |

O limite é importante: gateway resolve *tráfego*, não *regra*.

### 6. Teste de contrato no CI

```csharp
[Fact] public void OpenApi_gerado_igual_ao_commitado()          { /* diff falha o build */ }
[Fact] public void Nenhuma_mudanca_quebra_v1()                  { /* compara com a tag anterior */ }
[Fact] public void Toda_rota_publica_exige_autenticacao()       { /* varre endpoints sem [Authorize] */ }
[Fact] public void Nenhuma_resposta_publica_expoe_id_interno()  { /* procura id sequencial no payload */ }
```

Os dois últimos são os que pegam o erro real: o endpoint novo que alguém esqueceu
de proteger, e o DTO que vazou `id_item`.

## Consequências

### Positivas

- O contrato vira artefato revisável: quebra deixa de ser acidente e passa a ser
  decisão.
- SDKs (TypeScript, Python, C#) gerados automaticamente a cada release.
- Identidade falsificável via corpo da requisição deixa de existir.
- O portal do desenvolvedor é pré-requisito de monetização (ADR-0010) e vem junto.

### Negativas

- **Rigidez custa.** Manter duas versões maiores por 12 meses é trabalho real, e
  toda depreciação vira projeto.
- **Três superfícies é mais código** que uma. O BFF duplica parte da forma dos
  dados (não da regra).
- **APIM tem custo** e sua camada Developer não tem SLA — o tier de produção
  (Standard v2 ou superior) é linha de custo relevante desde a Fase 3.
- Idempotência e cursor exigem infraestrutura (Redis, índice estável) que a API
  interna atual não precisava.

### Neutras / a monitorar

- O `api.py` atual é a base do `/v1`, não um descarte. Os endpoints já existem e as
  regras são as mesmas; muda a borda.
- `POST /snapshots` é operação administrativa e **não deve** ir para a API pública —
  vai para o control plane (ADR-0006).
- Webhooks são parte do contrato público (ADR-0008) e precisam do mesmo rigor de
  versionamento que os endpoints.

## Alternativas consideradas

**GraphQL.** Resolveria bem `visao_360`, `vizinhanca` e o grafo, evitando
sobrebusca. Recusada como superfície pública: dificulta cota e medição por
operação (tudo é um `POST /graphql`, o que atrapalha o ADR-0010), complica o cache
e tem superfície de ataque maior (consultas profundas). **Bom candidato para o
BFF**, onde o consumidor é conhecido — reavaliar na Fase 6.

**gRPC.** Ótimo entre serviços internos; ruim como API pública para clientes
corporativos que esperam REST + OpenAPI. Pode ser adotado internamente após a
extração (ADR-0006).

**Versionar por header (`Accept: application/vnd.catalogo.v2+json`).** Mais
elegante em tese. Recusada: pior para depuração, cache e para o cliente médio, que
vai errar o header.

**Sem gateway, tudo na aplicação.** Mais barato. Recusada: cota, medição, portal e
autoatendimento de chave viram código proprietário — exatamente o que o APIM
entrega pronto e o que o ADR-0010 depende.

## Revisitar quando

- O BFF acumular mais de ~20 endpoints só de agregação (→ avaliar GraphQL).
- O custo do APIM passar de ~5% da receita de API.
- Algum cliente grande exigir ordenação ou consulta que o REST não expresse bem.
