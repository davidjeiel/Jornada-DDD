# ADR-0004 — PostgreSQL como sistema de registro, com poliglotismo deliberado no entorno

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0003, ADR-0005, ADR-0008, ADR-0010

## Contexto

O requisito é escolher o banco **ideal para o tipo de aplicação, sem se restringir
a um tipo só**. Antes de escolher, é preciso reconhecer que este produto tem cinco
cargas de trabalho com perfis genuinamente diferentes:

| Carga | Perfil | Exemplo no código atual |
|---|---|---|
| **Governança transacional** | Escrita pequena, invariante forte, auditoria | `submeter`, `decidir_validacao`, `publicar`, `revisao_catalogo` |
| **Consulta hierárquica / grafo** | Leitura recursiva, profundidade baixa | `trilha`, `arvore`, `vizinhanca`, `grafo_catalogo`, `analise_impacto` |
| **Busca exploratória** | Texto livre, faceta, relevância | `buscar` com `LIKE`, `_filtro_busca` |
| **Analítico** | Agregação, série histórica | `indicadores`, `snapshot_indicador`, `cobertura_por_dominio` |
| **Medição de uso** | Append-only, altíssimo volume, retenção curta | *não existe ainda* (ADR-0010) |

Poliglotismo não é escolher cinco bancos porque há cinco cargas. É reconhecer onde
um banco só já atende bem e onde ele genuinamente não atende, cobrando o custo
operacional de cada peça adicional.

A restrição prática: cada tecnologia de dados a mais custa backup, monitoramento,
plano de recuperação de desastre, conhecimento no time e uma fonte de verdade a
mais para manter consistente.

## Decisão

### O sistema de registro é o PostgreSQL

**Azure Database for PostgreSQL Flexible Server**, versão **17** (18 é suportada;
17 é a escolha conservadora com ecossistema de extensões já estabilizado). Toda
escrita de negócio passa por aqui, e só por aqui. Todo o resto é derivado.

Cinco razões, todas verificáveis no código atual:

1. **Transação real.** O padrão outbox de `notificacoes.py` — gravar a notificação
   na mesma transação do fato gerador — só é correto com ACID. Mesmo motivo para a
   revisão imutável com hash e a auditoria.
2. **JSONB cobre o metamodelo.** A coluna `atributos TEXT DEFAULT '{}'` vira
   `JSONB` com índice GIN e validação por JSON Schema (ADR-0005). Deixa de ser
   blob opaco e passa a ser consultável: `atributos @> '{"linguagem_ubiqua": ...}'`.
3. **CTE recursiva cobre a hierarquia e o grafo.** `trilha`, `arvore` e `vizinhanca`
   são `WITH RECURSIVE` diretos. Ver seção de grafo abaixo.
4. **RLS cobre o isolamento** entre empresas (ADR-0003). Nenhum outro banco
   gerenciado no Azure oferece isso com a mesma maturidade.
5. **Constraint é documentação executável.** As FKs, `UNIQUE` e `CHECK` do
   `schema.sql` atual já carregam regra; em Postgres elas ganham `EXCLUDE` para
   vigência sem sobreposição (`responsabilidade`, `atribuicao_papel`), que hoje é
   verificada em código.

Acesso em dois estilos, ambos atrás das portas do ADR-0001:
- **EF Core** para escrita, agregado, migração e outbox — mapeamento de agregado
  e `SaveChanges` transacional.
- **Dapper** para consulta pesada (`visao_360`, `grafo_catalogo`, `indicadores`) —
  SQL explícito, sem materializar agregado à toa.

### O que sai do PostgreSQL, e por quê

| Tecnologia | Papel | Por que não cabe no Postgres |
|---|---|---|
| **Azure Cache for Redis** | Cache de leitura, rate limit distribuído, chave de idempotência, lock de "assumir análise" | Escrita de altíssima frequência e TTL; usar tabela para isso gera bloat e vacuum |
| **Azure AI Search** | Busca do catálogo: facetas, relevância, sinônimos, analisador pt-BR, busca vetorial | `LIKE '%termo%'` não usa índice, não ordena por relevância e não faceta. É o gargalo mais previsível do produto |
| **Azure Blob Storage** | Evidências, anexos, exports, snapshots de payload grandes | Binário em banco relacional infla backup e cache |
| **Azure Service Bus** | Transporte de evento de domínio (despachado pela outbox) | Fila em tabela não faz fan-out, DLQ, nem entrega a consumidor externo |
| **Event Hubs → Azure Data Explorer** | Medição de uso (ADR-0010) | Append-only de altíssimo volume com retenção curta polui o OLTP e distorce o autovacuum |

