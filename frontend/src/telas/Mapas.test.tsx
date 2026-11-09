// Equivalentes de tests/test_acesso.py (grafo) e tests/test_escala.py (mapas)
// do painel-ddd. O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { AtivoResumo, Impacto, NoDaArvore, Vizinhanca } from '../api';
import { posicionar } from '../grafo';
import { MENU } from '../navegacao';
import { AtivosNoGrafo, VisaoDoGrafo } from './Grafo';
import { QuemDepende } from './Impacto';
import { ArvoreDoCatalogo, Mapas, filtrarArvore } from './Mapas';

const nada = () => {};
const ativo = (id: string, nome: string, tipo = 'capacidade'): AtivoResumo => ({ id, codigo: id.toUpperCase(), nome, tipo, status: 'publicado', criticidade: 'media' });
const no = (a: AtivoResumo, filhos: NoDaArvore[] = [], implementacoes = 0): NoDaArvore => ({ ativo: a, implementacoes, filhos });

const negocio = [no(ativo('dom-1', 'Contrato', 'dominio'), [no(ativo('sub-1', 'Originação', 'subdominio'), [
  no(ativo('ctx-1', 'Simulação', 'contexto'), [no(ativo('cap-1', 'Simular Financiamento'), [], 1)]),
])])];

const vizinhanca: Vizinhanca = {
  centro: 'cap-1', saltos: 1, truncado: false, teto: 150,
  nos: [{ ativo: ativo('cap-1', 'Simular Financiamento'), salto: 0 }, { ativo: ativo('app-1', 'Motor', 'aplicacao'), salto: 1 }, { ativo: ativo('app-2', 'Portal', 'aplicacao'), salto: 1 }],
  arestas: [{ origem: 'app-1', destino: 'cap-1', tipo: 'implementa', criticidade: 'alta', mecanismo: null }],
};
const semConsumidor: Impacto = { id: 'cap-1', codigo: 'CAP-1', total: 0, criticos: 0, exigeConfirmacao: false, consumidores: [], porCriticidade: {}, porTipo: {} };

describe('grafo', () => {
  it('o layout põe o centro no meio e posiciona todos (test_layout_poe_o_centro_no_meio)', () => {
    const { nos, arestas } = posicionar(vizinhanca, 800, 600);
    const centro = nos.find((n) => n.salto === 0)!;
    expect([centro.x, centro.y]).toEqual([400, 300]);
    expect(nos.every((n) => Number.isFinite(n.x) && Number.isFinite(n.y))).toBe(true);
    expect(arestas.every((a) => [a.x1, a.y1, a.x2, a.y2].every(Number.isFinite))).toBe(true);
    expect(new Set(nos.map((n) => `${n.x},${n.y}`)).size).toBe(3);
  });

  it('a tela desenha o svg, mostra quem depende e avisa o corte (test_tela_do_grafo_responde_e_avisa_o_corte)', () => {
    const tela = (dados: Vizinhanca) => renderToStaticMarkup(
      <VisaoDoGrafo dados={dados} impacto={semConsumidor} metamodelo={null} tipos={[]} onSaltos={nada} onTipos={nada} />);
    const html = tela(vizinhanca);
    expect(html).toContain('<svg');
    expect(html).toContain('Quem depende deste ativo');
    expect(html).not.toContain('foi cortado');
    expect(tela({ ...vizinhanca, truncado: true, teto: 3 })).toContain('passou do teto de 3 ativos e foi cortado');
  });
});

