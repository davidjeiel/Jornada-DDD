// Menu e rotas: a mesma estrutura do painel-ddd (catalogo/templates/base.html),
// para que as duas ferramentas tenham as mesmas portas de entrada. Telas que
// ainda não chegaram respondem com o aviso da fase que as entrega
// (docs/PARIDADE.md), em vez de sumirem do menu.
export type Secao =
  | 'painel' | 'catalogo' | 'mapas'
  | 'mesa' | 'novo' | 'descobertas' | 'validacoes'
  | 'notificacoes' | 'perfil'
  | 'pleitos' | 'squads' | 'gestao-acessos' | 'politicas'
  | 'cartilha' | 'solicitar-acesso' | 'nao-encontrada';

export type Grupo = 'Trabalhar' | 'Você' | 'Administração';

export type ItemDeMenu = {
  secao: Secao;
  caminho: string;
  rotulo: string;
  /** O que aparece no menu recolhido, como no painel ("VE", "CA"...). */
  inicial: string;
  grupo?: Grupo;
  /** Quem vê o item: só papéis que concedem acesso, ou só o admin. */
  exige?: 'conceder' | 'administrar';
  /** Fase do plano de paridade que entrega a tela; ausente = já entregue. */
  fase?: number;
  descricao: string;
};

export const MENU: ItemDeMenu[] = [
  { secao: 'painel', caminho: '/', rotulo: 'Visão executiva', inicial: 'VE', descricao: 'Indicadores do catálogo e da governança.' },
  { secao: 'catalogo', caminho: '/catalogo', rotulo: 'Catálogo de ativos', inicial: 'CA', descricao: 'Todos os ativos da empresa, com busca e filtros.' },
  { secao: 'mapas', caminho: '/mapas', rotulo: 'Mapas', inicial: 'MP', descricao: 'Árvores de negócio e técnica, vizinhança e análise de consumidores.' },

  { secao: 'mesa', caminho: '/mesa', rotulo: 'Minha mesa', inicial: 'MM', grupo: 'Trabalhar', descricao: 'O que é seu: rascunhos, validações assumidas e pendências.' },
  { secao: 'novo', caminho: '/novo', rotulo: 'Novo ativo', inicial: 'NA', grupo: 'Trabalhar', descricao: 'Cadastro de um ativo em rascunho.' },
  { secao: 'descobertas', caminho: '/descobertas', rotulo: 'Descobertas', inicial: 'DE', grupo: 'Trabalhar', fase: 9, descricao: 'Importação de OpenAPI e inventário Git, com prévia e triagem.' },
  { secao: 'validacoes', caminho: '/validacoes', rotulo: 'Validações', inicial: 'VL', grupo: 'Trabalhar', descricao: 'Fila de etapas de validação aguardando decisão.' },

  { secao: 'notificacoes', caminho: '/notificacoes', rotulo: 'Notificações', inicial: 'NT', grupo: 'Você', descricao: 'Caixa de avisos de submissão, decisão e SLA.' },
  { secao: 'perfil', caminho: '/perfil', rotulo: 'Seu perfil', inicial: 'PF', grupo: 'Você', descricao: 'Quem você é na ferramenta e o que o seu papel permite.' },

  { secao: 'pleitos', caminho: '/pleitos', rotulo: 'Pleitos de acesso', inicial: 'PA', grupo: 'Administração', exige: 'conceder', descricao: 'Fila de pedidos de papel para quem concede acesso.' },
  { secao: 'squads', caminho: '/squads', rotulo: 'Squads', inicial: 'SQ', grupo: 'Administração', exige: 'conceder', descricao: 'Os times da empresa.' },
  { secao: 'gestao-acessos', caminho: '/gestao-acessos', rotulo: 'Gestão de acessos', inicial: 'GA', grupo: 'Administração', exige: 'administrar', descricao: 'Cadastros, concessões e revogações de papel em toda a empresa.' },
  { secao: 'politicas', caminho: '/politicas', rotulo: 'Políticas', inicial: 'PO', grupo: 'Administração', descricao: 'Rito de validação por tipo e criticidade: etapas, evidência, score e SLA.' },
];

export const CARTILHA: ItemDeMenu = {
  secao: 'cartilha', caminho: '/cartilha', rotulo: 'Cartilha', inicial: '?', descricao: 'O que cada papel faz na ferramenta.',
};

/** Fora do menu, como no painel: é o link de quem ainda não tem papel. */
export const SOLICITAR_ACESSO: ItemDeMenu = {
  secao: 'solicitar-acesso', caminho: '/acesso/solicitar', rotulo: 'Solicitar acesso', inicial: 'SA', descricao: 'Cadastro e pedido de papel.',
};

/** O que a API diz que a pessoa pode (GET /v1/perfil). */
export type Alcadas = { podeConceder: boolean; podeAdministrar: boolean };

// A decisão é do servidor (acesso.PERMISSOES, com os papéis concedidos e o
// modo de leitura): esconder o item é conforto, não autorização.
export function itensVisiveis(alcadas: Alcadas): ItemDeMenu[] {
  return MENU.filter((item) =>
    item.exige === 'conceder' ? alcadas.podeConceder
      : item.exige === 'administrar' ? alcadas.podeAdministrar
        : true);
}

/**
 * A seção que o caminho marca no menu. O destaque é por seção, não por rota
 * exata: dentro de um ativo (/ativos/...), "Catálogo de ativos" continua marcado.
 */
export function secaoDoCaminho(caminho: string): Secao {
  const limpo = caminho.replace(/\/+$/, '') || '/';
  if (limpo.startsWith('/ativos/')) return 'catalogo';
  if (limpo === CARTILHA.caminho) return 'cartilha';
  if (limpo === SOLICITAR_ACESSO.caminho) return 'solicitar-acesso';
  return MENU.find((item) => item.caminho === limpo)?.secao ?? 'nao-encontrada';
}

export function itemDaSecao(secao: Secao): ItemDeMenu | undefined {
  if (secao === 'cartilha') return CARTILHA;
  if (secao === 'solicitar-acesso') return SOLICITAR_ACESSO;
  return MENU.find((item) => item.secao === secao);
}
