# Paridade funcional com o painel-ddd

O Jornada-DDD é a versão .NET multiempresa do `painel-ddd` (Flask). O objetivo
é que as duas ferramentas façam **as mesmas coisas**. A aparência pode ser
diferente.

## Critério

"Igual em funcionalidade" é medido assim: **todo comportamento que o painel
testa tem um teste equivalente passando aqui.** O painel tem 167 testes
automatizados. Os 14 de `tests/test_dialeto.py` testam o adaptador de SQL
Server do próprio painel e não se aplicam, então sobram **153 comportamentos**.

Cada linha abaixo é um teste do painel. "No Jornada" diz qual teste (ou qual
peça de código) cobre o comportamento. Os testes de front citam o nome do teste
original no título, para facilitar a busca.

| Situação | Significado |
|---|---|
| ✅ coberto | Há teste equivalente passando. |
| 🟡 parcial | A regra existe, mas falta a tela, o teste ou parte do comportamento. |
| ⬜ pendente | Ainda não existe no Jornada. |
| — não se aplica | Detalhe de implementação do painel, sem equivalente aqui. |

## O que fica diferente de propósito

- **Várias empresas.** Todo dado e toda consulta são por empresa (ADR-0003).
  O painel atende uma só.
- **Cosmos DB** como banco (ADR-0016), no lugar do SQLite/SQL Server do painel.
- **Identidade.** Enquanto não houver identidade corporativa (ADR-0007), a
  pessoa chega pelo cabeçalho `X-Pessoa`, escolhida em Seu perfil, como o
  seletor do painel. O papel é o **concedido** a ela (ADR-0018). A escolha
  livre de papel pelo `X-Papel` fica atrás de `Acesso:ModoDemonstracao`, ligado
  só no ambiente local.
- **Primeiro administrador.** O painel cria o primeiro admin pela linha de
  comando (`flask conceder`). No Jornada ele sai da configuração
  `Acesso:AdministradoresIniciais`: a matrícula listada que pleitear admin numa
  empresa sem admin é aprovada na hora (ADR-0018).
- **Escopo de concessão.** Como no painel, em `empresa` (o antigo `global`),
  `dominio` ou `squad`. O papel de squad vale nos ativos daquele squad; squad
  inativo não recebe ativo novo nem concessão nova, mas os vínculos que já
  existem continuam valendo.
- **Busca do catálogo.** O filtro roda no banco; a ordenação e a página, na
  aplicação, sobre o recorte inteiro, até 5.000 ativos (acima disso a resposta
  avisa `truncado`). Assim a ordem por criticidade, que não é alfabética, vale
  igual nos dois adaptadores. Não há ordenação por score: o painel guarda o
  score em `qualidade_catalogo`; o Jornada o calcula no pré-check. Os filtros
  guardados ficam no navegador, não na sessão do servidor.
- **Gestão de acessos.** No painel, salvar o cadastro troca todos os papéis por
  um papel na empresa inteira, e um papel de domínio some sem ninguém pedir. No
  Jornada a correção dos dados e os papéis são operações separadas: concede-se e
  revoga-se cada atribuição. A revogação vale na hora; no painel, o papel
  revogado ainda vale até a meia-noite.
- **Visual.** O tema segue o do painel (paleta, tipografia, menu lateral escuro,
  cartões e etiquetas, em `frontend/src/styles.css`). A disposição de cada tela
  acompanha a fase que a porta: até lá, ela pode diferir da do painel.
- **Descontinuar.** No painel, o serviço bloqueia diante de consumidor de
  alta criticidade a menos que receba `forcar`, e a tela pede o código antes
  de mandar `forcar`. No Jornada é um mecanismo só, na API: com consumidor
  crítico, a confirmação é o código do ativo. Assim um cliente da API não
  contorna a confirmação. O motivo é obrigatório.
- **Grafo do catálogo.** O painel desenha todos os ativos na visão
  executiva. No Jornada há um teto de 300 nós (até 1.000 pela API), e passar
  dele marca `truncado` e a tela avisa, a mesma regra da vizinhança. Arquivados
  ficam fora do grafo, da cobertura por domínio e das pendências prioritárias.
- **Relações de um ativo descontinuado.** O painel encerra cada relação
  (`fim_vigencia`). No Jornada a relação mora na partição de quem aponta
  (ADR-0016), e encerrá-las seria um lote por consumidor, sem atomicidade.
  Uma relação com um dos lados fora de vigência deixa de contar na análise de
  impacto e vira alerta no pré-check de quem ainda aponta para ele.

## Fases

| Fase | Entrega |
|---|---|
| 0 | Fundação: rotas no front, o menu do painel, tema com três estados, menu recolhível, busca no topo, carga de demonstração, CI rodando de verdade |
| 1 | Ciclo de vida: descontinuar com análise de impacto, arquivar, snapshot imutável da revisão com hash, comparação entre revisões, trilha de auditoria |
| 2 | Pessoas e acesso: cadastro com matrícula, papéis concedidos com escopo, modo leitura, seu perfil, pleitos e concessão, squads, cartilha derivada das permissões |
| 3 | Ficha e cadastro: ficha 360 em abas, checklist, assistente por bloco do papel, trilha clicável, relação com mecanismo, relações em lote |
| 4 | Validação e mesa: assumir e liberar, abas com contagem, painel do validador, minha mesa, políticas, SLA |
| 5 | Catálogo e busca: sugestões na busca, paginação, ordenação, filtros guardados com chips |
| 6 | Mapas e grafo: árvores de negócio e técnica, vizinhança, análise de consumidores |
| 7 | Visão executiva: KPIs que levam ao recorte, snapshots mensais, variação |
| 8 | Notificações: caixa, sino, preferências, vigia de SLA (tarefa de fundo periódica, por empresa) |
| 9 | Descobertas: importação de OpenAPI e inventário Git com prévia, bandeja e triagem |
| 10 | Migração dos dados do painel |

