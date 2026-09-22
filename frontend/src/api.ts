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

const empresa = '11111111-1111-1111-1111-111111111111';
const papel = 'curador';
const pessoa = '33333333-3333-3333-3333-333333333333';

async function requisicao<T>(caminho: string, init?: RequestInit): Promise<T> {
  const resposta = await fetch(`/api${caminho}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Empresa': empresa,
      'X-Papel': papel,
      'X-Pessoa': pessoa,
      ...init?.headers,
    },
  });

  if (!resposta.ok) {
    const problema = await resposta.json().catch(() => null);
    throw new Error(problema?.detail ?? `A API respondeu com ${resposta.status}`);
  }

  return resposta.status === 204 ? (undefined as T) : resposta.json();
}

export function listarAtivos(): Promise<{ total: number; ativos: AtivoResumo[] }> {
  return requisicao('/v1/ativos');
}

export function obterAtivo(id: string): Promise<Record<string, unknown>> {
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

export function atribuirResponsavel(id: string): Promise<void> {
  return requisicao(`/v1/ativos/${id}/responsaveis`, {
    method: 'POST',
    body: JSON.stringify({ idPessoa: pessoa, papel: 'OwnerTecnico' }),
  });
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
