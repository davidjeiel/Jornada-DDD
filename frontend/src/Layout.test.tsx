// Equivalentes de tests/test_acesso.py, test_jornada.py e test_cartilha.py do
// painel-ddd sobre o esqueleto da página: o que tem de estar em TODA tela.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { Layout } from './Layout';
import { MENU, type Secao } from './navegacao';
import { Cartilha } from './telas/Cartilha';
import { usuariosDeTeste, type UsuarioDeTeste } from './usuarios';

const curadora = usuariosDeTeste.find((u) => u.papel === 'curador')!;

function pagina(secao: Secao, opcoes: { usuario?: UsuarioDeTeste; compacto?: boolean } = {}) {
  return renderToStaticMarkup(
    <Layout
      secao={secao}
      usuario={opcoes.usuario ?? curadora}
      tema="sistema"
      onAlternarTema={() => {}}
      menuCompacto={opcoes.compacto ?? false}
      onAlternarMenu={() => {}}
      termo=""
      onBuscar={() => {}}
    >
      <p>conteúdo</p>
    </Layout>,
  );
}

const TODAS: Secao[] = [...MENU.map((item) => item.secao), 'cartilha'];

/** Existe uma tag com TODOS estes atributos, em qualquer ordem? */
function temTag(html: string, tag: string, ...atributos: string[]): boolean {
  return [...html.matchAll(new RegExp(`<${tag}\\b[^>]*>`, 'g'))]
    .some(([abertura]) => atributos.every((atributo) => abertura.includes(atributo)));
}

describe('esqueleto da página', () => {
  it('é acessível: pular para o conteúdo, regiões nomeadas, seção marcada (test_acessibilidade_do_esqueleto)', () => {
    const html = pagina('catalogo');
    expect(html.indexOf('<a class="pular"')).toBe(html.indexOf('<a'));
    expect(html).toContain('href="#conteudo"');
    expect(temTag(html, 'main', 'id="conteudo"')).toBe(true);
    expect(temTag(html, 'nav', 'aria-label="Navegação principal"')).toBe(true);
    expect(temTag(html, 'form', 'role="search"')).toBe(true);
    expect(temTag(html, 'input', 'name="termo"', 'aria-label="Buscar em todo o catálogo"')).toBe(true);
    expect(html.match(/aria-current="page"/g)).toHaveLength(1);
    expect(temTag(html, 'a', 'href="/catalogo"', 'aria-current="page"')).toBe(true);
  });

  it('o menu recolhível tem controle e iniciais (test_menu_recolhivel_tem_controle_e_iniciais)', () => {
    const aberto = pagina('painel');
    expect(temTag(aberto, 'button', 'class="recolher"', 'aria-controls="menu-lateral"', 'aria-expanded="true"')).toBe(true);
    expect(aberto).toContain('Recolher o menu lateral');
    for (const item of MENU.filter((i) => !i.exige)) {
      expect(aberto).toContain(`<span class="inicial" aria-hidden="true">${item.inicial}</span>`);
    }
    const recolhido = pagina('painel', { compacto: true });
    expect(temTag(recolhido, 'button', 'class="recolher"', 'aria-expanded="false"')).toBe(true);
    expect(recolhido).toContain('Expandir o menu lateral');
  });

  it('o campo de busca existe em todas as telas (test_campo_de_busca_existe_em_todas_as_telas)', () => {
    for (const secao of TODAS) expect(pagina(secao), secao).toContain('name="termo"');
  });

  it('o rodapé leva à cartilha em qualquer tela (test_rodape_leva_a_cartilha_em_qualquer_tela)', () => {
    for (const secao of TODAS) expect(pagina(secao), secao).toMatch(/<footer[\s\S]*href="\/cartilha"[\s\S]*<\/footer>/);
  });

  it('o atalho da cartilha fica ao lado de quem você é (test_atalho_fica_ao_lado_do_botao_de_identificacao)', () => {
    const html = pagina('painel');
    const quem = html.indexOf('class="quem"');
    const atalho = html.indexOf('class="cartilha-atalho"');
    expect(quem).toBeGreaterThan(-1);
    expect(atalho).toBeGreaterThan(quem);
    // nada clicável entre os dois
    expect(html.slice(html.indexOf('</a>', quem) + 4, html.lastIndexOf('<a', atalho))).not.toMatch(/<(a|button)\b/);
  });

  it('o atalho se marca quando você está na cartilha (test_atalho_se_marca_quando_voce_esta_na_cartilha)', () => {
    expect(temTag(pagina('cartilha'), 'a', 'class="cartilha-atalho ativo"', 'aria-current="page"')).toBe(true);
    expect(pagina('painel')).not.toContain('cartilha-atalho ativo');
  });

  it('o sino leva às notificações e o nome leva ao perfil', () => {
    const html = pagina('painel');
    expect(temTag(html, 'a', 'class="sino"', 'href="/notificacoes"')).toBe(true);
    expect(temTag(html, 'a', 'class="quem"', 'href="/perfil"')).toBe(true);
    expect(html).toContain(curadora.nome);
  });

  it('o menu de administração depende do papel (test_menu_so_mostra_a_fila_para_quem_concede)', () => {
    const negocio = usuariosDeTeste.find((u) => u.papel === 'negocio')!;
    expect(pagina('painel')).toContain('Pleitos de acesso');
    expect(pagina('painel', { usuario: negocio })).not.toContain('Pleitos de acesso');
    expect(pagina('painel', { usuario: negocio })).toContain('Políticas');
  });
});

describe('cartilha', () => {
  it('não escreve nada: nenhum formulário (test_cartilha_nao_escreve_nada)', () => {
    const html = renderToStaticMarkup(<Cartilha />);
    expect(html).toContain('Cartilha');
    expect(html).not.toMatch(/<(form|input|textarea|select)\b/);
  });
});