Cada fase começa depois que o PR da anterior é mergeado. Dentro de uma fase,
cada PR porta primeiro os testes do painel daquela fatia e depois implementa
domínio, caso de uso, API (com o contrato OpenAPI regenerado), os dois
adaptadores de persistência (memória e Cosmos, com a suíte de contrato) e a
tela. Ao fechar um PR, atualize a linha correspondente aqui.

## Situação

| Situação | Comportamentos |
|---|---|
| ✅ coberto | 152 |
| 🟡 parcial | 0 |
| ⬜ pendente | 0 |
| — não se aplica | 1 |
| **Total** | **153** |


### Por fase

| Fase | Entrega | ✅ | 🟡 | ⬜ |
|---|---|---|---|---|
| 0 | Fundação, mais o que o domínio já cobria | 29 | 0 | 0 |
| 1 | Ciclo de vida | 4 | 0 | 0 |
| 2 | Pessoas e acesso | 45 | 0 | 0 |
| 3 | Ficha e cadastro | 22 | 0 | 0 |
| 4 | Validação e mesa | 8 | 0 | 0 |
| 5 | Catálogo e busca | 12 | 0 | 0 |
| 6 | Mapas e grafo | 12 | 0 | 0 |
| 7 | Visão executiva | 3 | 0 | 0 |
| 8 | Notificações | 7 | 0 | 0 |
| 9 | Descobertas | 10 | 0 | 0 |

### Sem teste no painel

A matriz acima nasceu dos testes do painel, e o que o painel faz sem teste
próprio ficou fora dela. Uma revisão tela a tela, rota a rota e função a função
achou o que segue. A coluna "PR" diz em qual entrega cada item entra.

| Comportamento do painel | PR | Situação | No Jornada |
|---|---|---|---|
| Exposição do catálogo: grafo de todos os ativos, recorte por domínio e subdomínio, clique no nó recorta (`servicos.grafo_catalogo`) | A | ✅ | `GrafoDoCatalogoSemRecorte`, `GrafoRecortadoSoHierarquia`, `GrafoDoCatalogoSemArquivadoEComTeto` (`GET /v1/mapa/grafo`) e `telas/VisaoExecutiva.test.tsx` |
| Cobertura de capacidades por domínio (`cobertura_por_dominio`) | A | ✅ | `CoberturaPorDominioContaAsCapacidades` e `telas/VisaoExecutiva.test.tsx` |
| Pendências prioritárias, cada uma levando ao recorte (`pendencias_prioritarias`) | A | ✅ | `PendenciasPrioritariasContamEApontamORecorte` e `telas/VisaoExecutiva.test.tsx`; sem origem da evidência na relação (item abaixo), "dependência crítica sem evidência" conta todas as dependências críticas |
| As cinco primeiras da fila de validação na visão executiva | A | ✅ | `telas/VisaoExecutiva.test.tsx` (`FilaResumida`, de `GET /v1/validacoes/fila`) |
| Qualidade cadastral média, também na fotografia mensal (`qualidade.media_geral`) | A | ✅ | `QualidadeMediaEhAMediaDoPreCheck`: a mesma calculadora do pré-check, sobre o catálogo já carregado |
| Ficha: dimensões de qualidade | B | ⬜ | |
| Ficha: alertas do pré-check e o rito da política | B | ⬜ | |
| Ficha: motivo da revisão ao submeter | B | ⬜ | |
| Ficha: próxima revisão (`qualidade.proxima_revisao`) | B | ⬜ | |
| Ficha: filhos na aba Relações | B | ⬜ | |
| Ficha: o que o tipo exige de responsáveis, na aba Pessoas | B | ⬜ | |
| Relação: origem da evidência (manual ou automática) | B | ⬜ | |
| Validação: checklist de governança no painel do validador | B | ⬜ | |
| Políticas: matriz mínima de aprovação por tipo de mudança (`governanca.MATRIZ_MUDANCA`) | B | ⬜ | |
| Catálogo: colunas pai, squad e qualidade; busca por texto na própria tela | C | ⬜ | |
| Mapa: expandir e recolher tudo; atalhos de cadastro na árvore vazia | C | ⬜ | |
| Grafo do ativo: tabela "Ativos no grafo" | C | ⬜ | |
| Minha mesa: qualidade, atualização e squad | C | ⬜ | |
| Novo ativo: "O que vem depois" e "Exigências deste tipo" | C | ⬜ | |
| Solicitar acesso: "O que cada papel exerce" e "O que acontece depois" | C | ⬜ | |
| Bandeja de descobertas: pai e quem trouxe | C | ⬜ | |

