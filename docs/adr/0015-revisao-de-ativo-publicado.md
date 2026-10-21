# ADR-0015 — Completar o ciclo de revisão de ativo publicado

- **Status:** proposto
- **Data:** 2026-10-15
- **Relacionados:** ADR-0001 (máquina de ciclo de vida no domínio), ADR-0012 (regras portadas do painel-ddd)

## Contexto

A especificação (Proposta Consolidada, §2 e §3.3) diz que versão publicada é
imutável: alterar exige abrir revisão, e a versão publicada segue valendo
enquanto a revisão é preparada. A versão portada tinha dois defeitos nesse
caminho, reproduzidos contra a API:

1. **Publicado editável.** Nenhum comando conferia o status: um `PUT` alterava
   nome, descrição e criticidade de um ativo publicado, sem rito nenhum.
2. **Rejeição permanente.** `DecidirValidacao` só publicava quando *todas* as
   validações do ativo estavam aprovadas, inclusive as de revisões anteriores.
   Uma reprovação na revisão 1 impedia para sempre a publicação de qualquer
   revisão seguinte.

E faltava o caminho de revisão: o agregado tinha `AbrirRevisao`, mas não havia
caso de uso nem rota, e a máquina de estados não deixava submeter a partir de
`em_revisao`.

## Decisão

- **O cadastro só muda em `rascunho` ou `em_revisao`.** Nome, descrição,
  criticidade, atributos, pai, responsáveis e evidências. O agregado recusa
  (`ATIVO_NAO_EDITAVEL`) e os casos de uso devolvem a recusa como valor (409 na API).
- **Abrir revisão** (`POST /v1/ativos/{id}/revisoes`, ação `Revisar`): só de
  ativo publicado; leva a `em_revisao`.
- **Duas transições a mais** na máquina portada de `governanca.TRANSICOES`:
  `em_revisao → em_validacao` (submeter a revisão; o `servicos.submeter` do
  painel-ddd já aceitava, por fora da tabela) e `em_validacao → em_revisao`.
- **Rejeição volta ao lugar certo.** O ativo guarda `UltimaRevisaoPublicada`:
  nunca publicado volta a `rascunho`; já publicado volta a `em_revisao`.
- **Só a revisão atual decide.** A publicação considera apenas as etapas da
  revisão em validação. Reprovar uma etapa cancela as outras etapas abertas da
  mesma revisão (status `cancelada`), como no painel-ddd. Etapa de revisão
  antiga ainda aberta é recusada (`VALIDACAO_OBSOLETA`, 409).
- **Migração de dados:** a coluna `ultima_revisao_publicada` é preenchida para
  ativos já `publicado`, `em_revisao` ou `descontinuado`. O preenchimento suspende
  o RLS forçado dentro da transação da migration e o religa em seguida, porque
  sem empresa no contexto o `UPDATE` não enxergaria linha nenhuma.

## Consequências

### Positivas
- O rito vale para mudanças, não só para a primeira publicação.
- Uma reprovação deixa de travar o ativo para sempre.

### Negativas
- **A versão publicada ainda não é preservada durante a revisão.** A edição em
  `em_revisao` muda o próprio registro, e quem consulta o ativo vê a mudança
  antes da aprovação. A especificação pede o contrário (a versão vigente segue
  valendo, como uma versão LTS). Resolver exige guardar a revisão publicada
  como snapshot imutável, o que também falta (a `REVISAO_CATALOGO` com hash da
  especificação). Fica para um ADR próprio.
- Responsável e evidência também passam a exigir revisão num ativo publicado.
  Trocar o owner de um ativo publicado é, agora, uma revisão.

## Alternativas consideradas

- **Só bloquear a edição de publicado**, sem rota de revisão: deixaria todo
  ativo publicado congelado para sempre. Recusada.
- **Rejeição sempre para rascunho**, como a tabela portada: um ativo que já foi
  oficial deixaria de ser consumível por causa de uma revisão recusada. Recusada.

## Revisitar quando

- entrar o snapshot imutável da revisão publicada;
- surgir necessidade de alterar responsável sem revisão (ex.: desligamento).
