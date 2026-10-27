// Equivalentes de tests/test_acesso.py do painel-ddd (tema e menu recolhível).
import { describe, expect, it } from 'vitest';
import {
  CHAVE_MENU, CHAVE_TEMA, ESTADOS_DE_TEMA, gravarMenuCompacto, gravarTema, lerMenuCompacto, lerTema, proximoTema,
} from './preferencias';

function memoria() {
  const dados = new Map<string, string>();
  return {
    dados,
    getItem: (chave: string) => dados.get(chave) ?? null,
    setItem: (chave: string, valor: string) => void dados.set(chave, valor),
    removeItem: (chave: string) => void dados.delete(chave),
  };
}

const bloqueado = {
  getItem: () => { throw new Error('bloqueado'); },
  setItem: () => { throw new Error('bloqueado'); },
  removeItem: () => { throw new Error('bloqueado'); },
};

describe('tema', () => {
  it('tem os três estados: sistema, claro e escuro (test_tema_noturno_tem_os_tres_estados)', () => {
    expect(ESTADOS_DE_TEMA.map((estado) => estado.tema)).toEqual(['sistema', 'light', 'dark']);
    expect(proximoTema('sistema')).toBe('light');
    expect(proximoTema('light')).toBe('dark');
    expect(proximoTema('dark')).toBe('sistema');
  });

  it('"sistema" não guarda nada: quem decide é o navegador', () => {
    const armazenamento = memoria();
    gravarTema(armazenamento, 'dark');
    expect(armazenamento.dados.get(CHAVE_TEMA)).toBe('dark');
    expect(lerTema(armazenamento)).toBe('dark');

    gravarTema(armazenamento, 'sistema');
    expect(armazenamento.dados.has(CHAVE_TEMA)).toBe(false);
    expect(lerTema(armazenamento)).toBe('sistema');
  });

  it('valor estranho no storage cai no tema do sistema', () => {
    const armazenamento = memoria();
    armazenamento.setItem(CHAVE_TEMA, 'roxo');
    expect(lerTema(armazenamento)).toBe('sistema');
  });

  it('storage bloqueado não quebra a troca', () => {
    expect(() => gravarTema(bloqueado, 'dark')).not.toThrow();
    expect(lerTema(bloqueado)).toBe('sistema');
  });
});

describe('menu recolhido', () => {
  it('é lembrado e esquecido', () => {
    const armazenamento = memoria();
    expect(lerMenuCompacto(armazenamento)).toBe(false);
    gravarMenuCompacto(armazenamento, true);
    expect(armazenamento.dados.get(CHAVE_MENU)).toBe('compacto');
    expect(lerMenuCompacto(armazenamento)).toBe(true);
    gravarMenuCompacto(armazenamento, false);
    expect(armazenamento.dados.has(CHAVE_MENU)).toBe(false);
  });

  it('storage bloqueado não quebra o botão', () => {
    expect(() => gravarMenuCompacto(bloqueado, true)).not.toThrow();
    expect(lerMenuCompacto(bloqueado)).toBe(false);
  });
});
