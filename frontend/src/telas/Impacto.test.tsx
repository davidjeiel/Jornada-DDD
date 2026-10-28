// Equivalente de test_tela_mostra_quem_depende_antes_de_descontinuar
// (tests/test_escala.py do painel-ddd): a aba Relações mostra quem depende do
// ativo e, dentro do cartão de descontinuação, quem seria afetado.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Impacto } from '../api';
import { CartaoDeDescontinuacao, QuemDepende, podeDescontinuar, podeSerArquivado, podeSerDescontinuado } from './Impacto';

const comCritico: Impacto = {
  id: 'cap',
  codigo: 'CAP-0001',
  total: 1,
  criticos: 1,
  exigeConfirmacao: true,
  consumidores: [{ id: 'app', codigo: 'APP-0001', nome: 'Gestor', tipoItem: 'aplicacao', tipoRelacao: 'implementa', criticidade: 'critica' }],
  porCriticidade: { critica: 1 },
  porTipo: { aplicacao: 1 },
};

const semConsumidor: Impacto = { ...comCritico, total: 0, criticos: 0, exigeConfirmacao: false, consumidores: [], porCriticidade: {}, porTipo: {} };

const nada = async () => {};

describe('quem depende deste ativo', () => {
  it('mostra os consumidores e quantos são de alta criticidade (test_tela_mostra_quem_depende_antes_de_descontinuar)', () => {
    const html = renderToStaticMarkup(<QuemDepende impacto={comCritico} />);
    expect(html).toContain('Quem depende deste ativo');
    expect(html).toContain('1 de alta criticidade');
    expect(html).toContain('APP-0001 · Gestor');

    const cartao = renderToStaticMarkup(<CartaoDeDescontinuacao impacto={comCritico} onDescontinuar={nada} />);
    expect(cartao).toContain('Ver quem seria afetado');
  });

  it('sem consumidor, diz isso e não pede o código', () => {
    expect(renderToStaticMarkup(<QuemDepende impacto={semConsumidor} />)).toContain('Nenhum ativo em vigor depende deste.');
    const cartao = renderToStaticMarkup(<CartaoDeDescontinuacao impacto={semConsumidor} onDescontinuar={nada} />);
    expect(cartao).not.toContain('name="confirmacao"');
    expect(cartao).not.toContain('Ver quem seria afetado');
  });

  it('com consumidor crítico, pede o código do ativo e só libera com ele (test_descontinuar_com_consumidor_critico_exige_o_codigo)', () => {
    const cartao = renderToStaticMarkup(<CartaoDeDescontinuacao impacto={comCritico} onDescontinuar={nada} />);
    expect(cartao).toContain('name="confirmacao"');
    expect(cartao).toContain('<strong>CAP-0001</strong>');
    expect(cartao).toMatch(/<button class="danger-button" disabled="">/);
  });
});

describe('quem e o quê se descontinua', () => {
  it('segue a permissão e a máquina de estados da API', () => {
    expect(['admin', 'curador', 'arquiteto'].every((p) => podeDescontinuar(p as never))).toBe(true);
    expect(podeDescontinuar('negocio')).toBe(false);
    expect(podeDescontinuar('techlead')).toBe(false);
    expect(podeSerDescontinuado('publicado') && podeSerDescontinuado('em_revisao')).toBe(true);
    expect(podeSerDescontinuado('rascunho')).toBe(false);
    expect(podeSerArquivado('rascunho') && podeSerArquivado('descontinuado')).toBe(true);
    expect(podeSerArquivado('publicado')).toBe(false);
  });
});
