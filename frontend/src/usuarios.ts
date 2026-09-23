// Usuários de teste para exercitar a plataforma sem identidade real (ADR-0007
// ainda não tem Entra/Keycloak plugado — o contexto vem de cabeçalho HTTP).
//
// Dois por papel, cobrindo DUAS empresas e MAIS DE UMA unidade cada, para dar
// para testar ao vivo: isolamento entre empresas (RLS, ADR-0003), segregação
// de função (quem submete não decide) e o bloco de tipos por papel
// (BLOCOS_POR_PAPEL) — tudo trocando de usuário, sem precisar de outra aba.

export type Papel = 'admin' | 'curador' | 'arquiteto' | 'techlead' | 'negocio' | 'consulta';

export type Empresa = { id: string; nome: string };
export type Unidade = { id: string; nome: string; empresaId: string };

export const empresas: Empresa[] = [
  { id: '11111111-1111-1111-1111-111111111111', nome: 'Horizonte Seguros' },
  { id: '22222222-2222-2222-2222-222222222222', nome: 'Meridian Logística' },
];

export const unidades: Unidade[] = [
  { id: 'a0000000-0000-0000-0000-000000000001', nome: 'Matriz', empresaId: empresas[0].id },
  { id: 'a0000000-0000-0000-0000-000000000002', nome: 'TI Corporativa', empresaId: empresas[0].id },
  { id: 'b0000000-0000-0000-0000-000000000001', nome: 'Filial Sul', empresaId: empresas[1].id },
  { id: 'b0000000-0000-0000-0000-000000000002', nome: 'Engenharia', empresaId: empresas[1].id },
];

export type UsuarioDeTeste = {
  id: string;
  nome: string;
  papel: Papel;
  rotuloPapel: string;
  empresaId: string;
  unidadeId: string;
};

const rotulos: Record<Papel, string> = {
  admin: 'Admin',
  curador: 'Curador(a)',
  arquiteto: 'Arquiteto(a)',
  techlead: 'Tech Lead',
  negocio: 'Negócio',
  consulta: 'Consulta',
};

export const usuariosDeTeste: UsuarioDeTeste[] = [
  { id: '10000000-0000-0000-0000-000000000001', nome: 'Ana Ferreira', papel: 'admin', rotuloPapel: rotulos.admin, empresaId: empresas[0].id, unidadeId: unidades[0].id },
  { id: '10000000-0000-0000-0000-000000000002', nome: 'Bruno Alves', papel: 'admin', rotuloPapel: rotulos.admin, empresaId: empresas[1].id, unidadeId: unidades[2].id },

  { id: '20000000-0000-0000-0000-000000000001', nome: 'Marina Costa', papel: 'curador', rotuloPapel: rotulos.curador, empresaId: empresas[0].id, unidadeId: unidades[0].id },
  { id: '20000000-0000-0000-0000-000000000002', nome: 'Carla Dias', papel: 'curador', rotuloPapel: rotulos.curador, empresaId: empresas[1].id, unidadeId: unidades[3].id },

  { id: '30000000-0000-0000-0000-000000000001', nome: 'Rafael Souza', papel: 'arquiteto', rotuloPapel: rotulos.arquiteto, empresaId: empresas[0].id, unidadeId: unidades[1].id },
  { id: '30000000-0000-0000-0000-000000000002', nome: 'Priscila Nunes', papel: 'arquiteto', rotuloPapel: rotulos.arquiteto, empresaId: empresas[1].id, unidadeId: unidades[3].id },

  { id: '40000000-0000-0000-0000-000000000001', nome: 'Thiago Lima', papel: 'techlead', rotuloPapel: rotulos.techlead, empresaId: empresas[0].id, unidadeId: unidades[1].id },
  { id: '40000000-0000-0000-0000-000000000002', nome: 'Juliana Ramos', papel: 'techlead', rotuloPapel: rotulos.techlead, empresaId: empresas[1].id, unidadeId: unidades[3].id },

  { id: '50000000-0000-0000-0000-000000000001', nome: 'Eduardo Melo', papel: 'negocio', rotuloPapel: rotulos.negocio, empresaId: empresas[0].id, unidadeId: unidades[0].id },
  { id: '50000000-0000-0000-0000-000000000002', nome: 'Fernanda Rocha', papel: 'negocio', rotuloPapel: rotulos.negocio, empresaId: empresas[1].id, unidadeId: unidades[2].id },

  { id: '60000000-0000-0000-0000-000000000001', nome: 'Gustavo Pires', papel: 'consulta', rotuloPapel: rotulos.consulta, empresaId: empresas[0].id, unidadeId: unidades[0].id },
  { id: '60000000-0000-0000-0000-000000000002', nome: 'Helena Cardoso', papel: 'consulta', rotuloPapel: rotulos.consulta, empresaId: empresas[1].id, unidadeId: unidades[2].id },
];

export function empresaDe(usuario: UsuarioDeTeste): Empresa {
  return empresas.find((e) => e.id === usuario.empresaId)!;
}

export function unidadeDe(usuario: UsuarioDeTeste): Unidade {
  return unidades.find((u) => u.id === usuario.unidadeId)!;
}

export function iniciaisDe(nome: string): string {
  const partes = nome.trim().split(/\s+/);
  return ((partes[0]?.[0] ?? '') + (partes[partes.length - 1]?.[0] ?? '')).toUpperCase();
}

const CHAVE_ARMAZENAMENTO = 'jornada-ddd:usuario-de-teste';

export function usuarioSalvo(): UsuarioDeTeste {
  try {
    const id = localStorage.getItem(CHAVE_ARMAZENAMENTO);
    const encontrado = usuariosDeTeste.find((u) => u.id === id);
    if (encontrado) return encontrado;
  } catch {
    // localStorage indisponível (aba privada, storage bloqueado): segue com o padrão.
  }
  return usuariosDeTeste[2]; // Marina Costa, curadora — o padrão histórico da demo.
}

export function salvarUsuario(usuario: UsuarioDeTeste): void {
  try {
    localStorage.setItem(CHAVE_ARMAZENAMENTO, usuario.id);
  } catch {
    // per-viewer convenience apenas: falhar aqui não pode quebrar a troca de usuário.
  }
}
