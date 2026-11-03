// A identidade do front no lugar do usuário de teste (ADR-0018).
import { describe, expect, it } from 'vitest';
import { cabecalhosDe, gravarIdentidade, identidadeInicial, lerIdentidade } from './identidade';

function memoria(inicial: Record<string, string> = {}) {
  const dados = new Map(Object.entries(inicial));
  return { getItem: (k: string) => dados.get(k) ?? null, setItem: (k: string, v: string) => void dados.set(k, v) };
}

describe('identidade', () => {
  it('não manda papel: o papel é o concedido à pessoa', () => {
    const cabecalhos = cabecalhosDe({ ...identidadeInicial, pessoaId: 'p-1' });
    expect(cabecalhos['X-Pessoa']).toBe('p-1');
    expect(cabecalhos).not.toHaveProperty('X-Papel');
    expect(cabecalhos).not.toHaveProperty('X-Modo');
  });

  it('sem pessoa não manda X-Pessoa, e o modo leitura vai no cabeçalho (test_modo_leitura_tambem_recusa_na_rota)', () => {
    const cabecalhos = cabecalhosDe({ ...identidadeInicial, modoLeitura: true });
    expect(cabecalhos).not.toHaveProperty('X-Pessoa');
    expect(cabecalhos['X-Modo']).toBe('leitura');
  });

  it('começa sem pessoa, e sobrevive a armazenamento corrompido', () => {
    expect(lerIdentidade(memoria()).pessoaId).toBeNull();
    expect(lerIdentidade(memoria({ 'jornada-ddd:identidade': '{quebrado' })).pessoaId).toBeNull();
  });

  it('aproveita o usuário de teste escolhido antes da fase 2', () => {
    const lida = lerIdentidade(memoria({ 'jornada-ddd:usuario-de-teste': '20000000-0000-0000-0000-000000000002' }));
    expect(lida).toEqual({ empresaId: '22222222-2222-2222-2222-222222222222', pessoaId: '20000000-0000-0000-0000-000000000002', modoLeitura: false });
  });

  it('o que se grava é o que se lê', () => {
    const fonte = memoria();
    gravarIdentidade({ empresaId: 'e-1', pessoaId: 'p-1', modoLeitura: true }, fonte);
    expect(lerIdentidade(fonte)).toEqual({ empresaId: 'e-1', pessoaId: 'p-1', modoLeitura: true });
  });
});
