// Menu e rotas: a mesma estrutura do painel-ddd (catalogo/templates/base.html),
// para que as duas ferramentas tenham as mesmas portas de entrada. Telas que
// ainda não chegaram respondem com o aviso da fase que as entrega
// (docs/PARIDADE.md), em vez de sumirem do menu.
import type { Papel } from './usuarios';

export type Secao =
  | 'painel' | 'catalogo' | 'mapas'
  | 'mesa' | 'novo' | 'descobertas' | 'validacoes'
  | 'notificacoes' | 'perfil'
  | 'pleitos' | 'squads' | 'gestao-acessos' | 'politicas'
  | 'cartilha' | 'nao-encontrada';

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
  { secao: 'mapas', caminho: '/mapas', rotulo: 'Mapas', inicial: 'MP', fase: 6, descricao: 'Árvores de negócio e técnica, vizinhança e análise de consumidores.' },

  { secao: 'mesa', caminho: '/mesa', rotulo: 'Minha mesa', inicial: 'MM', grupo: 'Trabalhar', fase: 4, descricao: 'O que é seu: rascunhos, validações assumidas e pendências.' },
  { secao: 'novo', caminho: '/novo', rotulo: 'Novo ativo', inicial: 'NA', grupo: 'Trabalhar', descricao: 'Cadastro de um ativo em rascunho.' },
  { secao: 'descobertas', caminho: '/descobertas', rotulo: 'Descobertas', inicial: 'DE', grupo: 'Trabalhar', fase: 9, descricao: 'Importação de OpenAPI e inventário Git, com prévia e triagem.' },
  { secao: 'validacoes', caminho: '/validacoes', rotulo: 'Validações', inicial: 'VL', grupo: 'Trabalhar', descricao: 'Fila de etapas de validação aguardando decisão.' },

  { secao: 'notificacoes', caminho: '/notificacoes', rotulo: 'Notificações', inicial: 'NT', grupo: 'Você', fase: 8, descricao: 'Caixa de avisos de submissão, decisão e SLA.' },
  { secao: 'perfil', caminho: '/perfil', rotulo: 'Seu perfil', inicial: 'PF', grupo: 'Você', descricao: 'Quem você é na ferramenta e o que o seu papel permite.' },

  { secao: 'pleitos', caminho: '/pleitos', rotulo: 'Pleitos de acesso', inicial: 'PA', grupo: 'Administração', exige: 'conceder', fase: 2, descricao: 'Fila de pedidos de papel para quem concede acesso.' },
  { secao: 'squads', caminho: '/squads', rotulo: 'Squads', inicial: 'SQ', grupo: 'Administração', exige: 'conceder', fase: 2, descricao: 'Times e o escopo de papel por squad.' },
  { secao: 'gestao-acessos', caminho: '/gestao-acessos', rotulo: 'Gestão de acessos', inicial: 'GA', grupo: 'Administração', exige: 'administrar', fase: 2, descricao: 'Concessões e revogações de papel em toda a empresa.' },
  { secao: 'politicas', caminho: '/politicas', rotulo: 'Políticas', inicial: 'PO', grupo: 'Administração', fase: 4, descricao: 'Rito de validação por tipo e criticidade: etapas, evidência, score e SLA.' },
];

export const CARTILHA: ItemDeMenu = {
  secao: 'cartilha', caminho: '/cartilha', rotulo: 'Cartilha', inicial: '?', descricao: 'O que cada papel faz na ferramenta.',
};

// Portado de acesso.PERMISSOES (ações "conceder" e "administrar"). Fonte de
// verdade continua no servidor: esconder o item é conforto, não autorização.
const QUEM_CONCEDE: Papel[] = ['admin', 'curador'];
const QUEM_ADMINISTRA: Papel[] = ['admin'];

export function itensVisiveis(papel: Papel): ItemDeMenu[] {
  return MENU.filter((item) =>
    item.exige === 'conceder' ? QUEM_CONCEDE.includes(papel)
      : item.exige === 'administrar' ? QUEM_ADMINISTRA.includes(papel)
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
  return MENU.find((item) => item.caminho === limpo)?.secao ?? 'nao-encontrada';
}

export function itemDaSecao(secao: Secao): ItemDeMenu | undefined {
  return secao === 'cartilha' ? CARTILHA : MENU.find((item) => item.secao === secao);
}
