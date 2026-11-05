# Cartilha do Catálogo Corporativo DDD

Guia de uso por papel. Cada papel tem um trabalho, e a ferramenta só mostra o seu.

O catálogo não é um formulário que todo mundo preenche igual. Ele distribui o trabalho:
quem conhece o negócio nomeia o significado, quem conhece a tecnologia registra a
realidade, e ninguém aprova o que escreveu. Esta cartilha mostra, para cada papel, o que
você consegue fazer, o que a ferramenta vai recusar e por quê — e a rotina que resolve o
seu dia.

> **As tabelas de referência não estão neste arquivo.** A matriz "quem pode fazer o quê",
> as etapas de cada papel e o rito de cada tipo de ativo são lidos das regras que a API
> aplica ([`PoliticaDeAutorizacao.cs`](../src/dominio/Jornada.Dominio/Acesso/PoliticaDeAutorizacao.cs)
> e [`PoliticasDeGovernanca`](../src/dominio/Jornada.Dominio/Governanca/PoliticaDeGovernanca.cs))
> e servidos em `GET /v1/cartilha`; a tela Cartilha os mostra na
> [Tabela de referência](#tabela-de-referência). É herança do `painel-ddd`: uma cartilha que
> repete a regra à mão envelhece em silêncio na primeira mudança. Este arquivo guarda só a
> prosa: a rotina, os avisos e as dúvidas de cada papel.

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

Esta versão ainda não tem identidade real (Entra External ID/Keycloak entram no roteiro —
ver [ADR-0007](adr/0007-identidade-autenticacao-autorizacao.md)). Em vez de login, abra
**Seu perfil** (o seu nome, ou "Quem é você?", no alto da tela) e escolha, em **Quem é
você**, a pessoa cadastrada que você é. No ambiente local há doze pessoas de teste, uma por
papel em cada uma das duas empresas (**Horizonte Seguros** e **Meridian Logística**); os
botões das empresas trocam o catálogo inteiro.

O papel **não se escolhe**: é o concedido à pessoa ([ADR-0018](adr/0018-papel-concedido-e-rito-de-acesso.md)).
Sem cadastro, use **Solicitar acesso**: informe matrícula, nome, e-mail e unidade e diga de
que papel precisa. O cadastro não concede nada; até um curador ou administrador decidir,
você consulta o catálogo inteiro e acompanha o pedido em Seu perfil.

Em Seu perfil também fica o **modo de trabalho**: em **Leitura**, nada que escreve
aparece, e a API recusa toda escrita, mesmo que o seu papel permita.

A escolha fica salva no seu navegador — ao voltar, você continua como a mesma pessoa.

### 2. Entenda os quatro espaços de trabalho

- **Visão geral** — os números do dia e o ativo que você está olhando: pré-check, score e
  caminho até a publicação.
- **Catálogo** — o mapa dos quatro níveis da Estrutura DDD, a lista completa de ativos, e o
  cadastro de item novo.
- **Catálogo** — o recorte do catálogo: filtre por tipo, status, criticidade, squad,
  origem (cadastro manual ou descoberta automática) ou só os ativos **sem responsável**,
  e ordene como quiser. Cada filtro ativo vira um chip que se remove sozinho, o total é
  o do recorte inteiro, e o recorte volta na sua próxima visita até você **limpar**. A
  busca do topo sugere ativos a partir de duas letras.
- **Validações** — a fila de etapas abertas, com uma aba por etapa e a contagem de cada
  uma, a mais crítica primeiro e o tempo até o prazo. Ao abrir uma etapa, o painel mostra
  as evidências, os responsáveis e **o que mudou** desde a revisão anterior. **Assuma** a
  análise para os outros saberem que está com você, e decida com parecer; com "ir para a
  próxima" marcado, a fila já abre a seguinte.
- **Minha mesa** — o que você assumiu, a fila livre, os seus rascunhos e o que você
  submeteu e espera decisão.
- **Indicadores** — uma leitura agregada: publicados, validações abertas, composição por
  tipo.

### 3. Abra a ficha do ativo

Selecione um ativo na lista e clique em **Abrir a ficha do ativo**: cada ativo tem a sua
página, com endereço próprio (dá para mandar o link). No alto, a **trilha** até o pai
direto (Domínio › Subdomínio › Contexto), com cada degrau clicável. Embaixo, cinco abas:

- **Resumo**: os dados e os campos do tipo (editáveis em rascunho ou revisão), o score do
  pré-check e o **caminho até a publicação**, com os cinco passos, o que falta em cada um e
  o link para a aba que resolve;
- **Relações**: as relações nos dois sentidos, com o **mecanismo** (síncrono, assíncrono,
  batch, arquivo, biblioteca) e a criticidade da dependência. A busca de destino sugere os
  tipos que fazem sentido para a relação escolhida ("implementa" propõe capacidades), e
  **Relacionar em lote** registra várias de uma vez. A mesma relação não se grava duas
  vezes, e uma `depende_de` que fecharia um ciclo é recusada;
- **Pessoas**: os responsáveis, e quem define um novo;
- **Evidências**: os ADRs, contratos e links anexados;
- **Histórico**: as revisões, com o hash de cada uma, e a trilha de auditoria.

Para cadastrar, **Novo ativo** oferece os tipos dos blocos que o seu papel escreve, com os
campos de cada tipo, e mostra ao lado o **rito de publicação** daquele tipo e criticidade:
as etapas, o score mínimo, as evidências e o prazo. Escolha também o **squad** que cuida do
ativo: um papel concedido "nos ativos de um squad" vale exatamente nesses ativos. Se a
ferramenta recusar o cadastro, o que você digitou continua no formulário.

A aba aberta fica no endereço: depois de salvar, você continua nela. Sem papel de escrita,
ou em modo de leitura, os formulários somem e ficam só os dados.

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
inteiro — cada empresa só vê os próprios ativos, sempre (o banco guarda cada empresa na
própria partição e toda consulta leva a empresa, não só a tela). Unidade não isola nada; hoje ela só viaja junto com o ativo
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
- Despachar os **Pleitos de acesso**, no grupo Administração do menu: você concede todo
  papel menos o de administrador, e não decide o próprio pleito; negar, ou conceder papel
  diferente do pedido, exige resposta
- Cadastrar e manter os **Squads**: título, sigla (única, até 20 letras) e descrição.
  Inativar um squad não apaga os vínculos que ele já tem

**O que a ferramenta recusa**

- Decidir validações — por desenho: quem cuida do cadastro não é quem o aprova. Você
  prepara, outra pessoa valida

**O que o `painel-ddd` já dava ao curador, e esta versão ainda não tem**

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

Você faz tudo o que os outros fazem, e opera a ferramenta: é quem mantém os cadastros das
pessoas e é o único que concede o papel de administrador.

**O que você faz**

- Todas as ações de cadastro, edição, relação e submissão, nos dois blocos
- Decidir qualquer etapa de validação
- Despachar os **Pleitos de acesso**, inclusive os de administrador
- Na **Gestão de acessos**: buscar qualquer cadastro, inclusive os inativos; corrigir nome,
  e-mail, unidade e squad; conceder um papel direto ou revogar um papel (a revogação vale
  na hora); inativar quem saiu, um a um ou em lote. Inativar encerra todos os papéis, e
  reativar não devolve nenhum
- Excluir um cadastro que não tem vínculo nenhum com o histórico. Com vínculo (papel,
  pleito, responsabilidade, revisão, validação ou auditoria), inative em vez de excluir
- Cadastrar e manter os **Squads**, como o curador

**O que nem você escapa**

- Decidir sobre uma revisão que você mesmo submeteu — a segregação de função não tem
  exceção de papel
- Mexer no próprio acesso: você não se inativa, não se exclui, não revoga os próprios
  papéis e não concede papel a si mesmo. Outro administrador faz isso por você
- Editar um ativo publicado sem abrir revisão — publicado é imutável. O botão
  **Abrir revisão**, no painel do ativo, leva o ativo para `em_revisao`; aí a edição
  volta a valer, e a revisão passa de novo pelo rito. Se a revisão for reprovada, o
  ativo volta para `em_revisao` (não para rascunho: ele já foi oficial)

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

> **Trocando de empresa** em Seu perfil, o catálogo muda por completo —
> é a prova mais direta de que o isolamento é de banco, não de tela.

---

## Tabela de referência

As tabelas abaixo saem das regras que a API aplica, não de um texto: mudou a regra no
código, muda esta página. Na leitura do repositório elas não aparecem; abra a Cartilha na
ferramenta, ou chame `GET /v1/cartilha`.

"Decidir validação" é permissão para **entrar na fila**: qual etapa você decide depende do
papel. *Negocial* exige negócio, *técnica* exige time técnico, *arquitetural* exige
arquiteto, e o admin decide qualquer uma. Em qualquer caso, o **responsável formal** do
ativo pode decidir a etapa correspondente ao papel de ownership que exerce sobre ele
(`OwnerNegocial`, `OwnerTecnico` ou `Arquiteto`).

Governança proporcional: um endpoint não passa pelo mesmo rito de um contexto delimitado.
**Marcar um ativo como crítico aperta o rito de verdade**: mais uma etapa, mais uma
evidência, score mais alto e metade do prazo. Use a criticidade pelo risco real, não por
importância percebida.

---

## Quando algo não funciona

As confusões mais prováveis com a versão atual, e o que fazer com cada uma.

**"O botão que eu usava sumiu"**
Duas causas, nesta ordem: o seu **papel** não permite aquela ação (veja a tabela acima);
ou o ativo é de um **bloco que o seu papel não escreve** — negócio não mexe em ativo
técnico e vice-versa. Confira quem você é, e se está em modo de leitura, em Seu perfil.

**"Não consigo decidir esta validação"**
Ou a **etapa exige um papel** que você não tem — peça o papel em Solicitar acesso, ou, no
ambiente de teste, entre como uma pessoa que o tenha —, ou **você mesmo submeteu** essa
revisão. No segundo caso não há o que ajustar: outra pessoa tem de decidir. Confira também
se você não está em modo de leitura.

**"Meu ativo não publica"**
Abra a **ficha completa** e olhe o **pré-check**, no painel de detalhe: score, dimensões
e bloqueios nomeados. O mais comum é evidência faltando ou owner do tipo certo ausente —
anexe uma evidência ou assuma o ativo, e o score sobe.

**"Cadastrei em uma empresa e não vejo em outra"**
Esperado, não é bug: cada empresa é isolada pela chave de partição no Cosmos DB
([ADR-0016](adr/0016-cosmos-db-como-sistema-de-registro.md)). Troque de empresa em Seu perfil — o
catálogo é outro, de propósito.

**Recursos do `painel-ddd` que esta cartilha não promete ainda**
Busca global, "Minha mesa", notificações (sino), grafo navegável de relações, painel
executivo clicável e descoberta automática por OpenAPI/Git — todos estão no roteiro (ver [ADR-0012](adr/0012-estrategia-de-migracao.md) e o
README, seção "O que ainda não existe"), nenhum está construído nesta fase. Se você
esperava um desses e não achou, não é engano seu.

---

*Versão correspondente ao Grupo 2 do plano de containers — fluxo de preenchimento
completo (editar, anexar evidência, relacionar) e seletor de usuários de teste.*