A fase 1 também porta o que o painel faz sem teste próprio: o retrato de cada
revisão com SHA-256 (`servicos._snapshot`), a comparação entre revisões
(`comparar_revisoes`) e a trilha de auditoria (`auditar`), na aba Histórico da
ficha. No Jornada estão cobertos por `HistoricoTestes`, pelo contrato de
persistência e por `telas/Historico.test.tsx` (ADR-0017).

A fase 2 foi dividida em dois PRs. O primeiro (2a) traz o cadastro de pessoa,
o pleito e a concessão, os papéis concedidos no lugar do `X-Papel`, o escopo de
domínio nos casos de uso, o modo leitura e as telas Seu perfil, Solicitar acesso
e Pleitos de acesso (ADR-0018). O segundo (2b) traz a gestão de pessoas, os
squads e a cartilha com a matriz derivada das permissões (2b): a matriz, as
etapas por papel e a tabela de ritos saem de `GET /v1/cartilha`, que as lê das
regras aplicadas, e o `CARTILHA.md` guarda só a prosa. Os dois parciais que
restam na fase 2 são os avisos de pleito aberto e decidido, que entram com as
notificações, na fase 8. A gestão de acessos e os squads do painel não têm
teste próprio lá; no Jornada estão em `GestaoTestes`, no contrato de
persistência e em `telas/Gestao.test.tsx`.

A fase 3 também foi dividida. O 3a traz a ficha do ativo como página própria
(`/ativos/{id}`, cinco abas pela URL, trilha clicável, caminho até a
publicação), a relação com mecanismo, destinos sugeridos, recusa de ciclo
antes de gravar e relação repetida ignorada, e as relações em lote. O 3b traz o
assistente de cadastro lendo o metamodelo (`GET /v1/metamodelo`) com o rito do
tipo antecipado, o squad do ativo e o escopo de papel por squad.

A fase 4 porta a fila de validação (abas por etapa com contagem, a mais
crítica primeiro, prazo de cada etapa pelo SLA da política), assumir e liberar
uma etapa, o painel do validador com o que mudou desde a revisão anterior,
"decidir e seguir para a próxima", a Minha mesa e a página Políticas. Etapas
abertas antes desta fase não têm prazo gravado e aparecem com "—"; ativos
cadastrados antes dela não têm autor e não entram em "Meus rascunhos".

### Governança (regras de publicação) — `tests/test_governanca.py` (10)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_hierarquia_invalida_e_recusada` | 1 | ✅ | `HierarquiaInvalida` (DescontinuacaoTestes) |
| `test_nome_duplicado_no_mesmo_tipo` | 0 | ✅ | `NomeDuplicadoBloqueia` (tests/Jornada.Dominio.Testes) e o contrato de persistência |
| `test_precheck_bloqueia_sem_owner` | 0 | ✅ | `OwnerAusenteBloqueia` |
| `test_fluxo_completo_ate_publicacao` | 0 | ✅ | `DuasEtapasSoPublicaComAsDuas`, `RejeitadoEReenviadoPublica` |
| `test_publicado_e_imutavel` | 0 | ✅ | `PublicadoNaoEditaSemRevisao`, `PublicadoNaoRecebeEvidenciaNemResponsavel` |
| `test_rejeicao_devolve_para_rascunho` | 0 | ✅ | `EmValidacaoVoltaParaRascunho`, `RejeicaoCancelaEtapasIrmas` |
| `test_dependencia_circular_recusada` | 0 | ✅ | `CicloBloqueia` e o contrato de persistência |
| `test_descontinuacao_bloqueada_por_consumidor_critico` | 1 | ✅ | `ConsumidorCriticoBloqueia`; digitar o código é o "forçar" (ver diferenças) |
| `test_score_de_qualidade_sobe_com_cadastro_completo` | 0 | ✅ | `TudoPerfeitoDaCem`, `AtivoCompletoTemCompletudeCem` |
| `test_api_lista_itens` | 0 | ✅ | `GET /v1/ativos` (contrato OpenAPI) |

