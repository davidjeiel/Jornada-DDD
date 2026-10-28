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
  pessoa e o papel chegam por cabeçalho. Na fase 2 o papel passa a ser
  **concedido** como no painel, e a escolha livre de papel fica atrás de
  `ModoDemonstracao`, ligado só no ambiente local.
- **Visual.** O tema segue o do painel (paleta, tipografia, menu lateral escuro,
  cartões e etiquetas, em `frontend/src/styles.css`). A disposição de cada tela
  acompanha a fase que a porta: até lá, ela pode diferir da do painel.
- **Descontinuar.** No painel, o serviço bloqueia diante de consumidor de
  alta criticidade a menos que receba `forcar`, e a tela pede o código antes
  de mandar `forcar`. No Jornada é um mecanismo só, na API: com consumidor
  crítico, a confirmação é o código do ativo. Assim um cliente da API não
  contorna a confirmação. O motivo é obrigatório.
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
| 8 | Notificações: caixa, sino, preferências, vigia de SLA (processo de fundo lendo o change feed) |
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
| ✅ coberto | 37 |
| 🟡 parcial | 17 |
| ⬜ pendente | 98 |
| — não se aplica | 1 |
| **Total** | **153** |


### Por fase

| Fase | Entrega | ✅ | 🟡 | ⬜ |
|---|---|---|---|---|
| 0 | Fundação, mais o que o domínio já cobria | 29 | 0 | 0 |
| 1 | Ciclo de vida | 4 | 0 | 0 |
| 2 | Pessoas e acesso | 4 | 7 | 34 |
| 3 | Ficha e cadastro | 0 | 8 | 14 |
| 4 | Validação e mesa | 0 | 0 | 9 |
| 5 | Catálogo e busca | 0 | 1 | 11 |
| 6 | Mapas e grafo | 0 | 0 | 12 |
| 7 | Visão executiva | 0 | 0 | 3 |
| 8 | Notificações | 0 | 1 | 5 |
| 9 | Descobertas | 0 | 0 | 10 |

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
| `test_papel_de_dominio_nao_vale_em_outro_dominio` | 2 | 🟡 | `EscopoDeDominioLimita` cobre a política; os casos de uso ainda não resolvem o domínio do ativo. |
| `test_etapa_negocial_exige_papel_de_negocio` | 0 | ✅ | `EtapaTecnicaExigeTechLead` (mesma tabela `PapelPorEtapa`) |
| `test_segregacao_de_funcao_quem_submete_nao_aprova` | 0 | ✅ | `QuemSubmeteuNaoDecide` |
| `test_servico_recusa_decisao_sem_papel` | 0 | ✅ | `EtapaTecnicaExigeTechLead`, `AtorSemPapelNaoFazNada` |
| `test_rota_de_escrita_recusa_mesmo_sem_passar_pela_tela` — O formulário enviado direto tem de ser recusado igual. | 0 | ✅ | Autorização no caso de uso, não na tela: `ConsultaNaoEscreve` |
| `test_sessao_sem_pessoa_e_mandada_ao_perfil` | 2 | ⬜ |  |
| `test_modo_leitura_esconde_os_formularios` | 2 | ⬜ |  |
| `test_modo_leitura_tambem_recusa_na_rota` | 2 | ⬜ |  |
| `test_descontinuar_com_consumidor_critico_exige_o_codigo` | 1 | ✅ | `ConfirmacaoErradaNaoDescontinua` e `telas/Impacto.test.tsx` |
| `test_acessibilidade_do_esqueleto` | 0 | ✅ | `Layout.test.tsx` e `estilos.test.ts` (frontend/src) |
| `test_criticidade_media_e_baixa_tem_estilos_proprios` | 0 | ✅ | `estilos.test.ts` |
| `test_submissao_avisa_quem_pode_decidir` | 8 | ⬜ |  |
| `test_notificacao_entra_na_mesma_transacao_e_fica_na_outbox` | 8 | ⬜ | A outbox de eventos já existe (lote do Cosmos); falta a notificação. |
| `test_preferencia_desligada_nao_gera_notificacao` | 8 | ⬜ |  |
| `test_rejeicao_avisa_o_autor` | 8 | ⬜ |  |
| `test_vigia_de_sla_nao_repete_o_alerta_no_mesmo_dia` | 8 | ⬜ |  |
| `test_sino_mostra_a_contagem_de_nao_lidas` | 8 | 🟡 | O sino está no topo de toda tela; a contagem chega com as notificações. |
| `test_vizinhanca_de_um_salto_traz_so_os_vizinhos_diretos` | 6 | ⬜ |  |
| `test_vizinhanca_de_dois_saltos_nao_repete_nos` | 6 | ⬜ |  |
| `test_teto_marca_truncado` | 6 | ⬜ |  |
| `test_filtro_por_tipo_de_relacao_reduz_o_grafo` | 6 | ⬜ |  |
| `test_layout_poe_o_centro_no_meio` | 6 | ⬜ |  |
| `test_tela_do_grafo_responde_e_avisa_o_corte` | 6 | ⬜ |  |
| `test_tema_noturno_tem_os_tres_estados` — Escolha explícita clara, escura e o padrão do sistema. | 0 | ✅ | `preferencias.test.ts` |
| `test_paleta_e_toda_por_token` — Nenhuma cor fixa fora da declaração dos tokens: senão um tema quebra o outro. | 0 | ✅ | `estilos.test.ts` |
| `test_menu_recolhivel_tem_controle_e_iniciais` | 0 | ✅ | `Layout.test.tsx`, `navegacao.test.ts` |
| `test_menu_compacto_esconde_rotulo_e_mostra_inicial` | 0 | ✅ | `estilos.test.ts` |
| `test_preferencia_aplicada_antes_da_pintura` — O script de tema vem antes do CSS: sem piscada de tela clara. | 0 | ✅ | `estilos.test.ts` (script no `index.html`) |

