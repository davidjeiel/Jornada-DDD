# ADR-0014 — Publicar o contrato OpenAPI no padrão CAIXA

- **Status:** proposto
- **Data:** 2026-10-09
- **Relacionados:** ADR-0009 (substitui parcialmente: formato de erro e geração do contrato), ADR-0002

## Contexto

O ADR-0009 previa um contrato gerado do código, mas a API não gerava contrato
nenhum, e os erros saíam em Problem Details (RFC 9457).

A CAIXA tem um guia para APIs .NET ("Orientações para API .NET com OpenAPI 3.0
no padrão CAIXA") com exigências concretas: OpenAPI 3.0.x via Swashbuckle,
modelo de erro próprio, cabeçalho `x-correlation-id`, metadados de governança no
contrato (equipe, gestor, classificação, RTC, timeouts), validação com Spectral
e publicação no API Management. O projeto passa a seguir esse guia, adaptando
o que conflita com decisões já tomadas.

## Decisão

Adotar o guia CAIXA, com as adaptações abaixo, todas explícitas:

| Item do guia | Como ficou | Por quê |
|---|---|---|
| .NET 8 LTS | **.NET 10 LTS** (mantém o ADR-0002) | O suporte do .NET 8 termina em novembro de 2026. Começar nele seria nascer em migração. |
| Swashbuckle | Swashbuckle.AspNetCore 10.3 | Adotado como pede o guia. |
| OpenAPI 3.0.3 | **3.0.4** | É o que o Swashbuckle 10 emite para 3.0.x. A 3.0.4 só esclarece a 3.0.3, sem mudar regra. Validar com o API Management antes da publicação. |
| Controllers | Minimal API, documentada por metadados | O guia mostra controllers como exemplo; trocar o estilo não muda o contrato. |
| Modelo `ApiError` | **Adotado** (`codigo`, `mensagem`, `correlationId`, `dataHora`) | Substitui o Problem Details do ADR-0009. Vale para TODA resposta 4xx/5xx, inclusive JSON malformado, rota inexistente e exceção inesperada. |
| `x-correlation-id` | Adotado, **opcional** na entrada | O guia diz "recebido ou gerado". Valor que não é UUID é descartado e substituído (vai para log). |
| Códigos HTTP | 400 entrada, 401, 403, 404, 409 conflito de estado, 422 regra, 500 | A tabela fica num ponto só (`CodigosDeErro`). Antes quase tudo era 422. |
| OAuth2/JWT | **Documentado como alvo, não validado** | A identidade corporativa ainda não está ligada. O contrato também documenta os cabeçalhos de desenvolvimento (`X-Empresa`...), marcados "somente desenvolvimento". |
| Rota `/<dominio>/<area>/<produto>/v1` | **Configurável** (`OpenApi:PrefixoDasRotas`), padrão `/v1` | Os valores precisam de aprovação da governança. |
| Metadados (equipe, gestor, classificação, RTC, timeouts) | Configuração `OpenApi:*`, publicados como `x-*` no `info` | Valor padrão "a definir com a governança": a lacuna aparece no contrato e vira aviso no Spectral, em vez de um dado inventado. |
| Swagger UI | Só em Development ou com `OpenApi:HabilitarUi=true` | O guia pede que a exposição em produção siga as regras do ambiente. O documento em si (`/swagger/v1/swagger.json`) fica sempre disponível, para o API Management. |
| Spectral | `.spectral.yaml` = `spectral:oas` + regras do checklist | O ruleset corporativo entra no `extends` quando for publicado. |
| Serilog, FluentValidation, API Versioning | **Não adotados agora** | Fora do escopo do contrato. A validação de entrada já acontece no domínio e nos tipos (`required`); a versão está no caminho. |

O contrato gerado fica commitado em `contratos/v1/openapi.yaml`, e o CI falha se
o código e o arquivo divergirem (`scripts/gerar-contrato.sh --verificar`) ou se
o Spectral achar erro.

## Consequências

### Positivas
- O contrato existe, é revisável no PR e passa nas regras do guia.
- Um só formato de erro, com `correlationId` que liga a resposta ao log.
- O `id` da ficha do ativo passou a sair como texto. Antes saía como objeto,
  e o frontend chamava `/v1/ativos/[object Object]` ao editar pela ficha.

### Negativas
- Mudança de contrato para quem já consumia a API: erros mudaram de formato e
  vários status mudaram (ex.: "não encontrado" era 422, agora é 404). Hoje o único
  consumidor é o frontend, ajustado no mesmo PR.
- O contrato promete JWT que a API ainda não valida. Está escrito na descrição
  do esquema, mas é uma promessa a cumprir.
- Os bloqueios do pré-check não vêm mais como lista no erro de submissão:
  vêm na mensagem. A lista estruturada continua em `GET .../pre-check`.

### A monitorar
- Aceitação da versão 3.0.4 pelo API Management e pelo ruleset corporativo.
- Valores reais dos metadados e do prefixo de rota.

## Alternativas consideradas

- **`Microsoft.AspNetCore.OpenApi` (gerador nativo do .NET 10).** Gera OpenAPI 3.1
  por padrão e dispensa pacote. Recusado porque o guia pede Swashbuckle e 3.0.x.
- **Manter Problem Details.** É padrão IETF e o ADR-0009 o escolheu. Recusado
  porque o guia define o modelo de erro corporativo, e dois formatos no mesmo
  ecossistema de APIs custam mais do que o ganho.
- **`x-correlation-id` obrigatório.** O exemplo do guia o marca como required.
  Recusado porque o texto do guia diz "recebido ou gerado", e obrigatório
  quebraria chamadas simples sem ganho de rastreabilidade.

## Revisitar quando

- a governança publicar o ruleset Spectral corporativo ou o modelo de erro definitivo;
- a identidade corporativa for ligada (o esquema Bearer deixa de ser só alvo);
- o API Management recusar a versão 3.0.4.
