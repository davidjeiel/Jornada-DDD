// Equivalentes de tests/test_acesso.py e tests/test_cartilha.py do painel-ddd
// que leem o CSS e o HTML de partida, como os originais.
import { describe, expect, it } from 'vitest';
import css from './styles.css?raw';
import indexHtml from '../index.html?raw';
import { CHAVE_MENU, CHAVE_TEMA } from './preferencias';

const semComentarios = css.replace(/\/\*[\s\S]*?\*\//g, '');

// As três declarações de tokens: claro, escuro forçado e escuro do sistema.
const BLOCO_CLARO = /:root\s*\{([^}]*)\}/;
const BLOCO_ESCURO = /:root\[data-theme="dark"\]\s*\{([^}]*)\}/;
const BLOCO_SISTEMA = /@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme="light"\]\)\s*\{([^}]*)\}\s*\}/;

function variaveis(bloco: RegExp): Set<string> {
  const corpo = semComentarios.match(bloco)?.[1] ?? '';
  return new Set([...corpo.matchAll(/(--[a-z0-9-]+)\s*:/g)].map((m) => m[1]));
}

function regras(seletor: string): string {
  const escapado = seletor.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return [...semComentarios.matchAll(new RegExp(`(?:^|\\}|,)\\s*${escapado}\\s*(?:,[^{]*)?\\{([^}]*)\\}`, 'gm'))].map((m) => m[1]).join(';');
}

describe('paleta', () => {
  it('é toda por token: nenhuma cor fixa fora das declarações (test_paleta_e_toda_por_token)', () => {
    const foraDosTokens = semComentarios
      .replace(BLOCO_SISTEMA, '')
      .replace(BLOCO_ESCURO, '')
      .replace(BLOCO_CLARO, '');
    expect(foraDosTokens).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(foraDosTokens).not.toMatch(/\b(rgba?|hsla?)\(/);
    expect(foraDosTokens).not.toMatch(/:\s*(white|black)\b/);
  });

  it('todo token do tema claro existe nos dois escuros', () => {
    const claro = variaveis(BLOCO_CLARO);
    expect(claro.size).toBeGreaterThan(20);
    expect([...claro].filter((v) => !variaveis(BLOCO_ESCURO).has(v))).toEqual([]);
    expect([...claro].filter((v) => !variaveis(BLOCO_SISTEMA).has(v))).toEqual([]);
  });

  it('criticidade baixa e média têm estilos próprios (test_criticidade_media_e_baixa_tem_estilos_proprios)', () => {
    for (const nivel of ['baixa', 'media', 'alta', 'critica']) {
      expect(regras(`.critical-${nivel}`), nivel).toMatch(/color:\s*var\(--/);
    }
    expect(regras('.critical-media')).not.toEqual(regras('.critical-baixa'));
  });
});

describe('menu recolhido', () => {
  it('esconde o rótulo e mantém a inicial (test_menu_compacto_esconde_rotulo_e_mostra_inicial)', () => {
    expect(semComentarios).toMatch(/\[data-menu="compacto"\] \.menu \.rotulo[^{]*\{[^}]*display:\s*none/);
    expect(semComentarios).toMatch(/\[data-menu="compacto"\] \.menu \.inicial\s*\{[^}]*display:\s*flex/);
    // Aberto, só o rótulo: a inicial é do menu estreito, como no painel.
    expect(semComentarios).toMatch(/(^|\})\s*\.menu \.inicial\s*\{[^}]*display:\s*none/m);
    expect(semComentarios).toMatch(/\[data-menu="compacto"\] \.menu\s*\{[^}]*flex-basis:\s*68px/);
  });
});

describe('cartilha', () => {
  it('rodapé e atalho têm estilo próprio (test_rodape_e_atalho_tem_estilo_proprio)', () => {
    expect(regras('.rodape')).not.toBe('');
    expect(semComentarios).toMatch(/\.cartilha-atalho[^{]*\{/);
    expect(semComentarios).toMatch(/\.cartilha-atalho\.ativo[^{]*\{/);
  });
});

describe('página de partida', () => {
  it('aplica tema e menu antes da pintura (test_preferencia_aplicada_antes_da_pintura)', () => {
    const cabeca = indexHtml.slice(0, indexHtml.indexOf('</head>'));
    const script = cabeca.indexOf('<script>');
    expect(script).toBeGreaterThan(-1);
    expect(cabeca).toContain(`'${CHAVE_TEMA}'`);
    expect(cabeca).toContain(`'${CHAVE_MENU}'`);
    // O app (e com ele o CSS) só entra depois, no fim do body.
    expect(indexHtml.indexOf('type="module"')).toBeGreaterThan(indexHtml.indexOf('</head>'));
  });

  it('declara o idioma (parte de test_acessibilidade_do_esqueleto)', () => {
    expect(indexHtml).toMatch(/<html lang="pt-BR">/);
    expect(indexHtml).toMatch(/name="viewport"/);
  });
});
