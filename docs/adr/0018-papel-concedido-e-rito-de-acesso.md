# ADR-0018 — Papel concedido por pleito, pessoas na partição de acesso

- **Status:** proposto
- **Data:** 2026-11-03
- **Relacionados:** ADR-0003 (empresa como fronteira), ADR-0007 (identidade e autorização), ADR-0016 (Cosmos DB), ADR-0017 (auditoria)

## Contexto

Até a fase 1, o papel de quem age chegava no cabeçalho `X-Papel`, e a falta
dele valia curador. Qualquer pessoa escolhia o próprio papel. Isso era
intencional durante a validação do MVP, mas o painel-ddd já tem o rito inteiro
(`acesso.solicitar_acesso` / `decidir_solicitacao`):

- a pessoa se cadastra com matrícula, nome, e-mail e unidade;
- o cadastro não concede nada, e ela consulta o catálogo até alguém decidir o
  pleito dela;
- só curador ou admin decidem, e só admin cria outro admin;
- ninguém decide o próprio pleito, e negar, ou conceder papel diferente do
  pedido, exige resposta;
- o papel pode valer só num domínio.

O usuário decidiu (plano de paridade, 2026-10-27) que o papel passa a ser
concedido, e que a escolha livre fica atrás de um modo de demonstração, ligado
só no ambiente local.

## Decisão

- **Pessoa e pleito no domínio.** `Pessoa` guarda o cadastro e as atribuições
  de papel; `DadosDeCadastro.Conferir` porta `validar_cadastro`, com os formatos
  do painel: matrícula de uma letra e seis números, unidade de quatro. Os
  formatos ficam fixos, como no painel; torná-los configuráveis por empresa
  fica para quando houver uma empresa que precise. `PleitoDeAcesso.Decidir`
  porta as regras de `decidir_solicitacao`, na mesma ordem: já decidido,
  segregação de função, alçada de quem decide e resposta. `PodeConceder` entra
  na `PoliticaDeAutorizacao`.
- **O ator vem da pessoa.** O middleware lê `X-Pessoa` e `X-Modo`. Na primeira
  vez que um caso de uso pede, `AtorDaRequisicao` lê a pessoa e monta o ator
  com os papéis concedidos e vigentes. É uma leitura pontual por requisição, e
  a revogação vale na chamada seguinte (ADR-0007 §2). Sem pessoa, o ator é
  anônimo: consulta tudo e não escreve nada.
- **Recusa com motivo.** Escrita sem pessoa devolve 401 `PESSOA_NAO_IDENTIFICADA`
  (a tela vai para Seu perfil). Escrita em modo de leitura devolve 403
  `MODO_LEITURA`, mesmo para admin. O resto continua 403 `SEM_PERMISSAO`.
- **Escopo de domínio de verdade.** `AlvoDoAtivo` sobe a hierarquia até o
  domínio, como o `_dominio_do_item` do painel, e todo caso de uso passa a
  montar o alvo por ali. No cadastro, o domínio é o do pai. A concessão aceita
  escopo `empresa` ou `dominio`; `squad` chega com o cadastro de squads.
- **`X-Papel` só em demonstração.** Com `Acesso:ModoDemonstracao=true`, o
  `X-Papel` substitui os papéis concedidos, para curl e apresentações. O
  `local/compose.yaml` liga o modo; o `appsettings.json` o deixa desligado.
- **Primeiro administrador pela configuração.** Sem admin, não há quem decida o
  pleito de admin. A matrícula listada em `Acesso:AdministradoresIniciais` que
  pleitear admin numa empresa ainda sem admin é aprovada na hora, sem decisor,
  com a explicação na resposta. Depois disso, admin só sai do rito normal.
- **Cosmos: uma partição de acesso por empresa.** Pessoas, pleitos e a reserva
  de cada matrícula (`matricula-C123456`) ficam em `[empresa, "acesso"]`, no
  container do catálogo. Pessoa nova e a reserva entram no mesmo lote:
  matrícula repetida derruba o lote inteiro. Decidir um pleito grava o pleito e
  a pessoa com o papel novo juntos. As atribuições ficam embutidas na pessoa,
  porque são lidas junto com ela a cada requisição.
- **Carga de demonstração.** As 12 pessoas de teste (seis por empresa, com os
  ids do front) entram com o papel já concedido, em empresa sem pessoas, mesmo
  que o catálogo já exista.
- **API:** `GET /v1/perfil`, `GET /v1/pessoas`, `POST /v1/pleitos` (aberta a
  quem não tem papel), `GET /v1/pleitos` (só quem concede) e
  `POST /v1/pleitos/{id}/decisoes`.

## Consequências

### Positivas
- Papel passa a ser fato registrado, com quem concedeu, quando e por quê.
- Papel com escopo de domínio deixa de ser letra morta: antes a política sabia
  filtrar por domínio, mas nenhum caso de uso dizia de que domínio era o ativo.
- O front deixa de decidir papel: menu, assistente de cadastro e botões seguem
  o que a API diz em `GET /v1/perfil`.

### Negativas
- **A identidade continua declarada.** `X-Pessoa` é tão falsificável quanto o
  seletor de pessoa do painel. O que muda é o papel: quem se passa por alguém
  ganha só os papéis daquela pessoa, não os que escolher. A identidade de
  verdade continua sendo o ADR-0007.
- **Uma leitura a mais por requisição de escrita.** A leitura pontual custa 1 RU
  e só acontece quando um caso de uso pede o ator. Um cache curto, invalidado
  pela concessão, fica para quando a carga pedir.
- **Partição de acesso única por empresa.** Uma empresa com centenas de
  milhares de pessoas e pleitos chegaria perto do limite de 20 GB por chave
  lógica muito antes de qualquer ativo. Para o volume esperado (milhares) é
  folga de sobra; revisitar se não for.
- **Dois pleitos simultâneos da mesma pessoa podem passar.** A checagem de
  pleito pendente é leitura seguida de escrita. No recadastro com dados
  alterados, o ETag da pessoa protege; com os mesmos dados, não. O painel tem o
  mesmo comportamento.
- **A concessão não vai para a trilha de auditoria.** O pleito já registra quem
  decidiu, quando, o quê e a resposta. A trilha por empresa (ADR-0017, "revisitar
  quando") fica para a gestão de acessos (2b), que revoga papel fora de pleito.
- **Os avisos de pleito aberto e decidido** ficam para a fase 8 (notificações).

## Alternativas consideradas

- **Container próprio de pessoas, partição `/empresa`.** Mesma consulta, mas
  um segundo container para provisionar e monitorar, sem ganho de isolamento:
  a empresa já é o primeiro nível da chave do catálogo. Recusada.
- **Uma partição por pessoa.** Escala sem teto, mas a reserva da matrícula e o
  pleito deixam de entrar no mesmo lote da pessoa. Recusada enquanto o volume
  não pedir.
- **Papéis no token.** É o caminho do ADR-0007 para a identidade, não para o
  papel: revogação teria de esperar o vencimento do token. Recusada lá e aqui.
- **Primeiro admin por linha de comando**, como o `flask conceder` do painel.
  O Jornada não tem CLI e roda em container; a configuração já vem por variável
  de ambiente. Recusada.

## Revisitar quando

- a identidade corporativa entrar (ADR-0007): `X-Pessoa` dá lugar ao claim do
  token, e o seletor de pessoa some do perfil;
- houver revogação ou edição de papel fora de pleito (gestão de acessos, 2b);
- uma empresa passar de dezenas de milhares de pessoas;
- alguma empresa precisar de matrícula ou unidade em outro formato.