### Cadastro de pessoa, pleito e concessão — `tests/test_acesso_pleito.py` (33)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_matricula_fora_do_formato_e_recusada` | 2 | 🟡 | `Matricula.De` existe no domínio; falta o cadastro de pessoa |
| `test_matricula_valida_em_qualquer_letra_inicial` | 2 | 🟡 | Idem |
| `test_matricula_e_normalizada_para_maiuscula` | 2 | 🟡 | Idem |
| `test_unidade_exige_quatro_numeros` | 2 | ⬜ |  |
| `test_nome_e_email_sao_conferidos` | 2 | ⬜ |  |
| `test_cadastro_nao_concede_papel_nenhum` — O ponto do rito: cadastrar-se não é ganhar acesso. | 2 | ⬜ |  |
| `test_quem_se_cadastra_consulta_o_catalogo` — Sem papel, tudo é leitura — e leitura tem de funcionar. | 2 | ⬜ |  |
| `test_pleito_nasce_pendente_com_os_dados_informados` | 2 | ⬜ |  |
| `test_segundo_pleito_pendente_e_recusado` | 2 | ⬜ |  |
| `test_recadastro_atualiza_a_pessoa_sem_duplicar` | 2 | ⬜ |  |
| `test_quem_concede_e_avisado_do_pleito` | 2 | ⬜ |  |
| `test_aprovacao_concede_o_papel_e_responde` | 2 | ⬜ |  |
| `test_negativa_exige_motivo_e_nao_concede_nada` | 2 | ⬜ |  |
| `test_papel_diferente_do_pleiteado_exige_explicacao` | 2 | ⬜ |  |
| `test_concessao_com_escopo_de_dominio_nao_vale_fora_dele` | 2 | ⬜ |  |
| `test_pleito_ja_decidido_nao_se_decide_de_novo` | 2 | ⬜ |  |
| `test_ninguem_concede_acesso_a_si_mesmo` — Segregação de função também no acesso, não só na validação. | 2 | ⬜ |  |
| `test_curador_concede_tudo_menos_admin` | 2 | ⬜ |  |
| `test_quem_nao_concede_nao_entra_na_fila` | 2 | ⬜ |  |
| `test_curador_que_tenta_conceder_admin_pela_rota_e_recusado` | 2 | ⬜ |  |
| `test_negocio_nao_cadastra_ativo_tecnico` — O papel autoriza a ação e também o objeto: era o furo antigo. | 2 | ✅ | `NegocioNaoCadastraAtivoTecnico` |
| `test_tech_lead_nao_redesenha_a_hierarquia_de_negocio` | 2 | ✅ | `TechLeadNaoMexeEmDominio` |
| `test_arquiteto_e_curador_atravessam_os_dois_blocos` | 2 | ✅ | `CuradorCadastraEmQualquerBloco`, `BlocosQueEscreve` |
| `test_edicao_de_ativo_existente_respeita_o_bloco` | 2 | 🟡 | `EditarAtivo` usa o bloco do tipo; falta teste de caso de uso |
| `test_rota_de_cadastro_recusa_tipo_fora_do_papel` | 2 | ✅ | `CadastrarAtivo` recusa com SEM_PERMISSAO (403) |
| `test_wizard_so_oferece_os_blocos_do_papel` | 2 | ⬜ |  |
| `test_sem_papel_o_wizard_manda_pleitear` | 2 | ⬜ |  |
| `test_tela_de_cadastro_abre_sem_identificacao` | 2 | ⬜ |  |
| `test_cadastro_pela_tela_identifica_a_pessoa_na_hora` | 2 | ⬜ |  |
| `test_fila_mostra_o_pleito_a_quem_concede` | 2 | ⬜ |  |
| `test_fila_nao_oferece_admin_a_curador` | 2 | ⬜ |  |
| `test_decisao_pela_tela_concede_e_aparece_no_perfil` | 2 | ⬜ |  |
| `test_menu_so_mostra_a_fila_para_quem_concede` | 2 | 🟡 | `navegacao.test.ts` e `Layout.test.tsx`; hoje o menu olha o papel do usuário de teste, não o papel concedido. |

