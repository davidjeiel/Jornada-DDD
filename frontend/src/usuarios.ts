// As empresas e as pessoas de teste da carga de demonstração, para exercitar a
// plataforma sem identidade real (ADR-0007 ainda não tem Entra/Keycloak).
//
// Desde a fase 2 o papel de cada uma é o CONCEDIDO, que a API devolve em
// GET /v1/perfil (ADR-0018). O papel listado aqui é só o que a carga concede,
// para o seletor do perfil dizer quem é quem antes de você escolher.

export type Papel = 'admin' | 'curador' | 'arquiteto' | 'techlead' | 'negocio' | 'consulta';

export type Empresa = { id: string; nome: string };

export const empresas: Empresa[] = [
  { id: '11111111-1111-1111-1111-111111111111', nome: 'Horizonte Seguros' },
  { id: '22222222-2222-2222-2222-222222222222', nome: 'Meridian Logística' },
];

export type UsuarioDeTeste = {
  id: string;
  nome: string;
  papel: Papel;
  rotuloPapel: string;
  empresaId: string;
};

const rotulos: Record<Papel, string> = {
  admin: 'Admin',
  curador: 'Curador(a)',
  arquiteto: 'Arquiteto(a)',
  techlead: 'Tech Lead',
  negocio: 'Negócio',
  consulta: 'Consulta',
};

// A carga de demonstração da API (src/hosts/Jornada.Api/Demonstracao) cadastra
// estas pessoas com estes ids: mudar um aqui exige mudar lá.
export const usuariosDeTeste: UsuarioDeTeste[] = [
  { id: '10000000-0000-0000-0000-000000000001', nome: 'Ana Ferreira', papel: 'admin', rotuloPapel: rotulos.admin, empresaId: empresas[0].id },
  { id: '10000000-0000-0000-0000-000000000002', nome: 'Bruno Alves', papel: 'admin', rotuloPapel: rotulos.admin, empresaId: empresas[1].id },

  { id: '20000000-0000-0000-0000-000000000001', nome: 'Marina Costa', papel: 'curador', rotuloPapel: rotulos.curador, empresaId: empresas[0].id },
  { id: '20000000-0000-0000-0000-000000000002', nome: 'Carla Dias', papel: 'curador', rotuloPapel: rotulos.curador, empresaId: empresas[1].id },

  { id: '30000000-0000-0000-0000-000000000001', nome: 'Rafael Souza', papel: 'arquiteto', rotuloPapel: rotulos.arquiteto, empresaId: empresas[0].id },
  { id: '30000000-0000-0000-0000-000000000002', nome: 'Priscila Nunes', papel: 'arquiteto', rotuloPapel: rotulos.arquiteto, empresaId: empresas[1].id },

  { id: '40000000-0000-0000-0000-000000000001', nome: 'Thiago Lima', papel: 'techlead', rotuloPapel: rotulos.techlead, empresaId: empresas[0].id },
  { id: '40000000-0000-0000-0000-000000000002', nome: 'Juliana Ramos', papel: 'techlead', rotuloPapel: rotulos.techlead, empresaId: empresas[1].id },

  { id: '50000000-0000-0000-0000-000000000001', nome: 'Eduardo Melo', papel: 'negocio', rotuloPapel: rotulos.negocio, empresaId: empresas[0].id },
  { id: '50000000-0000-0000-0000-000000000002', nome: 'Fernanda Rocha', papel: 'negocio', rotuloPapel: rotulos.negocio, empresaId: empresas[1].id },

  { id: '60000000-0000-0000-0000-000000000001', nome: 'Gustavo Pires', papel: 'consulta', rotuloPapel: rotulos.consulta, empresaId: empresas[0].id },
  { id: '60000000-0000-0000-0000-000000000002', nome: 'Helena Cardoso', papel: 'consulta', rotuloPapel: rotulos.consulta, empresaId: empresas[1].id },
];

export function empresaDe(empresaId: string): Empresa {
  return empresas.find((e) => e.id === empresaId) ?? { id: empresaId, nome: `empresa ${empresaId.slice(0, 8)}` };
}

// Nomes das pessoas da empresa, vindos de GET /v1/pessoas: a trilha e as
// filas mostram quem agiu pelo nome. Antes da lista chegar, valem os de teste.
const nomesConhecidos = new Map<string, string>();

export function registrarPessoas(pessoas: { id: string; nome: string }[]): void {
  for (const pessoa of pessoas) nomesConhecidos.set(pessoa.id, pessoa.nome);
}

/** Nome de quem agiu, para a trilha. */
export function nomeDaPessoa(id: string): string {
  return nomesConhecidos.get(id) ?? usuariosDeTeste.find((u) => u.id === id)?.nome ?? `pessoa ${id.slice(0, 8)}`;
}

export function iniciaisDe(nome: string): string {
  const partes = nome.trim().split(/\s+/);
  return ((partes[0]?.[0] ?? '') + (partes[partes.length - 1]?.[0] ?? '')).toUpperCase();
}
