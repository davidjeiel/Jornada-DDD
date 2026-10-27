# ADR-0016 — Cosmos DB (API NoSQL) como sistema de registro do catálogo

- **Status:** proposto
- **Data:** 2026-10-21
- **Relacionados:** ADR-0003 (isolamento por empresa), ADR-0004 (Postgres como
  sistema de registro), ADR-0008 (outbox), ADR-0015 (revisão de ativo publicado)
- **Substitui parcialmente:** ADR-0004 (o sistema de registro) e ADR-0003 §3
  (o isolamento passa a ser imposto pela chave de partição, não pelo RLS)

## Contexto

A gestão pediu Cosmos DB no lugar do Postgres, seguindo a remodelagem feita pelo
Thales na prova de conceito do painel-ddd (branch `poc/painel-ddd`, Python). A
API escolhida é a NoSQL.

A modelagem da POC é híbrida: um documento central por ativo, com responsáveis,
relações e evidências embutidos, e containers separados para o que tem ciclo de
vida próprio (revisões com hash SHA-256, validações, contadores, auditoria,
pessoas e notificações). A chave de partição é, na maioria dos containers, o
próprio id do documento.

Ela foi feita para o painel-ddd, que atende uma empresa só. O Jornada atende
várias (ADR-0003), e três pontos da POC não sobrevivem a essa diferença:

1. **Partição pelo id do ativo.** Toda listagem de uma empresa vira consulta em
   todas as partições, e o isolamento entre empresas passa a depender só de um
   filtro no código. Hoje quem garante isso é o RLS do Postgres.
2. **Sem transação entre documentos.** A POC grava cada documento na hora
   (`commit()` virou no-op) e registra o risco. No Jornada, três fluxos precisam
   ser atômicos: ativo + evento na outbox (ADR-0008); submeter (ativo muda de
   status e as validações nascem); decidir (validação decidida, etapas irmãs
   canceladas, ativo publicado ou rejeitado, ADR-0015). Na POC, validações e
   revisões ficam em partições próprias, e nenhuma transação as alcança.
3. **Políticas por tipo.** No Jornada, metamodelo e políticas são por empresa
   (ADR-0005).

O emulador Linux (vNext) foi testado em 2026-10-21 com o SDK .NET 3.63 e
suporta tudo de que esta decisão depende: chave de partição hierárquica com
consulta por prefixo, `TransactionalBatch` (inclusive o desfazer quando uma
operação falha), concorrência otimista por ETag, `patch` com incremento,
change feed e `JOIN` em array.

## Decisão

### 1. Partição: empresa primeiro, sempre

Container `catalogo` com **chave de partição hierárquica `[/empresa, /idAtivo]`**.

- Ler um ativo é leitura pontual pela chave completa, tão barata quanto na POC.
- Listar, filtrar e buscar dentro de uma empresa é consulta pelo **prefixo**
  `[empresa]`: só alcança as partições físicas daquela empresa.
- Cada ativo é uma partição lógica pequena; o limite de 20 GB por partição
  lógica deixa de ser preocupação, inclusive para empresa grande.

### 2. Tudo o que muda junto com o ativo mora na partição do ativo

Na mesma partição `[empresa, idAtivo]`, discriminados por um campo `tipo`:

| Documento | Conteúdo |
|---|---|
| `ativo` | o agregado, com `responsaveis[]`, `evidencias[]` e `atributos` embutidos |
| `validacao` | uma etapa de validação de uma revisão |
| `relacao` | uma aresta que SAI deste ativo (origem = este ativo) |
| `evento` | evento de domínio pendente de despacho (outbox) |
| `revisao` | snapshot imutável da revisão publicada, com hash (quando o ADR do "LTS" entrar) |

Com isso, `IUnidadeDeTrabalho.ConfirmarAsync` grava um ativo, suas validações e
seus eventos num **`TransactionalBatch` por partição**: ou tudo entra, ou nada.

Relações viram documentos próprios, e não um array dentro do ativo como na POC,
porque o agregado `Ativo` não é dono das relações: regravar o ativo não pode
apagar arestas gravadas por outro caso de uso. As relações de entrada saem de
uma consulta por prefixo da empresa (`WHERE c.destino = @id`).

### 3. Outros containers

| Container | Chave | Conteúdo |
|---|---|---|
| `contadores` | `/empresa` | sequencial do código por tipo (`patch` com incremento) |

Pessoas, papéis, notificações, auditoria e indicadores ficam fora deste passo:
o Jornada ainda não tem portas para eles. Quando tiverem, seguem a mesma regra:
empresa no início da chave.

### 4. Isolamento sem RLS: três camadas

O RLS do Postgres não tem equivalente no Cosmos. A chave de acesso enxerga a
conta inteira. O isolamento passa a ser imposto assim:

1. **Um ponto único de acesso.** O adaptador só fala com o Cosmos por uma
   classe que monta a chave de partição a partir do contexto de tenant da
   requisição. Nenhum caso de uso passa a empresa (continua valendo o ADR-0003
   §2), e nenhuma consulta sai sem o prefixo da empresa.
