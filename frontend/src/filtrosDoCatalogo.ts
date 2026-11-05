// Os filtros do catálogo: o que vai na URL, o que fica guardado para a visita
// seguinte e os chips de filtro ativo. Portado de web._filtros_do_pedido e
// web._chips do painel-ddd, com o navegador no lugar da sessão do servidor.
import type { Squad } from './api';

/** Os filtros do recorte, na ordem e com o rótulo dos chips (web.FILTROS_CATALOGO). */
export const FILTROS = [
  { chave: 'termo', rotulo: 'Busca' },
  { chave: 'tipo', rotulo: 'Tipo' },
  { chave: 'status', rotulo: 'Status' },
  { chave: 'squad', rotulo: 'Squad' },
  { chave: 'criticidade', rotulo: 'Criticidade' },
  { chave: 'semOwner', rotulo: 'Sem responsável' },
  { chave: 'origem', rotulo: 'Origem do cadastro' },
] as const;

/** Além do recorte, o que também volta na visita seguinte. A página não: volta à primeira. */
const DA_VISAO = ['ordenar', 'desc'] as const;

export const ORDENACOES = [
  { chave: 'tipo', rotulo: 'Tipo' },
  { chave: 'nome', rotulo: 'Nome' },
  { chave: 'codigo', rotulo: 'Código' },
  { chave: 'status', rotulo: 'Status' },
  { chave: 'criticidade', rotulo: 'Criticidade' },
  { chave: 'atualizado', rotulo: 'Última alteração' },
];

const CHAVE = 'jornada-ddd:filtros-catalogo';
const CONHECIDAS: string[] = [...FILTROS.map((f) => f.chave), ...DA_VISAO, 'pagina'];

type Armazenamento = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

function guardaveis(busca: URLSearchParams): URLSearchParams {
  const saida = new URLSearchParams();
  for (const chave of [...FILTROS.map((f) => f.chave), ...DA_VISAO]) {
    const valor = busca.get(chave);
    if (valor) saida.set(chave, valor);
  }
  return saida;
}

/**
 * O recorte que vale nesta visita. Com algum filtro na URL, é ele, e fica
 * guardado. Sem nenhum, é o da visita anterior (test_filtros_voltam_na_visita_seguinte).
 * Com <c>limpar</c>, zera o guardado (test_limpar_zera_os_filtros_guardados).
 */
export function filtrosEfetivos(busca: URLSearchParams, armazenamento: Armazenamento | null): URLSearchParams {
  try {
    if (busca.has('limpar')) {
      armazenamento?.removeItem(CHAVE);
      return new URLSearchParams();
    }
    if (CONHECIDAS.some((chave) => busca.has(chave))) {
      const recorte = guardaveis(busca);
      armazenamento?.setItem(CHAVE, recorte.toString());
      const pagina = busca.get('pagina');
      if (pagina) recorte.set('pagina', pagina);
      return recorte;
    }
    return new URLSearchParams(armazenamento?.getItem(CHAVE) ?? '');
  } catch {
    return guardaveis(busca);
  }
}

/** O endereço do catálogo com estes filtros; mudar um filtro volta para a primeira página. */
export function urlDoCatalogo(filtros: URLSearchParams, mudancas: Record<string, string | null> = {}, manterPagina = false): string {
  const proximos = new URLSearchParams(filtros);
  for (const [chave, valor] of Object.entries(mudancas)) {
    if (valor) proximos.set(chave, valor); else proximos.delete(chave);
  }
  if (!manterPagina && !('pagina' in mudancas)) proximos.delete('pagina');
  proximos.delete('ativo');
  const texto = proximos.toString();
  return `/catalogo${texto ? `?${texto}` : '?limpar=1'}`;
}

const STATUS: Record<string, string> = {
  rascunho: 'rascunho', em_validacao: 'em validação', publicado: 'publicado', em_revisao: 'em revisão', descontinuado: 'descontinuado', arquivado: 'arquivado',
};

export type Chip = { chave: string; rotulo: string; valor: string; semEle: string };

/** Um chip por filtro ativo, cada um com o endereço do catálogo sem ele (test_chip_remove_apenas_o_proprio_filtro). */
export function chipsDosFiltros(filtros: URLSearchParams, squads: Squad[], rotuloDoTipo: (chave: string) => string): Chip[] {
  return FILTROS.filter((f) => filtros.get(f.chave)).map((f) => {
    const bruto = filtros.get(f.chave)!;
    const valor = f.chave === 'tipo' ? rotuloDoTipo(bruto)
      : f.chave === 'status' ? STATUS[bruto] ?? bruto
        : f.chave === 'squad' ? squads.find((s) => s.id === bruto)?.sigla ?? bruto
          : f.chave === 'semOwner' ? 'sim'
            : f.chave === 'origem' ? (bruto === 'automatica' ? 'descoberta automática' : 'cadastro manual')
              : bruto;
    return { chave: f.chave, rotulo: f.rotulo, valor, semEle: urlDoCatalogo(filtros, { [f.chave]: null }) };
  });
}