### Acesso, notificações, grafo e esqueleto — `tests/test_acesso.py` (29)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_papel_global_vale_em_qualquer_item` | 0 | ✅ | `CuradorCadastraEmQualquerBloco` (papel de empresa) |
| `test_papel_de_dominio_nao_vale_em_outro_dominio` | 2 | ✅ | `PapelDeDominioNaoValeEmOutroDominio` (AcessoTestes): o caso de uso sobe a hierarquia até o domínio (`AlvoDoAtivo`) |
| `test_etapa_negocial_exige_papel_de_negocio` | 0 | ✅ | `EtapaTecnicaExigeTechLead` (mesma tabela `PapelPorEtapa`) |
| `test_segregacao_de_funcao_quem_submete_nao_aprova` | 0 | ✅ | `QuemSubmeteuNaoDecide` |
| `test_servico_recusa_decisao_sem_papel` | 0 | ✅ | `EtapaTecnicaExigeTechLead`, `AtorSemPapelNaoFazNada` |
| `test_rota_de_escrita_recusa_mesmo_sem_passar_pela_tela` — O formulário enviado direto tem de ser recusado igual. | 0 | ✅ | Autorização no caso de uso, não na tela: `ConsultaNaoEscreve` |
| `test_sessao_sem_pessoa_e_mandada_ao_perfil` | 2 | ✅ | `SessaoSemPessoaEMandadaAoPerfil`: 401 `PESSOA_NAO_IDENTIFICADA`, e a tela vai para /perfil |
| `test_modo_leitura_esconde_os_formularios` | 2 | ✅ | Em modo leitura `blocosQueEscreve` vem vazio e nada que escreve aparece: `Layout.test.tsx` (modo no cabeçalho), `telas/Acesso.test.tsx` |
| `test_modo_leitura_tambem_recusa_na_rota` | 2 | ✅ | `ModoLeituraTambemRecusaNaRota`: 403 `MODO_LEITURA` (cabeçalho `X-Modo`) |
| `test_descontinuar_com_consumidor_critico_exige_o_codigo` | 1 | ✅ | `ConfirmacaoErradaNaoDescontinua` e `telas/Impacto.test.tsx` |
| `test_acessibilidade_do_esqueleto` | 0 | ✅ | `Layout.test.tsx` e `estilos.test.ts` (frontend/src) |
| `test_criticidade_media_e_baixa_tem_estilos_proprios` | 0 | ✅ | `estilos.test.ts` |
| `test_submissao_avisa_quem_pode_decidir` | 8 | ✅ | `SubmissaoAvisaQuemPodeDecidir`: a mesma regra de `PodeDecidir` aplicada a cada pessoa ativa |
| `test_notificacao_entra_na_mesma_transacao_e_fica_na_outbox` | 8 | ✅ | `NotificacaoEntraNaMesmaTransacaoEFicaNaOutbox` e o contrato `NotificacoesPersistem`: o aviso vai no lote da partição do fato (ativo, ou acesso); despacho em `POST /v1/notificacoes/despachos` |
| `test_preferencia_desligada_nao_gera_notificacao` | 8 | ✅ | `PreferenciaDesligadaNaoGeraNotificacao`; `GET/PUT /v1/notificacoes/preferencias` |
| `test_rejeicao_avisa_o_autor` | 8 | ✅ | `RejeicaoAvisaOAutor` |
| `test_vigia_de_sla_nao_repete_o_alerta_no_mesmo_dia` | 8 | ✅ | `VigiaDeSlaNaoRepeteOAlertaNoMesmoDia` e `VigiaAvisaQuemAssumiu`; roda na tarefa de fundo `TarefasDeNotificacao` (a cada 15 min, por empresa) e em `POST /v1/notificacoes/vigia-de-sla` |
| `test_sino_mostra_a_contagem_de_nao_lidas` | 8 | ✅ | `SinoMostraAContagemDeNaoLidas` e `telas/Notificacoes.test.tsx`: bolha do sino (`GET /v1/notificacoes/contagem`) e a caixa |
| `test_vizinhanca_de_um_salto_traz_so_os_vizinhos_diretos` | 6 | ✅ | `VizinhancaDeUmSalto` (`GET /v1/ativos/{id}/vizinhanca`) |
| `test_vizinhanca_de_dois_saltos_nao_repete_nos` | 6 | ✅ | `VizinhancaDeDoisSaltosNaoRepeteNos` |
| `test_teto_marca_truncado` | 6 | ✅ | `TetoMarcaTruncado` |
| `test_filtro_por_tipo_de_relacao_reduz_o_grafo` | 6 | ✅ | `FiltroPorTipoDeRelacaoReduzOGrafo` |
| `test_layout_poe_o_centro_no_meio` | 6 | ✅ | `telas/Mapas.test.tsx` (`grafo.ts`: o layout em anéis vai para o front, que desenha o SVG) |
| `test_tela_do_grafo_responde_e_avisa_o_corte` | 6 | ✅ | `telas/Mapas.test.tsx`: SVG, "Quem depende deste ativo" e o aviso de corte |
| `test_tema_noturno_tem_os_tres_estados` — Escolha explícita clara, escura e o padrão do sistema. | 0 | ✅ | `preferencias.test.ts` |
| `test_paleta_e_toda_por_token` — Nenhuma cor fixa fora da declaração dos tokens: senão um tema quebra o outro. | 0 | ✅ | `estilos.test.ts` |
| `test_menu_recolhivel_tem_controle_e_iniciais` | 0 | ✅ | `Layout.test.tsx`, `navegacao.test.ts` |
| `test_menu_compacto_esconde_rotulo_e_mostra_inicial` | 0 | ✅ | `estilos.test.ts` |
| `test_preferencia_aplicada_antes_da_pintura` — O script de tema vem antes do CSS: sem piscada de tela clara. | 0 | ✅ | `estilos.test.ts` (script no `index.html`) |

