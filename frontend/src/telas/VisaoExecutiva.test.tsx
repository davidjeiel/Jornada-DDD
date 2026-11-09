// Equivalentes de tests/test_jornada.py (visão executiva) do painel-ddd.
// O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { GrafoDoCatalogo, Indicadores, ItemDaFila } from '../api';
import { posicionarEmCirculo } from '../grafo';
import {
  CartoesDosIndicadores, CoberturaPorDominio, ExposicaoDoCatalogo, FilaResumida, PendenciasPrioritarias, QualidadeCadastral, SerieHistorica,
  caminhoDaVisao, destinoDaPendencia, leituraDaVariacao, recorteDaUrl,
} from './VisaoExecutiva';

const base: Indicadores = {
  atuais: { dominios: 2, capacidades: 3, cobertura: 55, sem_owner: 4, apis_governadas: 100, pendencias: 1 },
  variacao: {},
  quantoMenosMelhor: ['dependencias_criticas', 'pendencias', 'sem_owner', 'sla_vencido'],
  serie: [],
  coberturaPorDominio: [],
  pendencias: [],
};

const ativo = (id: string, nome: string, tipo: string, criticidade = 'media') => ({ id, codigo: id.toUpperCase(), nome, tipo, status: 'publicado', criticidade });

const grafo: GrafoDoCatalogo = {
  nos: [ativo('dom', 'Contrato', 'dominio'), ativo('sub', 'Originação', 'subdominio'), ativo('app', 'Motor', 'aplicacao', 'critica')],
  arestas: [
    { origem: 'dom', destino: 'sub', tipo: 'hierarquia', criticidade: 'media', mecanismo: null },
    { origem: 'app', destino: 'sub', tipo: 'depende_de', criticidade: 'alta', mecanismo: null },
  ],
  filtrado: false,
  truncado: false,
  teto: 300,
  dominios: [{ id: 'dom', nome: 'Contrato', idPai: null }, { id: 'dom2', nome: 'Pagamentos', idPai: null }],
  subdominios: [{ id: 'sub', nome: 'Originação', idPai: 'dom' }, { id: 'sub2', nome: 'Cobrança', idPai: 'dom2' }],
};

