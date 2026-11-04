// Perfis como a API devolve (GET /v1/perfil), para os testes das telas.
import type { Perfil, Pleito } from '../api';
import { ROTULO_DO_PAPEL, type ChaveDePapel } from '../papeis';

const BLOCOS: Record<ChaveDePapel, string[]> = {
  admin: ['Ativos técnicos', 'Estrutura DDD'],
  curador: ['Ativos técnicos', 'Estrutura DDD'],
  arquiteto: ['Ativos técnicos', 'Estrutura DDD'],
  techlead: ['Ativos técnicos'],
  negocio: ['Estrutura DDD'],
  consulta: [],
};

export function perfilDe(papel: ChaveDePapel | null, pleitos: Pleito[] = []): Perfil {
  return {
    identificado: true,
    modoLeitura: false,
    modoDemonstracao: true,
    pessoa: { id: 'p-1', matricula: 'C123456', nome: 'Davi Souza', email: 'davi@empresa.com', unidade: '0427', ativa: true, papeis: papel ? [papel] : [], idSquad: null },
    papeis: papel
      ? [{ papel, rotulo: ROTULO_DO_PAPEL[papel], escopoTipo: 'empresa', escopoId: null, escopoRotulo: 'todo o catálogo', blocos: BLOCOS[papel], desde: '2026-10-01' }]
      : [],
    pleitos,
    blocosQueEscreve: papel ? BLOCOS[papel] : [],
    podeConceder: papel === 'admin' || papel === 'curador',
    podeConcederAdmin: papel === 'admin',
    podeAdministrar: papel === 'admin',
  };
}

export function pleito(dados: Partial<Pleito> = {}): Pleito {
  return {
    id: 'pl-1', status: 'pendente', papelPleiteado: 'techlead', justificativa: 'Vou cadastrar APIs.', criadoEm: '2026-11-01T12:00:00Z',
    pessoa: { id: 'p-1', matricula: 'C123456', nome: 'Davi Souza', email: 'davi@empresa.com', unidade: '0427', ativa: true, papeis: [], idSquad: null },
    papelConcedido: null, escopoTipo: null, escopoRotulo: null, decididoPor: null, decididoEm: null, resposta: '',
    ...dados,
  };
}