### Cadastro de pessoa, pleito e concessão — `tests/test_acesso_pleito.py` (33)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_matricula_fora_do_formato_e_recusada` | 2 | ✅ | `MatriculaForaDoFormatoERecusada` (AcessoTestes) |
| `test_matricula_valida_em_qualquer_letra_inicial` | 2 | ✅ | `MatriculaValidaEmQualquerLetraInicial` |
| `test_matricula_e_normalizada_para_maiuscula` | 2 | ✅ | `MatriculaENormalizadaParaMaiuscula` |
| `test_unidade_exige_quatro_numeros` | 2 | ✅ | `UnidadeExigeQuatroNumeros` |
| `test_nome_e_email_sao_conferidos` | 2 | ✅ | `NomeEEmailSaoConferidos` |
| `test_cadastro_nao_concede_papel_nenhum` — O ponto do rito: cadastrar-se não é ganhar acesso. | 2 | ✅ | `CadastroNaoConcedePapelNenhum` |
| `test_quem_se_cadastra_consulta_o_catalogo` — Sem papel, tudo é leitura — e leitura tem de funcionar. | 2 | ✅ | `QuemSeCadastraConsultaOCatalogo` |
| `test_pleito_nasce_pendente_com_os_dados_informados` | 2 | ✅ | `PleitoNascePendenteComOsDadosInformados` |
| `test_segundo_pleito_pendente_e_recusado` | 2 | ✅ | `SegundoPleitoPendenteERecusado` (409 `PLEITO_PENDENTE`) |
| `test_recadastro_atualiza_a_pessoa_sem_duplicar` | 2 | ✅ | `RecadastroAtualizaAPessoaSemDuplicar`; no Cosmos, a reserva da matrícula impede a duplicata (contrato de persistência) |
| `test_quem_concede_e_avisado_do_pleito` | 8 | ✅ | `QuemConcedeEAvisadoDoPleito`: `PodeConceder` por pessoa; admin só por admin; a resposta vai a quem pediu |
| `test_aprovacao_concede_o_papel_e_responde` | 2 | ✅ | `AprovacaoConcedeOPapelEResponde`: o papel vale, a resposta fica no pleito e no perfil, e o aviso vai à caixa (fase 8). |
| `test_negativa_exige_motivo_e_nao_concede_nada` | 2 | ✅ | `NegativaExigeMotivoENaoConcedeNada`, com o aviso de negativa na caixa (fase 8). |
| `test_papel_diferente_do_pleiteado_exige_explicacao` | 2 | ✅ | `PapelDiferenteDoPleiteadoExigeExplicacao` |
| `test_concessao_com_escopo_de_dominio_nao_vale_fora_dele` | 2 | ✅ | `ConcessaoComEscopoDeDominioNaoValeForaDele` |
| `test_pleito_ja_decidido_nao_se_decide_de_novo` | 2 | ✅ | `PleitoJaDecididoNaoSeDecideDeNovo` (409 `PLEITO_JA_DECIDIDO`) |
| `test_ninguem_concede_acesso_a_si_mesmo` — Segregação de função também no acesso, não só na validação. | 2 | ✅ | `NinguemConcedeAcessoASiMesmo` (403 `SEGREGACAO_DE_FUNCAO`) |
| `test_curador_concede_tudo_menos_admin` | 2 | ✅ | `CuradorConcedeTudoMenosAdmin` |
| `test_quem_nao_concede_nao_entra_na_fila` | 2 | ✅ | `QuemNaoConcedeNaoEntraNaFila` |
| `test_curador_que_tenta_conceder_admin_pela_rota_e_recusado` | 2 | ✅ | `CuradorQueTentaConcederAdminERecusado` (403) |
| `test_negocio_nao_cadastra_ativo_tecnico` — O papel autoriza a ação e também o objeto: era o furo antigo. | 2 | ✅ | `NegocioNaoCadastraAtivoTecnico` |
| `test_tech_lead_nao_redesenha_a_hierarquia_de_negocio` | 2 | ✅ | `TechLeadNaoMexeEmDominio` |
| `test_arquiteto_e_curador_atravessam_os_dois_blocos` | 2 | ✅ | `CuradorCadastraEmQualquerBloco`, `BlocosQueEscreve` |
| `test_edicao_de_ativo_existente_respeita_o_bloco` | 2 | ✅ | `EdicaoDeAtivoExistenteRespeitaOBloco` |
| `test_rota_de_cadastro_recusa_tipo_fora_do_papel` | 2 | ✅ | `CadastrarAtivo` recusa com SEM_PERMISSAO (403) |
| `test_wizard_so_oferece_os_blocos_do_papel` | 2 | ✅ | `telas/Acesso.test.tsx`: o Novo ativo agrupa os tipos por bloco e mostra só os do papel |
| `test_sem_papel_o_wizard_manda_pleitear` | 2 | ✅ | `telas/Acesso.test.tsx` |
| `test_tela_de_cadastro_abre_sem_identificacao` | 2 | ✅ | `telas/Acesso.test.tsx`; `POST /v1/pleitos` não exige `X-Pessoa` |
| `test_cadastro_pela_tela_identifica_a_pessoa_na_hora` | 2 | ✅ | O App passa a usar a pessoa devolvida pelo cadastro; `telas/Acesso.test.tsx` cobre o perfil com o pleito aguardando decisão |
| `test_fila_mostra_o_pleito_a_quem_concede` | 2 | ✅ | `telas/Acesso.test.tsx`, `QuemNaoConcedeNaoEntraNaFila` |
| `test_fila_nao_oferece_admin_a_curador` | 2 | ✅ | `telas/Acesso.test.tsx` (a API devolve `podeConcederAdmin`) |
| `test_decisao_pela_tela_concede_e_aparece_no_perfil` | 2 | ✅ | `DecisaoApareceNoPerfil` e `telas/Acesso.test.tsx` |
| `test_menu_so_mostra_a_fila_para_quem_concede` | 2 | ✅ | `navegacao.test.ts` e `Layout.test.tsx`: o menu segue as alçadas que a API devolve em `GET /v1/perfil` |