2. **Uma guarda de arquitetura** que falha o build se `Microsoft.Azure.Cosmos`
   for usado fora desse ponto.
3. **A suíte de contrato** (`tests/Jornada.Persistencia.Testes`) roda contra o
   adaptador Cosmos e prova, em todas as consultas, que uma empresa não enxerga
   nem grava dado de outra.

Em produção, a aplicação usa identidade gerenciada com o papel de dados de
menor privilégio (sem operações de gerenciamento da conta), e não a chave
mestra.

### 5. Concorrência e eventos

- Toda gravação de documento lido usa o ETag (`IfMatchEtag`). Edição
  concorrente vira conflito explícito em vez de última-gravação-vence. O
  adaptador Postgres não tinha token de concorrência.
- O despacho da outbox lê o change feed do container `catalogo`, filtra
  `tipo = evento` e publica no Service Bus (ADR-0008 continua valendo; muda só
  de onde o despachante lê).

### 6. Identificadores

O Jornada mantém os ids `Guid` (v7). O `id` do documento é o Guid em texto. O
código legível (`SIS-0001`) continua vindo do sequencial por empresa e tipo. A
POC preservou ids inteiros porque migrava dados do SQL Server; aqui não há esse
legado.

### 7. O Postgres sai do produto

Decisão da gestão em 2026-10-21: o Jornada fica só com o Cosmos DB. O adaptador
`Jornada.Persistencia.Postgres`, as migrations, o `Esquema.sql` (RLS), o
PgBouncer e os jobs de CI de RLS foram removidos. O adaptador em memória fica:
é a base dos testes de caso de uso e o modo sem infraestrutura da API.
`Persistencia:Provedor` aceita só `cosmos` e `memoria`; qualquer outro valor
derruba a partida, em vez de cair em silêncio na memória.

Funcionalidade nova que grava dados é implementada em memória e em Cosmos, e
passa na mesma suíte de contrato.

## Consequências

### Positivas
- Um banco só para operar, testar e documentar.
- Atomicidade preservada exatamente onde o domínio precisa dela (o agregado e o
  que nasce com ele), sem transação distribuída.
- Leitura de ativo por chave completa e listagem por empresa sem consulta em
  todas as partições.
- Concorrência otimista que hoje não existe.
- O snapshot da revisão publicada (pendente desde o ADR-0015) encontra um lugar
  natural: um documento imutável na partição do ativo.

### Negativas
- **Sem volta rápida para o Postgres.** O adaptador foi removido; voltar exige
  reescrevê-lo a partir do histórico do git e passar de novo na suíte de contrato.
- **O isolamento deixa de ser imposto pelo banco.** Uma falha no ponto único de
  acesso vaza dados entre empresas, sem segunda barreira no banco. As três
  camadas da seção 4 reduzem, mas não eliminam, esse risco. Cliente que exija
  isolamento físico precisa do tier Dedicado (ADR-0003 §4): container ou conta
  própria.
- **Não há transação entre ativos.** Um caso de uso que altere dois ativos na
  mesma confirmação grava em dois lotes, e o segundo pode falhar depois do
  primeiro. Hoje nenhum caso de uso faz isso; se surgir, precisa de
  compensação explícita.
- **Unicidade de nome continua só na aplicação.** Era a mesma situação no
  adaptador Postgres, que também não tinha índice único de nome. Fechar a
  janela de corrida exige um documento de reserva por (empresa, tipo, nome).
- **Consultas que eram SQL viram código.** A CTE recursiva de dependência
  circular vira busca em profundidade sobre as arestas da empresa, lidas numa
  consulta por prefixo. Aceitável na
  escala de um catálogo por empresa (centenas a poucos milhares de ativos).
- **O emulador não reproduz custo nem índices.** RU e índices de intervalo ou
  compostos não existem no emulador vNext. Desempenho e custo só se medem numa
  conta real.

### Neutras / a monitorar
- Consumo de RU por listagem por empresa conforme o catálogo cresce.
- Tamanho do documento `ativo` (limite de 2 MB), caso evidências ou atributos
  cresçam sem controle.

## Alternativas consideradas

- **A modelagem da POC como está** (chave `/id_item`, containers por conceito).
  Recusada pelos três motivos do contexto: listagem em todas as partições,
  isolamento só no código e fluxos de governança sem atomicidade.
- **Chave só `/empresa`.** Listagem barata e transação em toda a empresa, mas
  uma empresa grande esbarra no limite de 20 GB e de vazão por partição lógica,
  e todas as escritas de uma empresa disputam a mesma partição. A chave
  hierárquica dá o mesmo prefixo sem esse teto.
- **Container por empresa.** Isolamento físico, mas custo e limite de containers
  por conta inviabilizam o tier Padrão. Fica como forma do tier Dedicado.
- **Relações embutidas no ativo**, como na POC. Recusada: o agregado não é dono
  das relações e regravá-lo apagaria arestas (ver seção 2).

## Revisitar quando

- um caso de uso precisar alterar dois ativos de forma atômica;
- o consumo de RU de listagem passar do orçamento do tier Padrão;
- entrar o snapshot imutável da revisão publicada (ADR próprio, sobre esta
  modelagem).