### Fluxo de trabalho — `tests/test_fluxo.py` (22)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_visao_360_abre_no_resumo_e_esconde_as_outras_abas` | 3 | ⬜ |  |
| `test_aba_escolhida_e_a_que_abre` | 3 | ⬜ |  |
| `test_aba_desconhecida_cai_no_resumo` | 3 | ⬜ |  |
| `test_escrita_volta_para_a_aba_de_origem` | 3 | ⬜ |  |
| `test_checklist_reflete_o_que_falta` | 3 | 🟡 | `ChecklistDerivaDoPreCheck`; a tela mostra só três passos. |
| `test_checklist_fica_completo_quando_o_precheck_aprova` | 3 | 🟡 | Idem. |
| `test_todo_bloqueio_do_precheck_cai_em_algum_passo` — O checklist não pode esconder um impedimento que a submissão vai levantar. | 0 | ✅ | `ChecklistDerivaDoPreCheck` |
| `test_checklist_aparece_no_rascunho_e_some_no_publicado` | 3 | ⬜ |  |
| `test_wizard_antecipa_o_rito_do_tipo` | 3 | ⬜ |  |
| `test_painel_do_validador_mostra_evidencias_e_responsaveis` | 4 | ⬜ |  |
| `test_painel_mostra_o_que_mudou_na_segunda_revisao` | 4 | ⬜ |  |
| `test_decidir_preserva_a_etapa_e_segue_para_a_proxima` | 4 | ⬜ |  |
| `test_decidir_sem_seguir_nao_seleciona_ninguem` | 4 | ⬜ |  |
| `test_abas_da_fila_mostram_a_contagem` | 4 | ⬜ |  |
| `test_assumir_e_liberar_uma_validacao` | 4 | ⬜ |  |
| `test_minha_mesa_separa_o_que_e_meu` | 4 | ⬜ |  |
| `test_tela_da_mesa_responde_e_entra_no_menu` | 4 | ⬜ |  |
| `test_migracao_cria_a_coluna_em_banco_antigo` — Banco criado antes da onda 2 recebe atribuido_a sem perder dados. |  | — | Migração de coluna do SQLite; o Cosmos não tem esquema. |
| `test_formulario_de_relacao_oferece_mecanismo` | 3 | ⬜ |  |
| `test_mecanismo_chega_ao_banco` | 3 | ⬜ |  |
| `test_relacao_sem_destino_avisa_em_vez_de_quebrar` | 3 | 🟡 | `RelacionarAtivos` recusa destino inexistente; falta teste. |
| `test_destinos_sugeridos_cobrem_todas_as_relacoes` — A lista que filtra o combobox não pode deixar uma relação sem sugestão. | 3 | ⬜ |  |

