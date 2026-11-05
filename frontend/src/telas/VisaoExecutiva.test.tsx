// Equivalentes de tests/test_jornada.py (visão executiva) do painel-ddd.
// O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Indicadores } from '../api';
import { CartoesDosIndicadores, SerieHistorica, leituraDaVariacao } from './VisaoExecutiva';

const base: Indicadores = {
  atuais: { dominios: 2, capacidades: 3, cobertura: 55, sem_owner: 4, apis_governadas: 100, pendencias: 1 },
  variacao: {},
  quantoMenosMelhor: ['dependencias_criticas', 'pendencias', 'sem_owner', 'sla_vencido'],
  serie: [],
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
});
