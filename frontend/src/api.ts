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
  caminho: { passo: string; rotulo: string; concluido: boolean; impedimentos: string[] }[];
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
};

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
  trilha: string[];
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
}): Promise<{ id: string }> {
  return requisicao('/v1/ativos', { method: 'POST', body: JSON.stringify(payload) });
}

export function editarAtivo(id: string, payload: {
  nome: string;
  descricao: string;
  criticidade: string;
  atributos: Record<string, string>;
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

export function relacionarAtivos(id: string, idDestino: string, tipo: string, criticidade = 'media'): Promise<void> {
  return requisicao(`/v1/ativos/${id}/relacoes`, {
    method: 'POST',
    body: JSON.stringify({ idDestino, tipo, criticidade }),
  });
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