### Navegação e filtros — `tests/test_jornada.py` (17)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_trilha_sobe_da_raiz_ate_o_pai_direto` | 3 | 🟡 | `TrilhaAsync` existe e vai na ficha; falta teste dedicado. |
| `test_trilha_de_item_raiz_e_vazia` | 3 | 🟡 | Idem. |
| `test_tela_do_ativo_mostra_a_trilha_clicavel` | 3 | ⬜ |  |
| `test_menu_continua_marcado_dentro_de_um_ativo` | 3 | 🟡 | `secaoDoCaminho` já marca o catálogo em `/ativos/...`; a página do ativo chega na fase 3. |
| `test_menu_marca_o_cadastro_no_wizard` | 0 | ✅ | `navegacao.test.ts` |
| `test_busca_global_sugere_ativos_por_termo` | 5 | ⬜ | A busca do topo já leva ao catálogo filtrado; as sugestões chegam na fase 5. |
| `test_busca_global_ignora_termo_curto` | 5 | ⬜ |  |
| `test_campo_de_busca_existe_em_todas_as_telas` | 0 | ✅ | `Layout.test.tsx` |
| `test_filtros_voltam_na_visita_seguinte` | 5 | ⬜ |  |
| `test_chip_remove_apenas_o_proprio_filtro` | 5 | 🟡 | Chip da busca existe; falta para os demais filtros. |
| `test_limpar_zera_os_filtros_guardados` | 5 | ⬜ |  |
| `test_filtro_sem_owner_isola_os_ativos_sem_responsavel` | 5 | ⬜ |  |
| `test_kpis_do_painel_levam_ao_recorte` | 7 | ⬜ |  |
| `test_queda_de_pendencia_e_lida_como_melhora` — Cair é bom em 'sem responsável': a cor tem de acompanhar o sentido. | 7 | ⬜ |  |
| `test_variacao_compara_as_duas_ultimas_competencias` | 7 | ⬜ |  |
| `test_wizard_devolve_o_que_foi_digitado_apos_erro` | 3 | 🟡 | O formulário de novo ativo mantém o que foi digitado após erro; o assistente chega na fase 3. |
| `test_wizard_preserva_o_pai_escolhido_apos_erro` | 3 | 🟡 | Idem. |

