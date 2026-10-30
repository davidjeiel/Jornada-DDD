// A aba Histórico (ADR-0017): revisões com hash, comparação e auditoria, como
// a aba Histórico da visão 360° do painel-ddd.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Comparacao, Historico as Dados } from '../api';
import { usuariosDeTeste } from '../usuarios';
import { Historico, detalheDaAuditoria, rotuloDaAcao } from './Historico';

const curadora = usuariosDeTeste.find((u) => u.papel === 'curador')!;
const hash = 'a'.repeat(64);

const dados: Dados = {
  revisoes: [
    { numero: 2, motivo: 'v2', autor: curadora.id, criadaEm: '2026-10-30T12:00:00Z', publicadaEm: null, hash: 'b'.repeat(64), integra: false },
    { numero: 1, motivo: 'primeira', autor: curadora.id, criadaEm: '2026-10-29T12:00:00Z', publicadaEm: '2026-10-29T13:00:00Z', hash, integra: true },
  ],
  auditoria: [
    { acao: 'alterar', pessoa: curadora.id, em: '2026-10-30T11:00:00Z', antes: { nome: 'Core' }, depois: { nome: 'Core 2' } },
    { acao: 'criar', pessoa: '99999999-0000-0000-0000-000000000000', em: '2026-10-29T10:00:00Z', antes: null, depois: { codigo: 'SIS-0001', nome: 'Core' } },
  ],
};

const nada = () => {};

describe('aba Histórico', () => {
  it('lista as revisões com o começo do hash e avisa quando ele não confere', () => {
    const html = renderToStaticMarkup(<Historico historico={dados} comparacao={null} onComparar={nada} />);
    expect(html).toContain('#2');
    expect(html).toContain(hash.slice(0, 12));
    expect(html).not.toContain(hash.slice(0, 13) + '<');
    expect(html).toContain('não confere');
    expect(html.match(/não confere/g)).toHaveLength(1);
    expect(html).toContain(curadora.nome);
    expect(html).toContain('não publicada');
  });

  it('oferece comparar as duas últimas e mostra o que mudou', () => {
    expect(renderToStaticMarkup(<Historico historico={dados} comparacao={null} onComparar={nada} />))
      .toContain('Comparar as duas últimas revisões');

    const comparacao: Comparacao = { a: 1, b: 2, diferencas: [{ campo: 'nome', rotulo: 'Nome', antes: 'Core', depois: 'Core 2' }] };
    const html = renderToStaticMarkup(<Historico historico={dados} comparacao={comparacao} onComparar={nada} />);
    expect(html).toContain('Revisão #1 vs #2');
    expect(html).toContain('<td class="antes">Core</td>');
    expect(html).toContain('<td class="depois">Core 2</td>');

    const igual = renderToStaticMarkup(<Historico historico={dados} comparacao={{ a: 1, b: 2, diferencas: [] }} onComparar={nada} />);
    expect(igual).toContain('Nenhuma diferença entre as revisões escolhidas.');
  });

  it('com uma revisão só, não há o que comparar', () => {
    const uma = { ...dados, revisoes: dados.revisoes.slice(1) };
    expect(renderToStaticMarkup(<Historico historico={uma} comparacao={null} onComparar={nada} />))
      .not.toContain('Comparar as duas últimas');
  });

  it('a auditoria diz quem fez o quê, com antes e depois na alteração', () => {
    const html = renderToStaticMarkup(<Historico historico={dados} comparacao={null} onComparar={nada} />);
    expect(html).toContain('Alterou');
    expect(html).toContain('Cadastrou');
    expect(html).toContain('pessoa 99999999');   // quem não é usuário de teste aparece pelo id
    expect(detalheDaAuditoria(dados.auditoria[0])).toBe('nome: Core → Core 2');
    expect(detalheDaAuditoria(dados.auditoria[1])).toBe('codigo: SIS-0001; nome: Core');
    expect(detalheDaAuditoria({ acao: 'submeter', pessoa: '', em: '', antes: null, depois: { revisao: '1', hash } }))
      .toBe(`revisao: 1; hash: ${hash.slice(0, 12)}`);
    expect(rotuloDaAcao('acao_nova')).toBe('acao nova');
  });
});