Note que **nenhum deles é fonte de verdade**. Índice de busca, cache e telemetria
são projeções: se forem perdidos, são reconstruídos a partir do Postgres. Isso
mantém uma única fonte de verdade e um único plano de recuperação crítico.

### Grafo: adiar, com gatilho explícito

O catálogo é conceitualmente um grafo (`relacionamento_ativo` com
`implementa | expoe | consome | produz | depende_de | persiste_em`), e `vizinhanca`
já faz travessia por saltos. A tentação de adotar um banco de grafo é grande.

**Decisão: não adotar banco de grafo agora.** Fundamentos:

- As consultas reais do produto são de **1 a 3 saltos** (`vizinhanca(saltos=1)` por
  padrão, teto de 150 nós; `analise_impacto` é vizinhança inversa). Nesse regime a
  CTE recursiva do Postgres é competitiva e frequentemente mais rápida, porque não
  há travessia de rede para um segundo banco.
- O **SQL/PGQ** — grafo de propriedades nativo no padrão SQL:2023 — chegou a ser
  commitado para o **PostgreSQL 19 e foi revertido em 07/09/2026** (≈16 mil linhas
  em 124 arquivos), por questões de projeto e maturidade. Não estará no 19.0. O
  trabalho continua, então isso volta à mesa em uma versão futura, não agora.
- Um segundo banco significa sincronização, um segundo modelo de consistência e um
  segundo plano de DR — custo alto para ganho hoje inexistente.

Em vez disso, otimizar dentro do Postgres:

```sql
-- Índices que fazem a travessia andar (com tenant_id à esquerda, ADR-0003)
CREATE INDEX ix_rel_origem  ON relacionamento_ativo (tenant_id, id_origem,  tipo_relacao)
  WHERE fim_vigencia IS NULL;
CREATE INDEX ix_rel_destino ON relacionamento_ativo (tenant_id, id_destino, tipo_relacao)
  WHERE fim_vigencia IS NULL;

-- Closure table para a hierarquia (trilha e árvore viram SELECT sem recursão)
CREATE TABLE item_ancestral (
  tenant_id   UUID   NOT NULL,
  id_item     BIGINT NOT NULL,
  id_ancestral BIGINT NOT NULL,
  distancia   INT    NOT NULL,
  PRIMARY KEY (tenant_id, id_item, id_ancestral)
);
```

A closure table é mantida por trigger ou pelo caso de uso; troca escrita (rara:
item muda de pai raramente) por leitura (constante: `trilha` é chamada em quase
toda tela, inclusive por `acesso._dominio_do_item` a cada verificação de
autorização). É o trade-off certo para este perfil de uso.

**Gatilhos para reabrir** (qualquer um basta):
- consulta de travessia passar a precisar de **mais de 4 saltos** em caminho crítico;
- entrar no produto algum algoritmo de grafo de verdade — centralidade, detecção de
  comunidade, menor caminho ponderado, análise de blast radius transitivo;
- p95 de `vizinhanca` passar de 500 ms com os índices e a closure table em uso.

Se reabrir, os candidatos são, nesta ordem: **Apache AGE** como extensão do próprio
Postgres (mantém um banco), **Azure Cosmos DB for Apache Gremlin** (gerenciado no
Azure) e **Neo4j AuraDB** (mais poderoso, mais caro, fora do Azure).

### Analítico: evoluir, não antecipar

`snapshot_indicador` já resolve o dashboard executivo materializando indicadores por
competência. Mantenha no Postgres. Quando o volume doer — múltiplas empresas, série
longa, recortes cruzados — a evolução é exportar para **Microsoft Fabric / OneLake**
via CDC, com o Postgres seguindo como fonte. Não construa lakehouse na Fase 1.

### Mapa de migração do schema atual