### Fluxo de trabalho — `tests/test_fluxo.py` (22)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_visao_360_abre_no_resumo_e_esconde_as_outras_abas` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx`: as cinco abas no HTML, só a escolhida visível |
| `test_aba_escolhida_e_a_que_abre` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` (`?aba=`) |
| `test_aba_desconhecida_cai_no_resumo` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` (`abaDaUrl`) |
| `test_escrita_volta_para_a_aba_de_origem` | 3 | ✅ | A aba mora na URL e a escrita não sai dela; `telas/PaginaDoAtivo.test.tsx` (`caminhoDaAba`) |
| `test_checklist_reflete_o_que_falta` | 3 | ✅ | `ChecklistRefleteOQueFalta` e `telas/PaginaDoAtivo.test.tsx`: os cinco passos, com a pendência e a aba que a resolve |
| `test_checklist_fica_completo_quando_o_precheck_aprova` | 3 | ✅ | `ChecklistFicaCompletoQuandoOPreCheckAprova` |
| `test_todo_bloqueio_do_precheck_cai_em_algum_passo` — O checklist não pode esconder um impedimento que a submissão vai levantar. | 0 | ✅ | `ChecklistDerivaDoPreCheck` |
| `test_checklist_aparece_no_rascunho_e_some_no_publicado` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` |
| `test_wizard_antecipa_o_rito_do_tipo` | 3 | ✅ | `telas/NovoAtivo.test.tsx`: o rito vem de `GET /v1/ritos?tipo=&criticidade=` e aparece ao lado do formulário |
| `test_painel_do_validador_mostra_evidencias_e_responsaveis` | 4 | ✅ | `PainelMostraEvidenciasEResponsaveis` e `telas/Validacoes.test.tsx` (`GET /v1/validacoes/{id}/painel`) |
| `test_painel_mostra_o_que_mudou_na_segunda_revisao` | 4 | ✅ | `PainelMostraOQueMudouNaSegundaRevisao` e `telas/Validacoes.test.tsx`: a comparação com a revisão anterior, pelo retrato (ADR-0017) |
| `test_decidir_preserva_a_etapa_e_segue_para_a_proxima` | 4 | ✅ | `telas/Validacoes.test.tsx` (`destinoDepoisDeDecidir`) |
| `test_decidir_sem_seguir_nao_seleciona_ninguem` | 4 | ✅ | `telas/Validacoes.test.tsx` |
| `test_abas_da_fila_mostram_a_contagem` | 4 | ✅ | `FilaOrdenaEConta` e `telas/Validacoes.test.tsx` |
| `test_assumir_e_liberar_uma_validacao` | 4 | ✅ | `AssumirELiberarUmaValidacao` e `telas/Validacoes.test.tsx`; quem não decide não assume (`QuemNaoDecideNaoAssume`) |
| `test_minha_mesa_separa_o_que_e_meu` | 4 | ✅ | `MinhaMesaSeparaOQueEMeu` e `telas/Validacoes.test.tsx` (`GET /v1/mesa`) |
| `test_tela_da_mesa_responde_e_entra_no_menu` | 4 | ✅ | `telas/Validacoes.test.tsx` |
| `test_migracao_cria_a_coluna_em_banco_antigo` — Banco criado antes da onda 2 recebe atribuido_a sem perder dados. |  | — | Migração de coluna do SQLite; o Cosmos não tem esquema. |
| `test_formulario_de_relacao_oferece_mecanismo` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx`: mecanismo e busca de destino (`destino-busca`) |
| `test_mecanismo_chega_ao_banco` | 3 | ✅ | `MecanismoChegaAoBanco` e o contrato de persistência |
| `test_relacao_sem_destino_avisa_em_vez_de_quebrar` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` (`problemaDaRelacao`); a API devolve 404 `DESTINO_NAO_ENCONTRADO` (`TipoEMecanismoSaoConferidos`) |
| `test_destinos_sugeridos_cobrem_todas_as_relacoes` — A lista que filtra o combobox não pode deixar uma relação sem sugestão. | 3 | ✅ | `DestinosSugeridosCobremTodasAsRelacoes` e `telas/PaginaDoAtivo.test.tsx` |

### Navegação e filtros — `tests/test_jornada.py` (17)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_trilha_sobe_da_raiz_ate_o_pai_direto` | 3 | ✅ | `TrilhaSobeDaRaizAteOPaiDireto` |
| `test_trilha_de_item_raiz_e_vazia` | 3 | ✅ | `TrilhaDeItemRaizEVazia` |
| `test_tela_do_ativo_mostra_a_trilha_clicavel` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` |
| `test_menu_continua_marcado_dentro_de_um_ativo` | 3 | ✅ | `navegacao.test.ts` e `telas/PaginaDoAtivo.test.tsx`: `/ativos/{id}` é a página do ativo |
| `test_menu_marca_o_cadastro_no_wizard` | 0 | ✅ | `navegacao.test.ts` |
| `test_busca_global_sugere_ativos_por_termo` | 5 | ✅ | `BuscaGlobalSugereAtivosPorTermo` (`GET /v1/busca/sugestoes`) e `telas/Busca.test.tsx`: cada sugestão leva a `/ativos/{id}` |
| `test_busca_global_ignora_termo_curto` | 5 | ✅ | `BuscaGlobalIgnoraTermoCurto` e `telas/Busca.test.tsx` |
| `test_campo_de_busca_existe_em_todas_as_telas` | 0 | ✅ | `Layout.test.tsx` |
| `test_filtros_voltam_na_visita_seguinte` | 5 | ✅ | `telas/Busca.test.tsx`: o recorte fica guardado no navegador (no painel, na sessão do servidor) |
| `test_chip_remove_apenas_o_proprio_filtro` | 5 | ✅ | `telas/Busca.test.tsx` |
| `test_limpar_zera_os_filtros_guardados` | 5 | ✅ | `telas/Busca.test.tsx` |
| `test_filtro_sem_owner_isola_os_ativos_sem_responsavel` | 5 | ✅ | `FiltroSemOwnerIsolaOsAtivosSemResponsavel` e o contrato de persistência (responsável encerrado não conta) |
| `test_kpis_do_painel_levam_ao_recorte` | 7 | ✅ | `telas/VisaoExecutiva.test.tsx`: cada KPI leva ao recorte do catálogo (`GET /v1/indicadores`) |
| `test_queda_de_pendencia_e_lida_como_melhora` — Cair é bom em 'sem responsável': a cor tem de acompanhar o sentido. | 7 | ✅ | `telas/VisaoExecutiva.test.tsx`: `quantoMenosMelhor` vem da API e a cor acompanha o sentido |
| `test_variacao_compara_as_duas_ultimas_competencias` | 7 | ✅ | `VariacaoComparaAsDuasUltimasCompetencias`; fotografia em `POST /v1/indicadores/fotografias` (só admin), persistida na partição `[empresa,"indicadores"]` |
| `test_wizard_devolve_o_que_foi_digitado_apos_erro` | 3 | ✅ | `telas/NovoAtivo.test.tsx`: o formulário é controlado e a recusa não o limpa (campos do tipo, criticidade, squad e descrição) |
| `test_wizard_preserva_o_pai_escolhido_apos_erro` | 3 | ✅ | `telas/NovoAtivo.test.tsx` |

### Escala: descoberta, paginação, mapas — `tests/test_escala.py` (27)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_previa_do_openapi_nao_escreve_nada` | 9 | ✅ | `PreviaDoOpenApiNaoEscreveNada` (`POST /v1/descobertas/previas`) |
| `test_previa_marca_o_que_ja_existe` | 9 | ✅ | `PreviaMarcaOQueJaExiste`; a busca por tipo e nome é a porta `ObterPorNomeAsync`, com contrato nos dois adaptadores |
| `test_importacao_marca_a_origem_e_cai_na_bandeja` | 9 | ✅ | `ImportacaoMarcaAOrigemECaiNaBandeja` (`POST /v1/descobertas/importacoes`): a API sob a aplicação, os endpoints sob a API |
| `test_inventario_git_tambem_entra_na_bandeja` | 9 | ✅ | `InventarioGitTambemEntraNaBandeja` |
| `test_tela_de_descobertas_mostra_previa_antes_de_gravar` | 9 | ✅ | `telas/Descobertas.test.tsx`: a prévia diz "nada foi gravado" e quantos a criar |
| `test_tela_de_descobertas_importa_ao_confirmar` | 9 | ✅ | `telas/Descobertas.test.tsx`: o botão de importar só aparece depois da prévia |
| `test_json_invalido_avisa_em_vez_de_quebrar` | 9 | ✅ | `JsonInvalidoAvisaEmVezDeQuebrar` (400 `CONTEUDO_INVALIDO`) e `telas/Descobertas.test.tsx` |
| `test_triagem_aceita_e_descarta` | 9 | ✅ | `TriagemAceitaEDescarta` (`POST /v1/descobertas/triagem`): aceitar vira cadastro humano sem mexer no ciclo de vida; descartar arquiva |
| `test_triagem_nao_toca_em_cadastro_humano` | 9 | ✅ | `TriagemNaoTocaEmCadastroHumano` |
| `test_triagem_recusa_acao_desconhecida` | 9 | ✅ | `TriagemRecusaAcaoDesconhecida` (400 `ACAO_DE_TRIAGEM_INVALIDA`); quem não importa não tria |
| `test_lote_cria_varias_e_ignora_as_existentes` | 3 | ✅ | `LoteCriaVariasEIgnoraAsExistentes` |
| `test_lote_ignora_o_proprio_ativo` | 3 | ✅ | `LoteIgnoraOProprioAtivo` |
| `test_tela_de_lote_marca_o_que_ja_existe` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` |
| `test_post_do_lote_volta_para_a_aba_de_relacoes` | 3 | ✅ | `telas/PaginaDoAtivo.test.tsx` |
| `test_pagina_traz_o_total_real_e_nao_o_tamanho_da_pagina` | 5 | ✅ | `PaginaTrazOTotalReal` (`GET /v1/catalogo`) |
| `test_pagina_fora_do_intervalo_volta_para_a_ultima` | 5 | ✅ | `PaginaForaDoIntervaloVoltaParaAUltima` |
| `test_ordenacao_por_score_e_por_nome` | 5 | ✅ | `OrdenacaoPorNomeEPorCriticidade`: nome, inversa e criticidade, como o teste do painel confere; ordenar por score não existe (ver diferenças) |
| `test_ordenacao_desconhecida_cai_no_padrao` | 5 | ✅ | `OrdenacaoDesconhecidaCaiNoPadrao` |
| `test_catalogo_mostra_total_e_paginacao` | 5 | ✅ | `telas/Busca.test.tsx` |
| `test_filtro_de_origem_separa_o_que_a_maquina_trouxe` | 5 | ✅ | `FiltroDeOrigemSeparaOQueAMaquinaTrouxe`, o contrato de persistência e `telas/Busca.test.tsx` |
| `test_arvore_de_negocio_desce_quatro_niveis` | 6 | ✅ | `ArvoreDeNegocioDesceQuatroNiveis` (`GET /v1/mapa/arvore`) e `telas/Mapas.test.tsx` |
| `test_arvore_tecnica_usa_a_mesma_mecanica` | 6 | ✅ | `ArvoreTecnicaUsaAMesmaMecanica` |
| `test_arvore_conta_implementacoes_da_capacidade` | 6 | ✅ | `ArvoreContaImplementacoesDaCapacidade` e `telas/Mapas.test.tsx` |
| `test_mapa_tem_as_duas_visoes` | 6 | ✅ | `telas/Mapas.test.tsx`: abas Negócio e Técnica e a busca na árvore (`filtro-arvore`) |
| `test_analise_agrupa_os_consumidores` | 6 | ✅ | `AnaliseAgrupaOsConsumidores` (`AnalisarImpacto`, desde a fase 1) |
| `test_tela_mostra_quem_depende_antes_de_descontinuar` | 1 | ✅ | `telas/Impacto.test.tsx` (aba Relações da ficha) |
| `test_ativo_sem_consumidor_diz_isso_claramente` | 6 | ✅ | `telas/Mapas.test.tsx` (`QuemDepende`) |

### Cartilha — `tests/test_cartilha.py` (15)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_cartilha_abre_sem_identificacao` — Quem ainda não se identificou é justamente quem mais precisa do guia. | 2 | ✅ | `telas/Cartilha.test.tsx`; `GET /v1/cartilha` não exige `X-Pessoa` |
| `test_cartilha_nao_escreve_nada` — Página de leitura: nenhum formulário de escrita, para nenhum papel. | 0 | ✅ | `Layout.test.tsx` |
| `test_cada_perfil_tem_secao_e_atalho` | 2 | ✅ | `telas/Cartilha.test.tsx`: seção com âncora, cartão de atalho e lema por perfil |
| `test_matriz_reflete_as_permissoes_reais` — Cada célula da matriz é lida de acesso.PERMISSOES, ação por ação. | 2 | ✅ | `MatrizRefleteAsPermissoesReais` (cada célula é o `Pode()` da política) e `telas/Cartilha.test.tsx` |
| `test_matriz_acompanha_mudanca_de_permissao` — Mudou a regra no código, muda a página — sem ninguém editar a cartilha. | 2 | ✅ | A matriz é lida de `PoliticaDeAutorizacao.PapeisQuePodem` a cada chamada; `telas/Cartilha.test.tsx` muda os dados e a página muda |
| `test_curador_nao_aparece_decidindo_validacao` — A segregação de função precisa estar visível, não só aplicada. | 2 | ✅ | `CuradorNaoApareceDecidindoValidacao` |
| `test_consulta_nao_pode_nada_na_matriz` | 2 | ✅ | `ConsultaNaoPodeNadaNaMatriz` |
| `test_etapas_por_papel_sai_do_acesso` | 2 | ✅ | `EtapasPorPapelSaiDoAcesso` |
| `test_ritos_vem_da_tabela_de_politicas` | 2 | ✅ | `RitosVemDaTabelaDePoliticas` e `telas/Cartilha.test.tsx` (score e prazo de cada rito) |
| `test_rito_critico_e_mais_apertado_que_o_padrao` — O que a cartilha afirma sobre criticidade precisa ser verdade no banco. | 0 | ✅ | `ApiCriticaEhMaisExigente` |
| `test_rodape_leva_a_cartilha_em_qualquer_tela` — O rodapé é do site, não da tela: precisa estar em todas. | 0 | ✅ | `Layout.test.tsx` |
| `test_atalho_fica_ao_lado_do_botao_de_identificacao` — Ao lado de "quem sou eu" fica "o que eu posso fazer". | 0 | ✅ | `Layout.test.tsx` |
| `test_atalho_se_marca_quando_voce_esta_na_cartilha` | 0 | ✅ | `Layout.test.tsx` |
| `test_cores_de_perfil_existem_nos_tres_estados_de_tema` — Se a cor só existe no claro, a cartilha fica ilegível de noite. | 2 | ✅ | `estilos.test.ts`: `--perfil-*` e `--perfil-*-bg` nos três estados |
| `test_rodape_e_atalho_tem_estilo_proprio` | 0 | ✅ | `estilos.test.ts` |
