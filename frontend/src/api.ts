import { empresaDe, unidadeDe, usuarioSalvo, type UsuarioDeTeste } from './usuarios';

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

// ── Identidade de teste (ADR-0007 ainda não tem SSO real) ─────────────────
// Trocar de usuário troca as três coisas que o middleware de tenant lê:
// empresa, papel e pessoa — e, quando houver, a unidade. Nenhum caso de uso
// recebe isso "de quem chama" no corpo da requisição; é sempre cabeçalho,
// exatamente como vai virar claim de token depois (ADR-0009).
let usuarioAtual: UsuarioDeTeste = usuarioSalvo();

export function obterUsuarioAtual(): UsuarioDeTeste {
  return usuarioAtual;
}

export function definirUsuarioAtual(usuario: UsuarioDeTeste): void {
  usuarioAtual = usuario;
}

async function requisicao<T>(caminho: string, init?: RequestInit): Promise<T> {
  const resposta = await fetch(`/api${caminho}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Empresa': usuarioAtual.empresaId,
      'X-Papel': usuarioAtual.papel,
      'X-Pessoa': usuarioAtual.id,
      'X-Unidade': usuarioAtual.unidadeId,
      ...init?.headers,
    },
  });

  if (!resposta.ok) {
    // Erro no padrão CAIXA (ApiError): { codigo, mensagem, correlationId, dataHora }.
    const erro = await resposta.json().catch(() => null);
    throw new Error(erro?.mensagem ?? `A API respondeu com ${resposta.status}`);
  }

  return resposta.status === 204 ? (undefined as T) : resposta.json();
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

export function atribuirResponsavel(id: string, papel: string = 'OwnerTecnico'): Promise<void> {
  return requisicao(`/v1/ativos/${id}/responsaveis`, {
    method: 'POST',
    body: JSON.stringify({ idPessoa: usuarioAtual.id, papel }),
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

export { empresaDe, unidadeDe };
