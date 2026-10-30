# ADR-0017 — Retratar cada revisão com hash e auditar na partição do ativo

- **Status:** proposto
- **Data:** 2026-10-30
- **Relacionados:** ADR-0015 (revisão de ativo publicado), ADR-0016 (Cosmos DB, partição por ativo), ADR-0008 (outbox)

## Contexto

O painel-ddd guarda, a cada submissão, o cadastro inteiro do ativo
(`revisao_catalogo.payload`) com o SHA-256 desse conteúdo (`payload_hash`), e
registra cada ação numa trilha (`auditoria_evento`: quem, quando, antes e
depois). A aba Histórico da visão 360° mostra as duas coisas, e o comparador
de revisões (`/ativo/<id>/comparar`) diz o que mudou de uma para a outra.

O Jornada não tinha nada disso. O ADR-0015 deixou anotado que sem o retrato
da revisão não há como provar o que foi aprovado, e o ADR-0016 já reservou o
documento `revisao` na partição do ativo.

## Decisão

- **Retrato na submissão.** `SubmeterAtivo` grava um `RevisaoDoAtivo` com o
  cadastro (nome, descrição, criticidade, tipo, pai, atributos, responsáveis
  vigentes, relações de saída, evidências) em JSON canônico: chaves em ordem
  fixa, listas ordenadas, acentos sem escape. O mesmo cadastro gera sempre o
  mesmo texto e, portanto, o mesmo hash. O que o próprio ciclo de vida mexe
  (status, datas, número) fica de fora, como os `CAMPOS_MECANICOS` do painel.
- **Imutável, com uma exceção.** O conteúdo e o hash nunca mudam. Ao publicar,
  `DecidirValidacao` marca a data de publicação, uma vez só
  (`REVISAO_JA_PUBLICADA` na segunda). `Integra` recalcula o hash na leitura:
  conteúdo alterado no banco aparece como "não confere", na API e na tela.
- **Auditoria só acrescenta.** Cada caso de uso que muda o catálogo registra
  um `RegistroDeAuditoria` (ação com o nome do painel, quem, quando, antes e
  depois). A edição registra só os campos que mudaram.
- **Tudo na partição do ativo, no mesmo lote.** No Cosmos, `revisao` (id
  `revisao-N`) e `auditoria` são documentos da partição `[empresa, idAtivo]`,
  gravados no `TransactionalBatch` do fato que os gera: a submissão, o retrato
  e a linha de auditoria entram juntos ou não entram. Duas submissões
  simultâneas da mesma revisão colidem no id do retrato.
- **Comparação no domínio.** `ComparadorDeRevisoes` compara campo a campo e
  resume cada valor numa linha; os ids (pai, destino de relação) viram código e
  nome pela consulta, que conhece o catálogo.
- **API:** `GET /v1/ativos/{id}/historico` (revisões e auditoria) e
  `GET /v1/ativos/{id}/revisoes/comparacao?a=&b=` (sem números, a última
  contra a anterior).

## Consequências

### Positivas
- O que foi aprovado fica provado: o retrato da revisão publicada, com hash.
- O validador ganha a base para ver "o que mudou" (fase 4 da paridade).
- Nenhuma escrita a mais fora do lote: retrato e auditoria não abrem janela de
  inconsistência com o ativo.

### Negativas
- **A relação é auditada fora do lote.** A aresta é gravada na hora pela porta
  de relações (ADR-0016 §2); a linha de auditoria vai logo depois. Se a segunda
  gravação falhar, a relação existe sem registro.
- **Revisões anteriores a este ADR não têm retrato.** A publicação delas segue
  funcionando; a comparação com elas devolve `REVISAO_NAO_ENCONTRADA`.
- **O retrato ainda não é servido no lugar do cadastro.** Durante uma revisão,
  quem consulta o ativo continua vendo o registro em edição, não a versão
  publicada (a "LTS" do ADR-0015). O retrato torna isso possível; servir a
  versão publicada é outra decisão.
- A partição do ativo cresce com a trilha. Um ativo muito movimentado chega
  perto do limite de 20 GB por chave lógica só depois de milhões de registros,
  mas a trilha não tem expurgo.

## Alternativas consideradas

- **Derivar a auditoria dos eventos da outbox.** Os eventos já vão no lote, mas
  não carregam quem agiu nem o antes e depois, e só existem para alguns fatos.
  Recusada.
- **Container próprio para auditoria, partição `/empresa`.** Facilita consultas
  da empresa inteira, mas perde a atomicidade com o fato auditado. Fica para
  quando houver auditoria sem ativo (concessão de papel, na fase 2).
- **Encadear os hashes (cada revisão com o hash da anterior).** Dá prova de
  ordem, além de prova de conteúdo. O painel não faz; sem demanda, recusada por ora.

## Revisitar quando

- for decidido servir a versão publicada durante a revisão (LTS);
- houver auditoria de ações sem ativo (fase 2) ou consulta de auditoria por empresa;
- a trilha precisar de retenção ou expurgo.
