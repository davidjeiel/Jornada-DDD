# Cartilha do Catálogo Corporativo DDD

Guia de uso por papel. Cada papel tem um trabalho, e a ferramenta só mostra o seu.

O catálogo não é um formulário que todo mundo preenche igual. Ele distribui o trabalho:
quem conhece o negócio nomeia o significado, quem conhece a tecnologia registra a
realidade, e ninguém aprova o que escreveu. Esta cartilha mostra, para cada papel, o que
você consegue fazer, o que a ferramenta vai recusar e por quê — e a rotina que resolve o
seu dia.

> **A matriz de permissões abaixo é lida direto do domínio**, em
> [`PoliticaDeAutorizacao.cs`](../src/dominio/Jornada.Dominio/Acesso/PoliticaDeAutorizacao.cs),
> e a tabela de ritos, de [`PoliticasDeGovernanca.Padrao()`](../src/dominio/Jornada.Dominio/Governanca/PoliticaDeGovernanca.cs).
> Isso é herança direta do `painel-ddd`: lá, `test_cartilha.py` falha o build se este
> documento descrever uma regra que o código já não aplica. Aqui esse teste **ainda não
> existe** — é dívida assumida, não descuido; ver [Limitações](../README.md#limitações-desta-primeira-versão).
> Até lá, este arquivo é responsabilidade de quem mexe em `Acesso/` ou `Governanca/`: mudou
> a regra, atualiza a cartilha no mesmo PR.

| Papel | Você responde por |
| --- | --- |
| [Negócio](#negócio) | o significado |
| [Time técnico](#time-técnico) | a realidade técnica |
| [Arquiteto](#arquiteto) | a coerência do todo |
| [Curador](#curador) | manter o cadastro vivo |
| [Administrador](#administrador) | operar a ferramenta |
| [Consulta](#consulta) | ler, sem alterar nada |

---

## Os primeiros minutos

Vale para qualquer papel. Faça isto uma vez e o resto da cartilha faz sentido.

### 1. Escolha quem você é

Esta primeira versão ainda não tem identidade real (Entra External ID/Keycloak entram no
roteiro — ver [ADR-0007](adr/0007-identidade-autenticacao-autorizacao.md)). Em vez de
login, a tela tem um **seletor de usuários de teste**: clique no nome da empresa ou no seu
avatar, no rodapé do menu lateral. São doze pessoas — duas para cada papel —, espalhadas em
duas empresas (**Horizonte Seguros** e **Meridian Logística**) e quatro unidades. Trocar de
usuário troca empresa, papel e unidade de uma vez, e recarrega o catálogo inteiro: é assim
que se testa isolamento entre empresas e segregação de função sem abrir uma segunda aba.

A escolha fica salva no seu navegador — ao voltar, você continua como a mesma pessoa.

### 2. Entenda os quatro espaços de trabalho

- **Visão geral** — os números do dia e o ativo que você está olhando: pré-check, score e
  caminho até a publicação.
- **Catálogo** — o mapa dos quatro níveis da Estrutura DDD, a lista completa de ativos, e o
  cadastro de item novo.
- **Governança** — a fila de validações abertas, com os botões **Aprovar** e **Ajustes**.
- **Indicadores** — uma leitura agregada: publicados, validações abertas, composição por
  tipo.

### 3. Abra a ficha completa

Selecione um ativo na lista e clique em **Abrir ficha completa**. Ali vivem três abas:
**Detalhes** (editar nome, criticidade, descrição e atributos, e ver os responsáveis
vigentes), **Evidências** (anexar e listar ADRs, contratos, links) e **Relações**
(registrar e listar `implementa`, `expõe`, `consome`, `depende_de` com outros ativos). Se
o seu papel é **consulta**, a aba Detalhes some o formulário e mostra só os dados.

### Três regras que valem para todo mundo

**O papel autoriza a ação e o objeto.** Quem responde pelo negócio escreve na Estrutura
DDD — domínio, subdomínio, contextos delimitados, capacidade. Quem responde pela técnica
escreve nos ativos técnicos — sistema, aplicação, repositório, API, endpoint, base de
dados, objeto de dado, evento. Arquiteto, curador e admin atravessam os dois blocos. Fora
do seu bloco, a rota recusa mesmo que você monte a chamada à API na mão.

**Quem submete não decide.** Se você enviou uma revisão para validação, a ferramenta não
deixa você mesmo decidir sobre ela — nem se o seu papel permitir aquela etapa. É
segregação de função, e não tem exceção, nem para admin.

**Empresa é fronteira, unidade é escopo.** Trocar de empresa no seletor troca o catálogo
inteiro — cada empresa só vê os próprios ativos, sempre (Postgres aplica isso por Row-Level
Security, não só a tela). Unidade não isola nada; hoje ela só viaja junto com o ativo
cadastrado, como contexto.

---

## Negócio

`negocio`

Você responde pelo **significado**: que domínios a empresa tem, que capacidades eles
entregam, e o que é crítico de verdade. A tecnologia entra depois; sem o seu nome nas
coisas, ela não tem onde se apoiar.

**O que você faz**

- Cadastrar e editar Domínio, Subdomínio, Contextos Delimitados e Capacidade de negócio
- Assumir como owner (negocial) um ativo desse bloco
- Anexar evidência e submeter para validação
- Decidir a etapa **negocial** das validações

**O que a ferramenta recusa**

- Registrar relações entre ativos — quem implementa o quê é informação técnica: peça ao
  time técnico
- Cadastrar ou editar ativo técnico — sistema, aplicação, API, endpoint e base de dados
  são do outro bloco: para você eles são consulta

**Sua rotina**

1. Em **Catálogo**, clique em **Cadastrar ativo** e escolha o tipo. A ferramenta pede o
   item pai quando o tipo exige — capacidade sem contexto delimitado nem chega a abrir o
   formulário de destino.
2. Preencha os campos do tipo. Em domínio, a *visão de negócio* é obrigatória; em
   capacidade, o *resultado esperado* — sem eles, o pré-check já nasce bloqueado.
3. Selecione o ativo, abra a **ficha completa** e assuma como owner, se for o caso.
4. No painel de detalhe, acompanhe o **pré-check**: score, dimensões e o que falta.
5. Submeta quando o pré-check aprovar. A etapa negocial abre na fila de Governança.

> **Capacidade sem contexto delimitado não é erro de formulário — é a hierarquia
> protegendo o catálogo.** A trilha de um ativo sobe até a raiz; sem pai, ela não existe.

---

## Time técnico

`techlead`

Você responde pela **realidade técnica**: o que existe de fato, onde roda, e o que
conversa com o quê. É o seu registro que transforma "acho que essa API é usada por
alguém" em uma resposta.

**O que você faz**

- Cadastrar e editar Sistema, Aplicação, Repositório, API, Endpoint, Base de dados, Objeto
  de dado e Evento de negócio
- Registrar relações entre ativos
- Assumir como owner técnico, anexar evidência e submeter
- Decidir a etapa **técnica** das validações

**O que a ferramenta recusa**

- Decidir as etapas negocial e arquitetural — a menos que você seja o responsável formal
  daquele ativo, no papel correspondente
- Cadastrar ou editar a hierarquia de negócio — domínio, subdomínio, contexto e
  capacidade são do bloco de negócio: para você eles são consulta

**Sua rotina: trazer um ativo técnico com a vizinhança dele**

1. Cadastre a **API** (ou o tipo técnico que for) em **Catálogo › Cadastrar ativo**,
   informando o contrato e a versão.
2. Abra a **ficha completa** e, na aba **Relações**, registre o que ela `expõe`,
   `consome` ou de que `depende` — escolha o ativo de destino e o tipo de relação.
3. Na aba **Evidências**, anexe o link do contrato OpenAPI ou do repositório: é isso que
   conta na dimensão de evidência do score.
4. Assuma como owner técnico e submeta. O pré-check mostra a etapa técnica (e a
   arquitetural, se o tipo exigir) na Governança.

> **O tipo de relação importa.** `depende_de` é o único que o pré-check usa para detectar
> ciclo — dois ativos que dependem um do outro travam a publicação de ambos, de propósito.

---

## Arquiteto

`arquiteto`

Você responde pela **coerência do todo**: se os limites entre contextos fazem sentido, se
um contrato novo não cria um acoplamento que ninguém vai conseguir desfazer.

**O que você faz**

- Cadastrar e editar qualquer tipo de ativo, nos dois blocos
- Registrar relações entre ativos
- Decidir a etapa **arquitetural** — a última barreira antes de publicar contratos e
  contextos delimitados

**O que a ferramenta recusa**

- Decidir sobre uma revisão que você mesmo submeteu — segregação de função não tem
  exceção de papel

**Sua rotina: decidir com o histórico à vista**

1. Em **Governança**, veja a fila de validações abertas — a etapa de cada uma aparece no
   card.
2. Selecione o ativo pela aba **Catálogo** e abra a **ficha completa**: a aba Relações
   mostra tudo que ele consome e expõe, nas duas direções (quem depende dele também
   aparece, marcado como "entrada").
3. Volte à **Governança** e decida com **Aprovar** ou **Ajustes** — Ajustes devolve o
   ativo para rascunho.

> **"Entrada" na lista de relações é a pergunta que mais importa antes de aprovar uma
> mudança de contrato**: é quem vai sentir se a API mudar de forma incompatível.

---

## Curador

`curador`

Você **mantém o cadastro vivo**. Catálogo desatualizado vira ficção em poucos meses, e é
o seu trabalho que impede isso: achar o que está incompleto ou sem responsável, e fazer
alguém resolver.

**O que você faz**

- Cadastrar e editar qualquer tipo de ativo, nos dois blocos
- Registrar relações entre ativos
- Anexar evidência, assumir ativos e submeter para validação

**O que a ferramenta recusa**

- Decidir validações — por desenho: quem cuida do cadastro não é quem o aprova. Você
  prepara, outra pessoa valida

**O que o `painel-ddd` já dava ao curador, e esta versão ainda não tem**

- **Conceder papéis pleiteados** — hoje não existe fluxo de solicitação de acesso; a
  identidade é o seletor de usuários de teste (ver [Os primeiros minutos](#os-primeiros-minutos))
- **Importar descobertas e triar bandeja** — ADR-0012 lista descoberta automática como
  item do roteiro, não desta fase

**Sua rotina, com as telas de hoje**

1. Em **Indicadores**, veja a composição do catálogo por tipo e a taxa de publicados —
   é o retrato rápido de onde falta trabalho.
2. Em **Catálogo**, ordene mentalmente pelos ativos com criticidade alta e status
   `rascunho` há mais tempo — ainda não há um filtro de "score crescente" nesta versão.
3. Abra cada um, complete atributos e descrição pela ficha completa, e confira se tem
   owner. Sem owner do papel exigido pelo tipo, o pré-check bloqueia a submissão.
4. Submeta e deixe a decisão com quem tem o papel da etapa.

> **Você não decide validação, e isso é de propósito.** Se a mesma pessoa preenchesse e
> aprovasse, o rito seria decorativo.

---

## Administrador

`admin`

Você faz tudo o que os outros fazem. Nesta versão, sem console de administração próprio,
"administrar" ainda é sinônimo de "ter todos os papéis de escrita" — a ação
`Administrar` existe na política do domínio, mas nenhuma tela a usa ainda.

**O que você faz**

- Todas as ações de cadastro, edição, relação e submissão, nos dois blocos
- Decidir qualquer etapa de validação

**O que nem você escapa**

- Decidir sobre uma revisão que você mesmo submeteu — a segregação de função não tem
  exceção de papel
- Editar um ativo publicado sem que ele volte a rascunho por decisão de Ajustes — não
  existe ainda um fluxo de "abrir revisão" separado na interface

> Comandos de manutenção (varredura de SLA, notificação, recálculo de qualidade em lote)
> existem no `painel-ddd` como comandos de linha de comando. Aqui ainda não há
> `Jornada.Workers` (ver [ADR-0012](adr/0012-estrategia-de-migracao.md)) — o score é
> recalculado a cada pré-check, na hora, não em lote agendado.

---

## Consulta

`consulta`

Você lê tudo e não altera nada. Nenhuma ação de escrita da tabela abaixo está disponível
para o seu papel — a interface esconde os botões, e a API recusa mesmo que você a chame
direto.

**O que o catálogo responde para você**

- *"Quem é dono disto?"* — ficha completa, aba **Detalhes**, lista de responsáveis com a
  vigência de cada um.
- *"O que isso expõe ou de que depende?"* — ficha completa, aba **Relações**.
- *"Este ativo está pronto para uso?"* — o **status** na lista do Catálogo. Só
  `publicado` e `em_revisao` são consumíveis; `rascunho` não é promessa de nada.
- *"Quantos ativos a empresa tem, e como estão distribuídos?"* — **Indicadores**.

> **Trocando para um usuário de outra empresa** no seletor, o catálogo muda por completo —
> é a prova mais direta de que o isolamento é de banco, não de tela.

---

## Tabela de referência

A matriz completa, do jeito que o domínio aplica
([`PoliticaDeAutorizacao.cs`](../src/dominio/Jornada.Dominio/Acesso/PoliticaDeAutorizacao.cs)).
Onde a coluna diz "sim" mas não há tela para a ação hoje, a política já está pronta —
falta o fio até a interface.

### Quem pode fazer o quê

| Ação | Negócio | Time técnico | Arquiteto | Curador | Admin | Consulta | Tem tela hoje? |
| --- | :---: | :---: | :---: | :---: | :---: | :---: | --- |
| Cadastrar ativo | sim | sim | sim | sim | sim | — | sim |
| Editar ativo | sim | sim | sim | sim | sim | — | sim |
| Registrar relação | — | sim | sim | sim | sim | — | sim |
| Submeter para validação | sim | sim | sim | sim | sim | — | sim |
| Abrir revisão | sim | sim | sim | sim | sim | — | não |
| Decidir validação | sim | sim | sim | — | sim | — | sim |
| Importar descobertas | — | sim | — | sim | sim | — | não |
| Triar bandeja | — | sim | — | sim | sim | — | não |
| Descontinuar ativo | — | — | sim | sim | sim | — | não |
| Conceder papéis | — | — | — | sim | sim | — | não |
| Administrar a ferramenta | — | — | — | — | sim | — | não |

"Decidir validação" é permissão para **entrar na fila** — qual etapa você decide depende
do papel: *negocial* exige negócio, *técnica* exige time técnico, *arquitetural* exige
arquiteto. Admin decide qualquer uma. Em qualquer caso, o **responsável formal** do ativo
pode decidir a etapa correspondente ao papel de ownership que exerce sobre ele
(`OwnerNegocial`, `OwnerTecnico` ou `Arquiteto`).

### O rito de cada tipo de ativo

Governança proporcional: um endpoint não passa pelo mesmo rito de um contexto delimitado.
Quem define é a política do tipo e da criticidade
([`PoliticasDeGovernanca.Padrao()`](../src/dominio/Jornada.Dominio/Governanca/PoliticaDeGovernanca.cs)).

| Tipo | Etapas de validação | Evidências mínimas | Score mínimo | Prazo por etapa | Revisar a cada |
| --- | --- | :---: | :---: | :---: | :---: |
| Domínio | negocial → arquitetural | 1 | 70% | 72h | 365 dias |
| Subdomínio | negocial | 0 | 65% | 72h | 365 dias |
| Contextos Delimitados | negocial → técnica → arquitetural | 1 | 75% | 48h | 180 dias |
| Capacidade de negócio | negocial → técnica | 1 | 70% | 48h | 180 dias |
| Capacidade de negócio **crítica** | negocial → técnica → arquitetural | 2 | 80% | 24h | 90 dias |
| Sistema | técnica | 0 | 60% | 72h | 365 dias |
| Aplicação | técnica | 1 | 65% | 48h | 180 dias |
| Repositório | técnica | 1 | 60% | 72h | 180 dias |
| API | técnica → arquitetural | 1 | 70% | 48h | 120 dias |
| API **crítica** | negocial → técnica → arquitetural | 2 | 80% | 24h | 90 dias |
| Endpoint | técnica | 0 | 55% | 72h | 180 dias |
| Base de dados | técnica | 0 | 60% | 72h | 365 dias |
| Objeto de dado | técnica | 0 | 55% | 72h | 365 dias |
| Evento de negócio | técnica → arquitetural | 1 | 70% | 48h | 120 dias |

> **Marcar um ativo como crítico aperta o rito de verdade**: mais uma etapa, mais uma
> evidência, score mais alto e metade do prazo. Use a criticidade pelo risco real, não por
> importância percebida — a política acima é por (tipo, criticidade), e "crítica" muda a
> linha inteira.

---

## Quando algo não funciona

As confusões mais prováveis com a versão atual, e o que fazer com cada uma.

**"O botão que eu usava sumiu"**
Duas causas, nesta ordem: o seu **papel** não permite aquela ação (veja a tabela acima);
ou o ativo é de um **bloco que o seu papel não escreve** — negócio não mexe em ativo
técnico e vice-versa. Confira quem você é no rodapé do menu lateral.

**"Não consigo decidir esta validação"**
Ou a **etapa exige um papel** que você não tem — troque para um usuário de teste com o
papel certo —, ou **você mesmo submeteu** essa revisão. No segundo caso não há o que
ajustar: escolha outro usuário de teste (de empresa e papel compatíveis) para decidir.

**"Meu ativo não publica"**
Abra a **ficha completa** e olhe o **pré-check**, no painel de detalhe: score, dimensões
e bloqueios nomeados. O mais comum é evidência faltando ou owner do tipo certo ausente —
anexe uma evidência ou assuma o ativo, e o score sobe.

**"Cadastrei em uma empresa e não vejo em outra"**
Esperado, não é bug: cada empresa é isolada por Row-Level Security no Postgres
([ADR-0003](adr/0003-modelo-multi-tenancy.md)). Troque de usuário para uma pessoa da
outra empresa no seletor — o catálogo é outro, de propósito.

**Recursos do `painel-ddd` que esta cartilha não promete ainda**
Busca global, "Minha mesa", notificações (sino), grafo navegável de relações, painel
executivo clicável, descoberta automática por OpenAPI/Git, e o fluxo de solicitação de
papel — todos estão no roteiro (ver [ADR-0012](adr/0012-estrategia-de-migracao.md) e o
README, seção "O que ainda não existe"), nenhum está construído nesta fase. Se você
esperava um desses e não achou, não é engano seu.

---

*Versão correspondente ao Grupo 2 do plano de containers — fluxo de preenchimento
completo (editar, anexar evidência, relacionar) e seletor de usuários de teste.*