### Escala: descoberta, paginação, mapas — `tests/test_escala.py` (27)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_previa_do_openapi_nao_escreve_nada` | 9 | ⬜ |  |
| `test_previa_marca_o_que_ja_existe` | 9 | ⬜ |  |
| `test_importacao_marca_a_origem_e_cai_na_bandeja` | 9 | ⬜ |  |
| `test_inventario_git_tambem_entra_na_bandeja` | 9 | ⬜ |  |
| `test_tela_de_descobertas_mostra_previa_antes_de_gravar` | 9 | ⬜ |  |
| `test_tela_de_descobertas_importa_ao_confirmar` | 9 | ⬜ |  |
| `test_json_invalido_avisa_em_vez_de_quebrar` | 9 | ⬜ |  |
| `test_triagem_aceita_e_descarta` | 9 | ⬜ |  |
| `test_triagem_nao_toca_em_cadastro_humano` | 9 | ⬜ |  |
| `test_triagem_recusa_acao_desconhecida` | 9 | ⬜ |  |
| `test_lote_cria_varias_e_ignora_as_existentes` | 3 | ⬜ |  |
| `test_lote_ignora_o_proprio_ativo` | 3 | ⬜ |  |
| `test_tela_de_lote_marca_o_que_ja_existe` | 3 | ⬜ |  |
| `test_post_do_lote_volta_para_a_aba_de_relacoes` | 3 | ⬜ |  |
| `test_pagina_traz_o_total_real_e_nao_o_tamanho_da_pagina` | 5 | ⬜ |  |
| `test_pagina_fora_do_intervalo_volta_para_a_ultima` | 5 | ⬜ |  |
| `test_ordenacao_por_score_e_por_nome` | 5 | ⬜ |  |
| `test_ordenacao_desconhecida_cai_no_padrao` | 5 | ⬜ |  |
| `test_catalogo_mostra_total_e_paginacao` | 5 | ⬜ |  |
| `test_filtro_de_origem_separa_o_que_a_maquina_trouxe` | 5 | ⬜ |  |
| `test_arvore_de_negocio_desce_quatro_niveis` | 6 | ⬜ |  |
| `test_arvore_tecnica_usa_a_mesma_mecanica` | 6 | ⬜ |  |
| `test_arvore_conta_implementacoes_da_capacidade` | 6 | ⬜ |  |
| `test_mapa_tem_as_duas_visoes` | 6 | ⬜ |  |
| `test_analise_agrupa_os_consumidores` | 6 | ⬜ |  |
| `test_tela_mostra_quem_depende_antes_de_descontinuar` | 1 | ✅ | `telas/Impacto.test.tsx` (aba Relações da ficha) |
| `test_ativo_sem_consumidor_diz_isso_claramente` | 6 | ⬜ |  |

### Cartilha — `tests/test_cartilha.py` (15)

| Teste do painel | Fase | Situação | No Jornada |
|---|---|---|---|
| `test_cartilha_abre_sem_identificacao` — Quem ainda não se identificou é justamente quem mais precisa do guia. | 2 | ⬜ | Depende da identificação (fase 2). |
| `test_cartilha_nao_escreve_nada` — Página de leitura: nenhum formulário de escrita, para nenhum papel. | 0 | ✅ | `Layout.test.tsx` |
| `test_cada_perfil_tem_secao_e_atalho` | 2 | ⬜ |  |
| `test_matriz_reflete_as_permissoes_reais` — Cada célula da matriz é lida de acesso.PERMISSOES, ação por ação. | 2 | ⬜ | A cartilha do Jornada é o `docs/CARTILHA.md`; a matriz derivada do código vem com pessoas e acesso. |
| `test_matriz_acompanha_mudanca_de_permissao` — Mudou a regra no código, muda a página — sem ninguém editar a cartilha. | 2 | ⬜ |  |
| `test_curador_nao_aparece_decidindo_validacao` — A segregação de função precisa estar visível, não só aplicada. | 2 | ⬜ |  |
| `test_consulta_nao_pode_nada_na_matriz` | 2 | ⬜ |  |
| `test_etapas_por_papel_sai_do_acesso` | 2 | ⬜ |  |
| `test_ritos_vem_da_tabela_de_politicas` | 4 | ⬜ |  |
| `test_rito_critico_e_mais_apertado_que_o_padrao` — O que a cartilha afirma sobre criticidade precisa ser verdade no banco. | 0 | ✅ | `ApiCriticaEhMaisExigente` |
| `test_rodape_leva_a_cartilha_em_qualquer_tela` — O rodapé é do site, não da tela: precisa estar em todas. | 0 | ✅ | `Layout.test.tsx` |
| `test_atalho_fica_ao_lado_do_botao_de_identificacao` — Ao lado de "quem sou eu" fica "o que eu posso fazer". | 0 | ✅ | `Layout.test.tsx` |
| `test_atalho_se_marca_quando_voce_esta_na_cartilha` | 0 | ✅ | `Layout.test.tsx` |
| `test_cores_de_perfil_existem_nos_tres_estados_de_tema` — Se a cor só existe no claro, a cartilha fica ilegível de noite. | 2 | 🟡 | `estilos.test.ts` garante todo token nos três estados; as cores por perfil chegam com a matriz da cartilha. |
| `test_rodape_e_atalho_tem_estilo_proprio` | 0 | ✅ | `estilos.test.ts` |