describe('mapas', () => {
  it('a árvore de negócio desce até a capacidade, com as implementações (test_arvore_conta_implementacoes_da_capacidade)', () => {
    const html = renderToStaticMarkup(<ArvoreDoCatalogo raizes={negocio} filtro="" />);
    expect(html.indexOf('Contrato')).toBeLessThan(html.indexOf('Originação'));
    expect(html.indexOf('Simulação')).toBeLessThan(html.indexOf('Simular Financiamento'));
    expect(html).toContain('1 implementação(ões)');
    expect(html).toContain('href="/ativos/cap-1"');
  });

  it('a busca na árvore guarda o caminho até o que casou', () => {
    const filtrada = filtrarArvore(negocio, 'simular');
    expect(filtrada[0].ativo.nome).toBe('Contrato');
    expect(filtrada[0].filhos[0].filhos[0].filhos[0].ativo.nome).toBe('Simular Financiamento');
    expect(filtrarArvore(negocio, 'inexistente')).toEqual([]);
    expect(renderToStaticMarkup(<ArvoreDoCatalogo raizes={negocio} filtro="inexistente" />)).toContain('Nada na árvore casa');
  });

  it('o mapa tem as duas visões e a busca na árvore (test_mapa_tem_as_duas_visoes)', () => {
    expect(MENU.find((m) => m.secao === 'mapas')?.fase).toBeUndefined();
    const tecnica = renderToStaticMarkup(<Mapas visao="tecnica" onErro={nada} />);
    expect(tecnica).toContain('id="filtro-arvore"');
    expect(tecnica).toContain('href="/mapas"');
    const abaTecnica = tecnica.match(/<a[^>]*href="\/mapas\?visao=tecnica"[^>]*>/)?.[0] ?? '';
    expect(abaTecnica).toContain('aria-selected="true"');
  });

  it('ativo sem consumidor diz isso claramente (test_ativo_sem_consumidor_diz_isso_claramente)', () => {
    expect(renderToStaticMarkup(<QuemDepende impacto={semConsumidor} />)).toContain('Nenhum ativo em vigor depende deste.');
  });
});

describe('detalhes do mapa e do grafo', () => {
  it('a árvore vazia aponta por onde começar, em cada visão', () => {
    const negocio = renderToStaticMarkup(<ArvoreDoCatalogo raizes={[]} filtro="" visao="negocio" />);
    expect(negocio).toContain('href="/novo?tipo=dominio"');
    const tecnica = renderToStaticMarkup(<ArvoreDoCatalogo raizes={[]} filtro="" visao="tecnica" />);
    expect(tecnica).toContain('href="/novo?tipo=sistema"');
    expect(tecnica).toContain('href="/descobertas"');
  });

  it('expandir e recolher tudo mandam em todos os ramos', () => {
    const raizes = Array.from({ length: 4 }, (_, i) => no(ativo(`d-${i}`, `Domínio ${i}`, 'dominio'), [no(ativo(`s-${i}`, `Sub ${i}`, 'subdominio'))]));
    const abertos = (html: string) => (html.match(/<details open=""/g) ?? []).length;
    expect(abertos(renderToStaticMarkup(<ArvoreDoCatalogo raizes={raizes} filtro="" />))).toBe(0);   // mais de três raízes: fechada
    expect(abertos(renderToStaticMarkup(<ArvoreDoCatalogo raizes={raizes} filtro="" tudoAberto />))).toBe(4);
    expect(abertos(renderToStaticMarkup(<ArvoreDoCatalogo raizes={raizes.slice(0, 2)} filtro="" tudoAberto={false} />))).toBe(0);
    const tela = renderToStaticMarkup(<Mapas visao="negocio" onErro={nada} />);
    expect(tela).toContain('Expandir tudo');
    expect(tela).toContain('Recolher tudo');
  });

  it('o grafo traz os mesmos ativos em tabela, com a distância', () => {
    const html = renderToStaticMarkup(<AtivosNoGrafo dados={vizinhanca} />);
    expect(html).toContain('Ativos no grafo (3)');
    expect(html).toContain('<td>centro</td>');
    expect((html.match(/<td>1<\/td>/g) ?? []).length).toBe(2);
    expect(renderToStaticMarkup(<VisaoDoGrafo dados={vizinhanca} impacto={semConsumidor} metamodelo={null} tipos={[]} onSaltos={nada} onTipos={nada} />))
      .toContain('Ativos no grafo');
  });
});