| Hoje (SQLite) | Alvo (PostgreSQL) | Motivo |
|---|---|---|
| `INTEGER PRIMARY KEY AUTOINCREMENT` | `BIGINT GENERATED ALWAYS AS IDENTITY` | Id sequencial interno |
| — (ids expostos na URL) | `codigo_publico UUID v7` | Não exponha id sequencial em API multiempresa: vaza volume e permite enumeração |
| `TEXT` para data/hora | `TIMESTAMPTZ` | `datetime('now')` é UTC implícito e sem fuso; vigência e SLA precisam de fuso |
| `atributos TEXT` (JSON) | `JSONB` + índice GIN + JSON Schema | ADR-0005 |
| `INTEGER` booleano (`ativo`) | `BOOLEAN` | |
| `db.MIGRACOES` (lista de `ALTER TABLE`) | **EF Core Migrations** versionadas no repo | Migração precisa ser revisável, reversível e auditável |
| `UNIQUE (codigo)` | `UNIQUE (tenant_id, codigo)` | ADR-0003 |
| `payload TEXT` + `payload_hash` | `JSONB` + `payload_hash` + encadeamento com o hash anterior | Torna a trilha *tamper-evident*, não só imutável |
| `PRAGMA journal_mode=WAL` | WAL nativo + PgBouncer | |
| Views `vw_*` | Views, com materialização onde medir valer | |

O `db.MIGRACOES` merece destaque: ele resolve bem o problema de hoje, mas não tem
rollback, não versiona e não distingue ambientes. Em produção multiempresa isso não
se sustenta. Toda migração passa a ser **compatível para trás e em duas fases**
(expandir → migrar dados → contrair), porque não existe janela em que todos os
clientes possam ficar fora do ar.

## Consequências

### Positivas

- Uma fonte de verdade, um plano de backup e DR crítico, um modelo de consistência.
- Cada peça adicional tem função clara e é descartável/reconstruível.
- Busca deixa de ser gargalo antes de virar um.
- A decisão de grafo fica registrada com gatilho objetivo, em vez de virar debate
  recorrente.

### Negativas

- **Mais serviços para operar** que hoje (hoje: um arquivo `.db`). Redis, AI Search
  e Blob têm custo mensal mesmo ociosos.
- **Consistência eventual** entre Postgres e AI Search: um ativo publicado pode
  demorar segundos para aparecer na busca. Precisa ser explícito na UI e ter
  reindexação completa disponível.
- **Custo:** em dev, Redis e AI Search podem ser substituídos por contêiner local
  via Aspire; em produção, são linha de custo fixa.
- Closure table é redundância que precisa ser mantida correta — exige teste que
  compara a closure com a recursão pura.

### Neutras / a monitorar

- PgBouncer em *transaction pooling* interage com RLS (ADR-0003) e com prepared
  statements. Usar `set_config(..., true)` e cuidar da configuração do Npgsql.
- `EXCLUDE USING gist` com `tstzrange` resolve vigência sem sobreposição em
  `responsabilidade` e `atribuicao_papel` — hoje é regra de aplicação. Migrar
  quando conveniente; é ganho de correção grátis.

## Alternativas consideradas

**Azure SQL Database.** Integração excelente com .NET, elastic pools maduros para
multiempresa e RLS nativa. Recusada por margem estreita: JSONB e o ecossistema de
extensões (`pg_partman`, `pgvector`, `pg_stat_statements`, AGE no futuro) dão mais
caminho de evolução, e Postgres evita dependência de licença. Se o time já tivesse
DBA de SQL Server, a decisão inverteria.

**Azure Cosmos DB (NoSQL) como sistema de registro.** Escala horizontal e
multi-região com facilidade. Recusada: o domínio é fortemente relacional
(hierarquia, relações tipadas, validações, revisões) e depende de transação
multi-documento. Modelar `pre_check` com consistência eventual seria retrabalho em
cima de um problema que não existe.

**Banco de grafo como primário (Neo4j).** Modelaria `relacionamento_ativo`
naturalmente. Recusada: o produto é 80% governança transacional e 20% travessia.
Otimizar o primário para os 20% penaliza os 80%.

**MongoDB.** O `atributos` JSON sugere afinidade. Recusada: JSONB entrega o mesmo
com transação, constraint e RLS por cima.

**Um banco só, sem Redis nem AI Search.** Mais simples e mais barato. Recusada
parcialmente: aceitável na Fase 1 (é o que o Aspire permite localmente), mas
insustentável quando a busca for o gargalo. A porta `IIndiceDeBusca` (ADR-0001)
permite começar com uma implementação sobre `tsvector` do Postgres e trocar para
AI Search sem tocar no domínio — **este é o caminho recomendado para a Fase 1**.

## Revisitar quando

- Qualquer um dos três gatilhos de grafo acima disparar.
- A base de um único cliente passar de ~200 GB, ou o total passar de ~1 TB —
  avaliar elastic clusters e particionamento por `tenant_id`.
- O `snapshot_indicador` passar de ~50 milhões de linhas ou o dashboard executivo
  passar de 3 s no p95 — hora de Fabric.
- PostgreSQL entregar SQL/PGQ em versão estável e disponível no Azure.
