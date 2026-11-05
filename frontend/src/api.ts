import { cabecalhosDe, lerIdentidade, type Identidade } from './identidade';

export type AtivoResumo = {
  id: string;
  codigo: string;
  nome: string;
  tipo: string;
  status: string;
  criticidade: string;
};

export type PreCheck = {
  aprovado: boolean;
  score: number;
  dimensoes: Record<string, number>;
  bloqueios: { codigo: string; mensagem: string }[];
  alertas: string[];
  politica: { scoreMinimo: number; evidenciaMinima: number; slaHoras: number; etapas: string[] };
  caminho: { passo: string; rotulo: string; concluido: boolean; impedimentos: string[]; pendencias: string[] }[];
};

export type Validacao = {
  id: string;
  ativo: string;
  revisao: number;
  etapa: string;
  status: string;
  submetidaPor: string;
  decididaPor: string | null;
  motivo: string | null;
  abertaEm: string | null;
  prazo: string | null;
  atribuidaA: string | null;
};

export type Evidencia = { id: string; tipo: string; titulo: string; url: string | null };

export type Responsavel = {
  idPessoa: string;
  papel: string;
  inicioVigencia: string;
  fimVigencia: string | null;
  vigente: boolean;
};

export type RelacaoDoAtivo = {
  direcao: 'saida' | 'entrada';
  ativoId: string;
  codigo: string;
  nome: string;
  tipo: string;
  criticidade: string;
  mecanismo: string | null;
};

/** Um degrau da trilha hierárquica, da raiz até o pai direto. */
export type Ancestral = { id: string; codigo: string; nome: string; tipoItem: string };

export type Consumidor = { id: string; codigo: string; nome: string; tipoItem: string; tipoRelacao: string; criticidade: string };

/** Quem seria afetado se o ativo fosse descontinuado (GET /ativos/{id}/impacto). */
export type Impacto = {
  id: string;
  codigo: string;
  total: number;
  criticos: number;
  exigeConfirmacao: boolean;
  consumidores: Consumidor[];
  porCriticidade: Record<string, number>;
  porTipo: Record<string, number>;
};

export type FichaDoAtivo = {
  id: string;
  codigo: string;
  nome: string;
  tipoItem: string;
  status: string;
  criticidade: string;
  descricao: string;
  revisaoAtual: number;
  idSquad: string | null;
  trilha: Ancestral[];
  atributos: Record<string, string>;
  evidencias: Evidencia[];
  responsaveis: Responsavel[];
  relacoes: RelacaoDoAtivo[];
};

export type RevisaoRetratada = {
  numero: number;
  motivo: string;
  autor: string;
  criadaEm: string;
  publicadaEm: string | null;
  hash: string;
  integra: boolean;
};

export type RegistroDeAuditoria = {
  acao: string;
  pessoa: string;
  em: string;
  antes: Record<string, string> | null;
  depois: Record<string, string> | null;
};

/** Revisões submetidas (com hash) e trilha de auditoria (GET /ativos/{id}/historico). */
export type Historico = { revisoes: RevisaoRetratada[]; auditoria: RegistroDeAuditoria[] };

export type Comparacao = { a: number; b: number; diferencas: { campo: string; rotulo: string; antes: string; depois: string }[] };

// ── Pessoas e acesso (ADR-0018) ─────────────────────────────────────────

export type Pessoa = {
  id: string;
  matricula: string;
  nome: string;
  email: string;
  unidade: string;
  ativa: boolean;
  papeis: string[];
  idSquad: string | null;
};

export type PapelConcedido = {
  papel: string;
  rotulo: string;
  escopoTipo: string;
  escopoId: string | null;
  escopoRotulo: string;
  blocos: string[];
  desde: string;
};

export type Pleito = {
  id: string;
  status: 'pendente' | 'aprovado' | 'negado';
  papelPleiteado: string;
  justificativa: string;
  criadoEm: string;
  pessoa: Pessoa | null;
  papelConcedido: string | null;
  escopoTipo: string | null;
  escopoRotulo: string | null;
  decididoPor: string | null;
  decididoEm: string | null;
  resposta: string;
};

