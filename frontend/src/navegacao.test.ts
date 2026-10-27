// Equivalentes de tests/test_jornada.py e tests/test_acesso_pleito.py do
// painel-ddd para o menu. O nome do teste original vem no fim de cada título.
import { describe, expect, it } from 'vitest';
import { CARTILHA, MENU, itensVisiveis, secaoDoCaminho } from './navegacao';

describe('menu', () => {
  it('tem os itens e grupos do painel, na mesma ordem', () => {
    expect(MENU.map((item) => [item.grupo ?? '', item.rotulo])).toEqual([
      ['', 'Visão executiva'], ['', 'Catálogo de ativos'], ['', 'Mapas'],
      ['Trabalhar', 'Minha mesa'], ['Trabalhar', 'Novo ativo'], ['Trabalhar', 'Descobertas'], ['Trabalhar', 'Validações'],
      ['Você', 'Notificações'], ['Você', 'Seu perfil'],
      ['Administração', 'Pleitos de acesso'], ['Administração', 'Squads'], ['Administração', 'Gestão de acessos'], ['Administração', 'Políticas'],
    ]);
  });

  it('todo item tem uma inicial curta e única para o menu recolhido (test_menu_recolhivel_tem_controle_e_iniciais)', () => {
    const iniciais = MENU.map((item) => item.inicial);
    for (const inicial of iniciais) expect(inicial).toMatch(/^[A-Z]{2}$/);
    expect(new Set(iniciais).size).toBe(iniciais.length);
  });

  it('só mostra a fila de pleitos e os squads para quem concede (test_menu_so_mostra_a_fila_para_quem_concede)', () => {
    const rotulos = (papel: Parameters<typeof itensVisiveis>[0]) => itensVisiveis(papel).map((item) => item.secao);
    for (const papel of ['admin', 'curador'] as const) {
      expect(rotulos(papel)).toContain('pleitos');
      expect(rotulos(papel)).toContain('squads');
    }
    for (const papel of ['arquiteto', 'techlead', 'negocio', 'consulta'] as const) {
      expect(rotulos(papel)).not.toContain('pleitos');
      expect(rotulos(papel)).not.toContain('squads');
      expect(rotulos(papel)).not.toContain('gestao-acessos');
      expect(rotulos(papel)).toContain('politicas');
    }
    expect(rotulos('admin')).toContain('gestao-acessos');
    expect(rotulos('curador')).not.toContain('gestao-acessos');
  });
});

describe('seção marcada no menu', () => {
  it('é a da tela, com ou sem barra no fim', () => {
    expect(secaoDoCaminho('/')).toBe('painel');
    expect(secaoDoCaminho('/catalogo')).toBe('catalogo');
    expect(secaoDoCaminho('/validacoes/')).toBe('validacoes');
    expect(secaoDoCaminho(CARTILHA.caminho)).toBe('cartilha');
  });

  it('continua no catálogo dentro de um ativo (test_menu_continua_marcado_dentro_de_um_ativo)', () => {
    expect(secaoDoCaminho('/ativos/0199a1b2-0000-7000-8000-000000000001')).toBe('catalogo');
  });

  it('marca o cadastro na tela de novo ativo (test_menu_marca_o_cadastro_no_wizard)', () => {
    expect(secaoDoCaminho('/novo')).toBe('novo');
  });

  it('caminho desconhecido não marca nada', () => {
    expect(secaoDoCaminho('/nao-existe')).toBe('nao-encontrada');
  });
});
