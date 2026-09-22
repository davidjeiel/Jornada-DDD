# ADR-0005 — Tornar o metamodelo do catálogo configurável por empresa

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0003, ADR-0004, ADR-0012

## Contexto

A decisão de modelagem documentada em [`docs/MODELO.md`](../MODELO.md) — **item
genérico + metamodelo**, em vez de uma tabela por tipo de ativo — é a melhor
decisão técnica do projeto. A taxonomia vive em `catalogo/tipos.py`:

```python
TIPOS: dict[str, TipoAtivo] = {
    # bloco "Estrutura DDD"
    "dominio": ..., "subdominio": ..., "contexto": ..., "capacidade": ...,
    # bloco "Ativos técnicos"
    "sistema": ..., "aplicacao": ..., "repositorio": ..., "api": ...,
    "endpoint": ..., "base_dados": ..., "objeto_dado": ..., "evento": ...,
}

# e as relações válidas também são código:
DESTINOS_VALIDOS = {
    "implementa": ("capacidade",),
    "expoe":      ("api", "endpoint", "evento"),
    "consome":    ("api", "endpoint", "evento", "objeto_dado"),
    # ...
}
```

Adicionar um tipo de ativo é uma entrada num dicionário, não uma migração. Os
campos específicos vão para `item_catalogo.atributos` (JSON). A validação de
hierarquia — quem pode ser pai de quem — é regra de aplicação.

O problema ao virar produto multiempresa: **essa taxonomia está em código**.

Cada empresa tem seu próprio vocabulário e sua própria árvore. Uma financeira fala
em "jornada" e "produto"; uma indústria em "planta", "linha" e "ativo físico"; uma
empresa que usa TOGAF quer "capability", "value stream" e "application component".
Nenhuma delas vai adotar `dominio → subdominio → contexto → capacidade` sem
adaptação — e o valor de um catálogo cai a zero quando ele não fala a língua de
quem o preenche.

Com a taxonomia em código, atender a segunda empresa significa `if empresa == X`,
e atender a décima significa um fork. É o caminho clássico pelo qual um bom produto
vira dez consultorias.

## Decisão

Promover o metamodelo de **código** para **dado versionado por empresa**.

### 1. Estrutura

```
CatalogoDeTipos (por empresa, versionado)
 └── TipoDeAtivo
      ├── chave, rótulo (singular/plural), ícone, cor, bloco
      ├── paisPermitidos: [chave]        ← substitui a validação de hierarquia em código
      ├── relacoesPermitidas: [(tipoRelacao, tipoDestino)]
      ├── schemaDeAtributos: JSON Schema ← valida `atributos`
      └── pesosDeQualidade                ← parametriza qualidade.py por tipo
```

O **catálogo padrão** (a taxonomia DDD de hoje, exatamente como está em `tipos.py`)
é versionado no repositório e semeado em toda empresa nova. A empresa **estende e
ajusta**; não parte do zero.

```sql
CREATE TABLE metamodelo_versao (
  tenant_id     UUID NOT NULL,
  versao        INT  NOT NULL,
  definicao     JSONB NOT NULL,      -- o CatalogoDeTipos inteiro
  publicado_em  TIMESTAMPTZ,
  derivado_de   INT,                 -- versão do catálogo padrão que originou
  PRIMARY KEY (tenant_id, versao)
);
```

O metamodelo é **imutável por versão**. Editar publica uma nova versão. Isso é o
mesmo princípio de `revisao_catalogo` — que o projeto já aplica aos ativos —
aplicado à própria definição dos ativos. Coerência conceitual e, na prática, a
única forma de responder "por que este ativo foi aprovado em 2027 com aquelas
regras".

### 2. Validação em duas camadas

- **Estrutural**: JSON Schema por tipo, aplicado no adaptador de persistência antes
  de gravar `atributos`. Campo obrigatório ausente, tipo errado, enum inválido —
  rejeitado na borda.
- **De negócio**: continua no domínio (`PoliticaDePublicacao`, ADR-0001), porque
  "score mínimo 70 para criticidade alta" não é forma, é regra.

### 3. O domínio não vira genérico

Risco central deste ADR: transformar `Ativo` num saco de propriedades e perder toda
a modelagem rica. A fronteira:

| Continua **tipado e rico** no domínio | Passa a ser **dado** no metamodelo |
|---|---|
| Ciclo de vida (`rascunho → em_validacao → publicado → descontinuado`) | Quais tipos existem e como se chamam |
| Máquina de transições (`governanca.TRANSICOES`) | Quais tipos podem ser pai de quais |
| Segregação de função, RBAC escopado | Quais campos cada tipo tem |
| Revisão imutável + hash | Quais relações são válidas entre quais tipos |
| Dimensões do score (completude, consistência, ownership, evidência, temporalidade) | O **peso** de cada dimensão por tipo |
| Etapas de validação e política por criticidade | Quais etapas se aplicam a cada tipo |

Em outras palavras: **o metamodelo configura *o quê*; o domínio continua dono do
*como*.** O ciclo de vida de um ativo é idêntico em toda empresa — é isso que faz
o produto ser um produto e não um framework.

### 4. Evolução de metamodelo é evento de negócio

Quando uma empresa publica a versão N+1, os ativos existentes continuam válidos sob
a versão em que foram publicados. O caso de uso `MigrarMetamodelo` gera um plano —
"3 campos novos obrigatórios, 412 ativos ficarão com pendência de completude" — e
só executa com aprovação. É exatamente o par `plano_*`/`importar_*` que
`integracoes.py` já usa para descoberta: simular antes de gravar. Reaproveite o
padrão e o vocabulário.

### 5. Marketplace de metamodelos

Consequência natural e comercialmente relevante: metamodelos prontos e versionados
— **DDD** (o atual), **TOGAF/ArchiMate**, **BIAN** (bancos), **eTOM** (telecom) —
viram catálogos importáveis. É diferenciação de produto e insumo de precificação
(ADR-0010). Não construa na Fase 1; deixe a porta aberta.

## Consequências

### Positivas

- Uma base de código atende empresas com vocabulários incompatíveis.
- Onboarding de cliente vira configuração, não desenvolvimento.
- A árvore de tipos vira artefato de governança do próprio cliente, versionado e
  auditável — coerente com a tese do produto.
- `atributos` deixa de ser blob opaco: com JSONB + índice GIN + JSON Schema, passa
  a ser consultável e facetável na busca (ADR-0004).

### Negativas

- **Complexidade real.** Validar dinamicamente, versionar e migrar metamodelo é
  bem mais difícil do que ler um `dict` em Python.
- **Risco de configuração infinita.** Cliente que redefine tudo cria um catálogo
  que ninguém entende e um chamado de suporte por semana. Mitigação: catálogo
  padrão forte, limites por tier de plano, e restrições sobre o que pode ser
  alterado (tipo nunca some — é depreciado).
- **Suporte fica mais difícil**: reproduzir um bug exige o metamodelo do cliente.
  Obriga exportação de metamodelo como artefato de diagnóstico.
- Consulta sobre `atributos` é mais lenta que sobre coluna nativa, mesmo com GIN.
  Para campos usados em filtro de alta frequência, considere coluna gerada
  (`GENERATED ALWAYS AS (atributos->>'x') STORED`) com índice.

### Neutras / a monitorar

- O `tipos.blocos()` atual (usado por `acesso.BLOCOS_POR_PAPEL` para decidir quem
  escreve em quê) passa a vir do metamodelo. A autorização continua no domínio,
  mas lê a classificação do dado — exige atenção para que uma mudança de
  metamodelo não altere permissão sem passar por aprovação.
- A compatibilidade do catálogo padrão com `docs/MODELO.md` deve ser mantida: esse
  documento vira a especificação do metamodelo padrão v1.

## Alternativas consideradas

**Manter a taxonomia em código, igual hoje.** Simples, rápido, tipado. Recusada:
inviabiliza o produto multiempresa no primeiro cliente que pedir vocabulário
próprio, e esse pedido chega cedo.

**Uma tabela por tipo de ativo.** Já foi analisada e recusada em `docs/MODELO.md`,
pelos motivos certos; a configurabilidade por empresa só reforça a recusa.

**Metamodelo global com campos opcionais para todo mundo.** União de todos os
vocabulários de todos os clientes. Recusada: vira um catálogo com 200 campos, 190
deles vazios, e destrói a métrica de completude do `qualidade.py`.

**Metamodelo configurável por empresa, mas sem versionamento.** Bem mais simples.
Recusada: um ativo publicado sob regras que depois mudaram ficaria sem explicação
auditável — o produto perderia a própria tese.

## Revisitar quando

- A média de tipos customizados por empresa passar de ~30% do catálogo padrão —
  sinal de que o padrão está errado, não os clientes.
- Três ou mais clientes pedirem o mesmo tipo customizado — promova ao padrão.
- O tempo de validação de `atributos` aparecer no p95 de escrita.