/** Quem você é para a API agora, e o que pode fazer (GET /v1/perfil). */
export type Perfil = {
  identificado: boolean;
  modoLeitura: boolean;
  modoDemonstracao: boolean;
  pessoa: Pessoa | null;
  papeis: PapelConcedido[];
  pleitos: Pleito[];
  blocosQueEscreve: string[];
  podeConceder: boolean;
  podeConcederAdmin: boolean;
  podeAdministrar: boolean;
};

export type FilaDePleitos = { total: number; pendentes: number; podeConcederAdmin: boolean; pleitos: Pleito[] };

export type Squad = { id: string; sigla: string; titulo: string; descricao: string; ativa: boolean };

/** Um cadastro na gestão de acessos (só admin). */
export type Cadastro = { pessoa: Pessoa; squad: Squad | null; papeis: PapelConcedido[] };

export type LinhaDaMatriz = { acao: string; rotulo: string; papeis: Record<string, boolean> };

export type Rito = {
  tipoItem: string; rotulo: string; criticidade: string; etapas: string[];
  evidenciaMinima: number; scoreMinimo: number; slaHoras: number; revisarACadaDias: number;
};

/** As tabelas da cartilha, lidas das regras que a API aplica (GET /v1/cartilha). */
export type DadosDaCartilha = {
  papeis: string[];
  matriz: LinhaDaMatriz[];
  etapasPorPapel: Record<string, string[]>;
  blocosPorPapel: Record<string, string[]>;
  ritos: Rito[];
};

/** Recusa da API no padrão CAIXA (ApiError), com o código para a tela decidir o que fazer. */
export class ErroDaApi extends Error {
  constructor(public readonly codigo: string, mensagem: string, public readonly status: number) {
    super(mensagem);
  }
}

// ── Identidade ────────────────────────────────────────────────────────────
// Empresa, pessoa e modo vão por cabeçalho, como vão virar claim de token
// depois (ADR-0009). Nenhum caso de uso recebe isso no corpo da requisição.
let identidadeAtual: Identidade = lerIdentidade();

// Escrita sem pessoa: a tela manda ao perfil, como o web.exige do painel.
let aoFaltarIdentificacao: () => void = () => {};

export function definirAoFaltarIdentificacao(acao: () => void): void {
  aoFaltarIdentificacao = acao;
}

export function obterIdentidade(): Identidade {
  return identidadeAtual;
}

export function definirIdentidade(identidade: Identidade): void {
  identidadeAtual = identidade;
}

