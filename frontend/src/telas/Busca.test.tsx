// Equivalentes de tests/test_jornada.py e tests/test_escala.py do painel-ddd
// para o catálogo e a busca. O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { PaginaDoCatalogo, Squad } from '../api';
import { chipsDosFiltros, filtrosEfetivos, urlDoCatalogo } from '../filtrosDoCatalogo';
import { ListaDeSugestoes } from '../Layout';
import { metamodeloDeTeste } from '../testes/perfis';
import { ChipsDosFiltros, FormularioDeFiltros, ListaDoCatalogo } from './Catalogo';

const nada = () => {};

function memoria(inicial: Record<string, string> = {}) {
  const dados = new Map(Object.entries(inicial));
  return {
    getItem: (k: string) => dados.get(k) ?? null,
    setItem: (k: string, v: string) => void dados.set(k, v),
    removeItem: (k: string) => void dados.delete(k),
  };
}

const busca = (texto: string) => new URLSearchParams(texto);
const squads: Squad[] = [{ id: 'sq-1', sigla: 'CRED', titulo: 'Squad Crédito', descricao: 'x', ativa: true }];
const rotulo = (chave: string) => metamodeloDeTeste.tipos.find((t) => t.chave === chave)?.rotulo ?? chave;

function pagina(total: number, numero = 1): PaginaDoCatalogo {
  const porPagina = 50;
  const paginas = Math.max(1, Math.ceil(total / porPagina));
  const primeiro = total === 0 ? 0 : (numero - 1) * porPagina + 1;
  const ultimo = Math.min(numero * porPagina, total);
  return {
    total, pagina: numero, paginas, porPagina, primeiro, ultimo, ordem: 'tipo', descendente: false, truncado: false,
    itens: Array.from({ length: Math.max(0, ultimo - primeiro + 1) }, (_, i) => ({
      ativo: { id: `a-${i}`, codigo: `APP-${i}`, nome: `Aplicação ${i}`, tipo: 'aplicacao', status: 'rascunho', criticidade: 'media' },
      origem: 'manual', idSquad: null, temResponsavel: true, atualizadoEm: '2026-11-05T12:00:00Z',
    })),
  };
}

describe('filtros guardados', () => {
  it('voltam na visita seguinte (test_filtros_voltam_na_visita_seguinte)', () => {
    const fonte = memoria();
    filtrosEfetivos(busca('tipo=dominio'), fonte);
    const depois = filtrosEfetivos(busca(''), fonte);
    expect(depois.get('tipo')).toBe('dominio');
    const html = renderToStaticMarkup(<FormularioDeFiltros filtros={depois} metamodelo={metamodeloDeTeste} squads={squads} onMudar={nada} />);
    expect(html).toMatch(/<option value="dominio" selected="">/);
  });

  it('limpar zera o guardado (test_limpar_zera_os_filtros_guardados)', () => {
    const fonte = memoria();
    filtrosEfetivos(busca('tipo=dominio'), fonte);
    expect(filtrosEfetivos(busca('limpar=1'), fonte).toString()).toBe('');
    const depois = filtrosEfetivos(busca(''), fonte);
    expect(depois.toString()).toBe('');
    expect(renderToStaticMarkup(<ChipsDosFiltros chips={chipsDosFiltros(depois, squads, rotulo)} />)).not.toContain('Filtros ativos');
  });

  it('a página não fica guardada: a próxima visita volta à primeira', () => {
    const fonte = memoria();
    filtrosEfetivos(busca('tipo=dominio&pagina=3'), fonte);
    expect(filtrosEfetivos(busca(''), fonte).get('pagina')).toBeNull();
  });
});

describe('chips', () => {
  it('cada chip remove só o próprio filtro (test_chip_remove_apenas_o_proprio_filtro)', () => {
    const filtros = busca('tipo=dominio&criticidade=media');
    const chips = chipsDosFiltros(filtros, squads, rotulo);
    const html = renderToStaticMarkup(<ChipsDosFiltros chips={chips} />);
    expect(html).toContain('aria-label="Filtros ativos"');
    expect(chips.find((c) => c.chave === 'tipo')!.semEle).toBe('/catalogo?criticidade=media');
    expect(chips.find((c) => c.chave === 'criticidade')!.semEle).toBe('/catalogo?tipo=dominio');
    expect(html).toContain('Tipo: Domínio');
  });

  it('o filtro de origem aparece como descoberta automática (test_filtro_de_origem_separa_o_que_a_maquina_trouxe)', () => {
    const html = renderToStaticMarkup(<ChipsDosFiltros chips={chipsDosFiltros(busca('origem=automatica&squad=sq-1&semOwner=true'), squads, rotulo)} />);
    expect(html).toContain('descoberta automática');
    expect(html).toContain('Squad: CRED');
    expect(html).toContain('Sem responsável: sim');
  });

  it('mudar um filtro volta para a primeira página; sem nenhum, o link limpa', () => {
    expect(urlDoCatalogo(busca('tipo=api&pagina=3'), { status: 'publicado' })).toBe('/catalogo?tipo=api&status=publicado');
    expect(urlDoCatalogo(busca('tipo=api'), { tipo: null })).toBe('/catalogo?limpar=1');
  });
});

describe('lista do catálogo', () => {
  it('mostra o total real e a paginação (test_catalogo_mostra_total_e_paginacao)', () => {
    const html = renderToStaticMarkup(<ListaDoCatalogo pagina={pagina(57)} filtros={busca('tipo=aplicacao')} idSelecionado={null} />);
    expect(html).toContain('<strong>57</strong> ativo(s)');
    expect(html).toContain('Mostrando 1–50');
    expect(html).toContain('Página 1 de 2');
    expect(html).toContain('Próxima');
    expect(html).toContain('href="/catalogo?tipo=aplicacao&amp;pagina=2"');
    expect(html).not.toContain('Anterior');
  });

  it('a última página oferece voltar', () => {
    const html = renderToStaticMarkup(<ListaDoCatalogo pagina={pagina(57, 2)} filtros={busca('')} idSelecionado={null} />);
    expect(html).toContain('Mostrando 51–57');
    expect(html).toContain('Anterior');
    expect(html).not.toContain('>Próxima<');
  });
});

describe('busca do topo', () => {
  it('cada sugestão leva à página do ativo (test_busca_global_sugere_ativos_por_termo)', () => {
    const html = renderToStaticMarkup(<ListaDeSugestoes itens={[
      { id: 'c-1', codigo: 'CTX-0001', nome: 'Simulação', tipo: 'contexto', status: 'publicado', url: '/ativos/c-1' },
      { id: 'c-2', codigo: 'CAP-0001', nome: 'Simular Financiamento', tipo: 'capacidade', status: 'rascunho', url: '/ativos/c-2' },
    ]} onEscolher={nada} />);
    expect(html).toContain('href="/ativos/c-1"');
    expect(html).toContain('Simular Financiamento');
  });

  it('sem sugestões, não há lista (test_busca_global_ignora_termo_curto)', () => {
    expect(renderToStaticMarkup(<ListaDeSugestoes itens={[]} onEscolher={nada} />)).toBe('');
  });
});