describe('visão executiva', () => {
  it('os KPIs levam ao recorte (test_kpis_do_painel_levam_ao_recorte)', () => {
    const html = renderToStaticMarkup(<CartoesDosIndicadores indicadores={base} />);
    for (const destino of ['/catalogo?tipo=dominio', '/catalogo?semOwner=true&amp;status=publicado', '/catalogo?tipo=capacidade', '/catalogo?tipo=api'])
      expect(html).toContain(`href="${destino}"`);
  });

  it('queda de pendência é lida como melhora (test_queda_de_pendencia_e_lida_como_melhora)', () => {
    const html = renderToStaticMarkup(<CartoesDosIndicadores indicadores={{ ...base, variacao: { sem_owner: -5, cobertura: -15 } }} />);
    expect(html).toContain('class="delta bom"');    // menos órfãos, melhora
    expect(html).toContain('class="delta ruim"');   // menos cobertura, piora
    expect(html).toContain('5 no mês');             // 9 → 4, sem casa decimal supérflua
    expect(html).not.toContain('5,0');
  });

  it('a leitura da variação acompanha o sentido do indicador', () => {
    expect(leituraDaVariacao(12, false)).toEqual({ classe: 'bom', seta: '▲', texto: '12 no mês' });
    expect(leituraDaVariacao(2, true)?.classe).toBe('ruim');
    expect(leituraDaVariacao(0.5, false)?.texto).toBe('0,5 no mês');
    expect(leituraDaVariacao(0, false)).toBeNull();
    expect(leituraDaVariacao(undefined, true)).toBeNull();
  });

  it('a série mostra as fotografias da mais recente para a mais antiga, e avisa quando não há', () => {
    expect(renderToStaticMarkup(<SerieHistorica indicadores={base} />)).toContain('Nenhuma fotografia mensal ainda');
    const html = renderToStaticMarkup(<SerieHistorica indicadores={{ ...base, serie: [
      { competencia: '2026-07', valores: { cobertura: 60 }, geradaEm: '2026-07-31T00:00:00Z' },
      { competencia: '2026-08', valores: { cobertura: 72, sem_owner: 4 }, geradaEm: '2026-08-31T00:00:00Z' },
    ] }} />);
    expect(html.indexOf('2026-08')).toBeLessThan(html.indexOf('2026-07'));
    expect(html).toContain('72%');
  });

  it('a exposição do catálogo desenha os ativos, a hierarquia tracejada e as relações', () => {
    const html = renderToStaticMarkup(<ExposicaoDoCatalogo grafo={grafo} recorte={{}} onRecorte={() => {}} />);
    expect(html).toContain('Todos os ativos e suas relações');
    expect((html.match(/<circle/g) ?? []).length).toBe(3);
    expect(html).toContain('class="aresta-resumo hierarquia"');
    expect(html).toContain('class="aresta-resumo criticidade-alta"');
    expect(html).toContain('class="no-resumo criticidade-critica"');
    expect(html).toContain('href="/?item=app"');            // clicar num nó recorta por ele
    expect(html).toContain('Todos os domínios');
    expect(html).toContain('Aplicar filtros');
    expect(html).not.toContain('Limpar');
  });

  it('o seletor de subdomínio acompanha o domínio escolhido, e o recorte oferece limpar', () => {
    const html = renderToStaticMarkup(<ExposicaoDoCatalogo grafo={{ ...grafo, filtrado: true }} recorte={{ dominio: 'dom' }} onRecorte={() => {}} />);
    expect(html).toContain('Originação</option>');
    expect(html).not.toContain('Cobrança</option>');
    expect(html).toContain('Ativos e relações do recorte');
    expect(html).toContain('href="/"');
    expect(html).toContain('só a hierarquia aparece');
  });

  it('o recorte vazio e o corte pelo teto são ditos, não escondidos', () => {
    expect(renderToStaticMarkup(<ExposicaoDoCatalogo grafo={{ ...grafo, nos: [], arestas: [] }} recorte={{ item: 'x' }} onRecorte={() => {}} />))
      .toContain('Nenhum ativo neste recorte');
    expect(renderToStaticMarkup(<ExposicaoDoCatalogo grafo={{ ...grafo, truncado: true }} recorte={{}} onRecorte={() => {}} />))
      .toContain('passou de 300 ativos');
  });

  it('o recorte mora na URL', () => {
    expect(caminhoDaVisao({})).toBe('/');
    expect(caminhoDaVisao({ dominio: 'a', subdominio: null, item: 'b' })).toBe('/?dominio=a&item=b');
    expect(recorteDaUrl(new URLSearchParams('subdominio=s'))).toEqual({ dominio: null, subdominio: 's', item: null });
  });

  it('o círculo começa no topo e mantém a ordem', () => {
    const { nos } = posicionarEmCirculo(grafo.nos, grafo.arestas, 900, 520);
    expect(nos[0]).toMatchObject({ x: 450, y: 75 });
    expect(nos[1].x).toBeGreaterThan(450);
  });

  it('a cobertura por domínio mostra a barra e destaca a baixa', () => {
    const html = renderToStaticMarkup(<CoberturaPorDominio indicadores={{ ...base, coberturaPorDominio: [
      { id: 'd1', nome: 'Contrato', total: 4, cobertas: 3, percentual: 75 },
      { id: 'd2', nome: 'Pagamentos', total: 2, cobertas: 1, percentual: 50 },
    ] }} />);
    expect(html).toContain('3/4 · 75%');
    expect(html).toContain('href="/ativos/d1"');
    expect((html.match(/class="barra baixa"/g) ?? []).length).toBe(1);
    expect(renderToStaticMarkup(<CoberturaPorDominio indicadores={base} />)).toContain('Nenhum domínio');
  });

  it('cada pendência leva ao recorte que a explica', () => {
    const pendencias = [
      { chave: 'capacidade_sem_owner', titulo: 'Capacidade sem owner', total: 2, tipoItem: 'capacidade', semResponsavel: false, tela: null },
      { chave: 'repositorio_sem_mantenedor', titulo: 'Repositório sem mantenedor', total: 0, tipoItem: 'repositorio', semResponsavel: true, tela: null },
      { chave: 'sla_vencido', titulo: 'Validação com SLA vencido', total: 1, tipoItem: null, semResponsavel: false, tela: 'validacoes' },
      { chave: 'dependencia_critica_sem_evidencia', titulo: 'Dependência crítica sem evidência', total: 3, tipoItem: null, semResponsavel: false, tela: null },
    ];
    expect(pendencias.map(destinoDaPendencia)).toEqual(['/catalogo?tipo=capacidade', '/catalogo?tipo=repositorio&semOwner=true', '/validacoes', null]);
    const html = renderToStaticMarkup(<PendenciasPrioritarias indicadores={{ ...base, pendencias }} />);
    expect(html).toContain('<strong class="contagem bom">0</strong>');
    expect(html).toContain('<strong class="contagem ruim">2</strong>');
    expect(html).toContain('<td>Dependência crítica sem evidência</td>');
  });

  it('a fila resumida mostra as cinco primeiras com etapa e prazo', () => {
    const itens: ItemDaFila[] = Array.from({ length: 7 }, (_, i) => ({
      validacao: { id: `v${i}`, ativo: `a${i}`, revisao: 1, etapa: 'tecnica', status: 'aberta' } as ItemDaFila['validacao'],
      ativo: ativo(`a${i}`, `Ativo ${i}`, 'api'),
      slaRestante: '2h',
    }));
    const html = renderToStaticMarkup(<FilaResumida itens={itens} />);
    expect((html.match(/<li/g) ?? []).length).toBe(5);
    expect(html).toContain('tecnica · 2h');
    expect(renderToStaticMarkup(<FilaResumida itens={[]} />)).toContain('Sem validações pendentes');
  });

  it('a qualidade cadastral pinta pela faixa', () => {
    expect(renderToStaticMarkup(<QualidadeCadastral indicadores={{ ...base, atuais: { qualidade_media: 85 } }} />)).toContain('valor-grande bom');
    expect(renderToStaticMarkup(<QualidadeCadastral indicadores={{ ...base, atuais: { qualidade_media: 55 } }} />)).toContain('valor-grande risco');
  });
});