async function requisicao<T>(caminho: string, init?: RequestInit): Promise<T> {
  const resposta = await fetch(`/api${caminho}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...cabecalhosDe(identidadeAtual),
      ...init?.headers,
    },
  });

  if (!resposta.ok) {
    // Erro no padrão CAIXA (ApiError): { codigo, mensagem, correlationId, dataHora }.
    const erro = await resposta.json().catch(() => null);
    if (erro?.codigo === 'PESSOA_NAO_IDENTIFICADA') aoFaltarIdentificacao();
    throw new ErroDaApi(erro?.codigo ?? 'ERRO', erro?.mensagem ?? `A API respondeu com ${resposta.status}`, resposta.status);
  }

  return resposta.status === 204 ? (undefined as T) : resposta.json();
}

export function consultarPerfil(): Promise<Perfil> {
  return requisicao('/v1/perfil');
}

export function listarPessoas(): Promise<{ total: number; pessoas: Pessoa[] }> {
  return requisicao('/v1/pessoas');
}

/** Aberta a quem não tem papel: cadastra a pessoa e pede o papel. */
export function solicitarAcesso(payload: {
  matricula: string; nome: string; email: string; unidade: string; papelPleiteado: string; justificativa: string;
}): Promise<{ idPessoa: string; idPleito: string; status: string }> {
  return requisicao('/v1/pleitos', { method: 'POST', body: JSON.stringify(payload) });
}

export function listarPleitos(status?: string): Promise<FilaDePleitos> {
  return requisicao(`/v1/pleitos${status ? `?status=${status}` : ''}`);
}

export function decidirPleito(id: string, payload: {
  aprovar: boolean; resposta: string; papelConcedido?: string; escopoTipo?: string; escopoId?: string;
}): Promise<Pleito> {
  return requisicao(`/v1/pleitos/${id}/decisoes`, { method: 'POST', body: JSON.stringify(payload) });
}

export function listarAtivos(): Promise<{ total: number; ativos: AtivoResumo[] }> {
  return requisicao('/v1/ativos');
}

export function obterAtivo(id: string): Promise<FichaDoAtivo> {
  return requisicao(`/v1/ativos/${id}`);
}

export function avaliarPreCheck(id: string): Promise<PreCheck> {
  return requisicao(`/v1/ativos/${id}/pre-check`);
}

export function cadastrarAtivo(payload: {
  tipo: string;
  nome: string;
  descricao: string;
  criticidade: string;
  atributos: Record<string, string>;
  idPai?: string;
  idSquad?: string;
}): Promise<{ id: string }> {
  return requisicao('/v1/ativos', { method: 'POST', body: JSON.stringify(payload) });
}

export function editarAtivo(id: string, payload: {
  nome: string;
  descricao: string;
  criticidade: string;
  atributos: Record<string, string>;
  idSquad?: string | null;
}): Promise<void> {
  return requisicao(`/v1/ativos/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export function atribuirResponsavel(id: string, idPessoa: string, papel: string = 'OwnerTecnico'): Promise<void> {
  return requisicao(`/v1/ativos/${id}/responsaveis`, {
    method: 'POST',
    body: JSON.stringify({ idPessoa, papel }),
  });
}

export function anexarEvidencia(id: string, payload: { tipo: string; titulo: string; url?: string }): Promise<void> {
  return requisicao(`/v1/ativos/${id}/evidencias`, { method: 'POST', body: JSON.stringify(payload) });
}

export type NovaRelacao = { tipo: string; criticidade: string; mecanismo?: string };

/** criada=false: a mesma relação já existia, e nada mudou. */
export function relacionarAtivos(id: string, idDestino: string, relacao: NovaRelacao): Promise<{ criada: boolean }> {
  return requisicao(`/v1/ativos/${id}/relacoes`, {
    method: 'POST',
    body: JSON.stringify({ idDestino, ...relacao, mecanismo: relacao.mecanismo || undefined }),
  });
}

export type ResultadoDoLote = { criadas: number; ignoradas: number; erros: { destino: string; codigo: string; mensagem: string }[] };

export function relacionarEmLote(id: string, destinos: string[], relacao: NovaRelacao): Promise<ResultadoDoLote> {
  return requisicao(`/v1/ativos/${id}/relacoes/lote`, {
    method: 'POST',
    body: JSON.stringify({ destinos, ...relacao, mecanismo: relacao.mecanismo || undefined }),
  });
}

export type CampoDoTipo = { nome: string; rotulo: string; tipo: string; obrigatorio: boolean; opcoes: string[] };
export type TipoDeAtivo = { chave: string; rotulo: string; bloco: string; pai: string | null; campos: CampoDoTipo[] };
export type TipoDeRelacao = { chave: string; rotulo: string; destinosSugeridos: string[] };

/** Tipos de ativo, tipos de relação e mecanismos da empresa (GET /v1/metamodelo). */
export type Metamodelo = { tipos: TipoDeAtivo[]; relacoes: TipoDeRelacao[]; mecanismos: { chave: string; rotulo: string }[] };

export function consultarMetamodelo(): Promise<Metamodelo> {
  return requisicao('/v1/metamodelo');
}

export function analisarImpacto(id: string): Promise<Impacto> {
  return requisicao(`/v1/ativos/${id}/impacto`);
}

// Com consumidor de alta criticidade, a API exige a confirmação: o código do ativo.
export function descontinuarAtivo(id: string, motivo: string, confirmacao?: string): Promise<{ afetados: number }> {
  return requisicao(`/v1/ativos/${id}/descontinuacao`, {
    method: 'POST',
    body: JSON.stringify({ motivo, confirmacao }),
  });
}

export function consultarHistorico(id: string): Promise<Historico> {
  return requisicao(`/v1/ativos/${id}/historico`);
}

// Sem a e b, a API compara a última revisão com a anterior.
export function compararRevisoes(id: string, a?: number, b?: number): Promise<Comparacao> {
  const parametros = a !== undefined && b !== undefined ? `?a=${a}&b=${b}` : '';
  return requisicao(`/v1/ativos/${id}/revisoes/comparacao${parametros}`);
}

export function arquivarAtivo(id: string): Promise<void> {
  return requisicao(`/v1/ativos/${id}/arquivamento`, { method: 'POST' });
}

// Publicado é imutável: alterar exige abrir revisão (o ativo vai para em_revisao).
export function abrirRevisao(id: string): Promise<void> {
  return requisicao(`/v1/ativos/${id}/revisoes`, { method: 'POST' });
}

export function submeterAtivo(id: string, motivo: string): Promise<{ revisao: number; score: number; etapasAbertas: string[] }> {
  return requisicao(`/v1/ativos/${id}/submissoes`, {
    method: 'POST',
    body: JSON.stringify({ motivo }),
  });
}

export function listarValidacoes(ativo?: string): Promise<{ total: number; validacoes: Validacao[] }> {
  return requisicao(`/v1/validacoes${ativo ? `?ativo=${ativo}` : ''}`);
}

export function decidirValidacao(id: string, aprovada: boolean, motivo: string): Promise<void> {
  return requisicao(`/v1/validacoes/${id}/decisoes`, {
    method: 'POST',
    body: JSON.stringify({ aprovada, motivo }),
  });
}

export function consultarCartilha(): Promise<DadosDaCartilha> {
  return requisicao('/v1/cartilha');
}

// ── Gestão de acessos (só admin) ─────────────────────────────────────────

export function listarCadastros(busca = ''): Promise<{ total: number; cadastros: Cadastro[] }> {
  return requisicao(`/v1/gestao/pessoas${busca ? `?busca=${encodeURIComponent(busca)}` : ''}`);
}

export function consultarVinculos(id: string): Promise<{ podeExcluir: boolean; vinculos: string[] }> {
  return requisicao(`/v1/gestao/pessoas/${id}/vinculos`);
}

export function corrigirCadastro(id: string, payload: {
  nome: string; email: string; unidade: string; idSquad: string | null; ativa: boolean;
}): Promise<Cadastro> {
  return requisicao(`/v1/gestao/pessoas/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export type PapelNoEscopo = { papel: string; escopoTipo: string; escopoId?: string | null };

export function concederPapel(id: string, payload: PapelNoEscopo): Promise<Cadastro> {
  return requisicao(`/v1/gestao/pessoas/${id}/papeis`, { method: 'POST', body: JSON.stringify(payload) });
}

export function revogarPapel(id: string, payload: PapelNoEscopo): Promise<Cadastro> {
  return requisicao(`/v1/gestao/pessoas/${id}/revogacoes`, { method: 'POST', body: JSON.stringify(payload) });
}

export function inativarEmLote(ids: string[]): Promise<{ inativados: number }> {
  return requisicao('/v1/gestao/inativacoes', { method: 'POST', body: JSON.stringify({ ids }) });
}

export function excluirCadastro(id: string): Promise<void> {
  return requisicao(`/v1/gestao/pessoas/${id}`, { method: 'DELETE' });
}

// ── Squads ────────────────────────────────────────────────────────────────

export function listarSquads(busca = ''): Promise<{ total: number; squads: Squad[] }> {
  return requisicao(`/v1/squads${busca ? `?busca=${encodeURIComponent(busca)}` : ''}`);
}

export type DadosDoSquad = { sigla: string; titulo: string; descricao: string };

export function cadastrarSquad(payload: DadosDoSquad): Promise<Squad> {
  return requisicao('/v1/squads', { method: 'POST', body: JSON.stringify(payload) });
}

export function alterarSquad(id: string, payload: DadosDoSquad): Promise<Squad> {
  return requisicao(`/v1/squads/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
}

export function inativarSquad(id: string): Promise<Squad> {
  return requisicao(`/v1/squads/${id}/inativacao`, { method: 'POST' });
}

/** O rito que a política aplica ao tipo e à criticidade (GET /v1/ritos). */
export function consultarRito(tipo: string, criticidade: string): Promise<Rito> {
  return requisicao(`/v1/ritos?tipo=${encodeURIComponent(tipo)}&criticidade=${encodeURIComponent(criticidade)}`);
}

// ── Fila de validação e mesa (fase 4) ────────────────────────────────────

export type ItemDaFila = { validacao: Validacao; ativo: AtivoResumo; slaRestante: string };
export type FilaDeValidacao = { itens: ItemDaFila[]; contagem: Record<string, number> };

export function consultarFila(filtro: { etapa?: string; minhas?: boolean; livres?: boolean } = {}): Promise<FilaDeValidacao> {
  const busca = new URLSearchParams();
  if (filtro.etapa) busca.set('etapa', filtro.etapa);
  if (filtro.minhas) busca.set('minhas', 'true');
  if (filtro.livres) busca.set('livres', 'true');
  const texto = busca.toString();
  return requisicao(`/v1/validacoes/fila${texto ? `?${texto}` : ''}`);
}

export function assumirValidacao(id: string): Promise<Validacao> {
  return requisicao(`/v1/validacoes/${id}/atribuicao`, { method: 'POST' });
}

export function liberarValidacao(id: string): Promise<Validacao> {
  return requisicao(`/v1/validacoes/${id}/atribuicao`, { method: 'DELETE' });
}

/** Tudo o que o validador precisa para decidir: a ficha, o pré-check e o que mudou. */
export type PainelDoValidador = {
  validacao: Validacao; ficha: FichaDoAtivo; preCheck: PreCheck; mudancas: Comparacao | null; primeiraRevisao: boolean;
};

export function consultarPainel(id: string): Promise<PainelDoValidador> {
  return requisicao(`/v1/validacoes/${id}/painel`);
}

export type Mesa = { minhasValidacoes: ItemDaFila[]; filaLivre: ItemDaFila[]; meusRascunhos: AtivoResumo[]; aguardandoDecisao: AtivoResumo[] };

export function consultarMesa(): Promise<Mesa> {
  return requisicao('/v1/mesa');
}

// ── Catálogo com recorte, ordem e página (fase 5) ────────────────────────

export type ItemDoCatalogo = { ativo: AtivoResumo; origem: string; idSquad: string | null; temResponsavel: boolean; atualizadoEm: string };

export type PaginaDoCatalogo = {
  itens: ItemDoCatalogo[]; total: number; pagina: number; paginas: number; porPagina: number;
  primeiro: number; ultimo: number; ordem: string; descendente: boolean; truncado: boolean;
};

/** Os parâmetros vão como chegam da URL do catálogo: a API confere e aplica os padrões. */
export function buscarNoCatalogo(parametros: URLSearchParams): Promise<PaginaDoCatalogo> {
  const texto = parametros.toString();
  return requisicao(`/v1/catalogo${texto ? `?${texto}` : ''}`);
}

export type Sugestao = { id: string; codigo: string; nome: string; tipo: string; status: string; url: string };

export function sugerirAtivos(termo: string): Promise<{ termo: string; itens: Sugestao[] }> {
  return requisicao(`/v1/busca/sugestoes?termo=${encodeURIComponent(termo)}`);
}

// ── Mapas e grafo (fase 6) ───────────────────────────────────────────────

export type NoDaVizinhanca = { ativo: AtivoResumo; salto: number };
export type Aresta = { origem: string; destino: string; tipo: string; criticidade: string; mecanismo: string | null };
export type Vizinhanca = { centro: string; saltos: number; nos: NoDaVizinhanca[]; arestas: Aresta[]; truncado: boolean; teto: number };

export function consultarVizinhanca(id: string, saltos = 1, tipos: string[] = []): Promise<Vizinhanca> {
  const busca = new URLSearchParams({ saltos: String(saltos) });
  if (tipos.length) busca.set('tipos', tipos.join(','));
  return requisicao(`/v1/ativos/${id}/vizinhanca?${busca.toString()}`);
}

export type NoDaArvore = { ativo: AtivoResumo; implementacoes: number; filhos: NoDaArvore[] };

export function consultarArvore(visao: 'negocio' | 'tecnica'): Promise<{ visao: string; raizes: NoDaArvore[] }> {
  return requisicao(`/v1/mapa/arvore?visao=${visao}`);
}

export type FotografiaDosIndicadores = { competencia: string; valores: Record<string, number>; geradaEm: string };
export type Indicadores = {
  atuais: Record<string, number>;
  variacao: Record<string, number>;
  quantoMenosMelhor: string[];
  serie: FotografiaDosIndicadores[];
};

export function consultarIndicadores(): Promise<Indicadores> {
  return requisicao('/v1/indicadores');
}

export function fotografarIndicadores(competencia?: string): Promise<FotografiaDosIndicadores> {
  const busca = competencia ? `?competencia=${encodeURIComponent(competencia)}` : '';
  return requisicao(`/v1/indicadores/fotografias${busca}`, { method: 'POST' });
}
