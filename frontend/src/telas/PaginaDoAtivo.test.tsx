// Equivalentes de tests/test_fluxo.py, test_jornada.py e test_escala.py do
// painel-ddd para a página do ativo. O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { AtivoResumo, FichaDoAtivo, Metamodelo, PreCheck } from '../api';
import { secaoDoCaminho } from '../navegacao';
import {
  VisaoDoAtivo, abaDaUrl, caminhoDaAba, candidatosAoDestino, destinoDigitado, problemaDaRelacao, type Acoes, type Aba,
} from './PaginaDoAtivo';
import { SelecaoDoLote } from './RelacoesEmLote';

const nada = () => {};
const assincrono = async () => {};
const acoes: Acoes = { editar: nada, submeter: nada, abrirRevisao: nada, atribuir: nada, anexar: nada, relacionar: nada, comparar: nada, descontinuar: assincrono, arquivar: assincrono };

const ID = '0199a1b2-0000-7000-8000-000000000004';
const ativo = (id: string, codigo: string, nome: string, tipo: string): AtivoResumo => ({ id, codigo, nome, tipo, status: 'publicado', criticidade: 'media' });
const ativos = [
  ativo('d-1', 'DOM-0001', 'Contrato', 'dominio'), ativo('s-1', 'SUB-0001', 'Originação', 'subdominio'),
  ativo('c-1', 'CTX-0001', 'Simulação', 'contexto'), ativo(ID, 'CAP-0001', 'Simular Financiamento', 'capacidade'),
  ativo('a-1', 'API-0001', 'API Simulação', 'api'), ativo('e-1', 'END-0001', 'POST /simulacoes', 'endpoint'),
];

const metamodelo: Metamodelo = {
  tipos: [{ chave: 'capacidade', rotulo: 'Capacidade de negócio', bloco: 'Estrutura DDD', pai: 'contexto', campos: [{ nome: 'resultado_esperado', rotulo: 'Resultado esperado', tipo: 'area', obrigatorio: true, opcoes: [] }] }],
  relacoes: [{ chave: 'implementa', rotulo: 'implementa a capacidade', destinosSugeridos: ['capacidade'] },
    { chave: 'expoe', rotulo: 'expõe', destinosSugeridos: ['api', 'endpoint', 'evento'] }],
  mecanismos: [{ chave: 'sincrono', rotulo: 'Síncrono (HTTP/RPC)' }, { chave: 'assincrono', rotulo: 'Assíncrono (evento/fila)' }],
};

function ficha(status = 'rascunho'): FichaDoAtivo {
  return {
    id: ID, codigo: 'CAP-0001', nome: 'Simular Financiamento', tipoItem: 'capacidade', status, criticidade: 'alta',
    descricao: 'Calcula condições.', revisaoAtual: 1,
    trilha: [{ id: 'd-1', codigo: 'DOM-0001', nome: 'Contrato', tipoItem: 'dominio' }, { id: 's-1', codigo: 'SUB-0001', nome: 'Originação', tipoItem: 'subdominio' },
      { id: 'c-1', codigo: 'CTX-0001', nome: 'Simulação', tipoItem: 'contexto' }],
    atributos: { resultado_esperado: 'Proposta simulada' }, evidencias: [], responsaveis: [],
    relacoes: [{ direcao: 'saida', ativoId: 'a-1', codigo: 'API-0001', nome: 'API Simulação', tipo: 'expoe', criticidade: 'media', mecanismo: 'sincrono' }],
  };
}

const preCheck: PreCheck = {
  aprovado: false, score: 62, dimensoes: {}, bloqueios: [], alertas: [],
  politica: { scoreMinimo: 70, evidenciaMinima: 1, slaHoras: 48, etapas: ['negocial', 'tecnica'] },
  caminho: [
    { passo: 'descrever', rotulo: 'Descrever o ativo', concluido: true, impedimentos: [], pendencias: [] },
    { passo: 'responsaveis', rotulo: 'Definir responsáveis', concluido: false, impedimentos: ['owner'], pendencias: ['Falta o owner negocial.'] },
    { passo: 'conectar', rotulo: 'Conectar ao catálogo', concluido: true, impedimentos: [], pendencias: [] },
    { passo: 'evidenciar', rotulo: 'Anexar evidências', concluido: false, impedimentos: ['evidencia'], pendencias: ['Falta 1 evidência.'] },
    { passo: 'qualidade', rotulo: 'Atingir o score mínimo', concluido: false, impedimentos: ['score'], pendencias: ['Score 62 abaixo de 70.'] },
  ],
};

function pagina(aba: Aba = 'resumo', status = 'rascunho', podeEscrever = true) {
  return renderToStaticMarkup(
    <VisaoDoAtivo ficha={ficha(status)} preCheck={preCheck} aba={aba} ativos={ativos} pessoas={[]} metamodelo={metamodelo}
      historico={null} comparacao={null} impacto={null} podeEscrever={podeEscrever} podeDescontinuar={false} idPessoa={null}
      ocupado={false} acoes={acoes} onErro={nada} />,
  );
}

/** A abertura da seção da aba, para ver se está escondida. */
const secao = (html: string, aba: string) => html.match(new RegExp(`<section[^>]*id="painel-${aba}"[^>]*>`))?.[0] ?? '';

describe('visão 360° do ativo', () => {
  it('abre no resumo e esconde as outras abas (test_visao_360_abre_no_resumo_e_esconde_as_outras_abas)', () => {
    const html = pagina();
    expect(secao(html, 'resumo')).not.toContain('hidden');
    expect(secao(html, 'relacoes')).toContain('hidden');
    expect(secao(html, 'pessoas')).toContain('hidden');
  });

  it('a aba escolhida é a que abre (test_aba_escolhida_e_a_que_abre)', () => {
    const html = pagina('evidencias');
    expect(secao(html, 'evidencias')).not.toContain('hidden');
    expect(secao(html, 'resumo')).toContain('hidden');
    expect(html).toMatch(/<a[^>]*aria-selected="true"[^>]*>Evidências/);
  });

  it('aba desconhecida cai no resumo (test_aba_desconhecida_cai_no_resumo)', () => {
    expect(abaDaUrl('inventada')).toBe('resumo');
    expect(abaDaUrl(null)).toBe('resumo');
    expect(abaDaUrl('pessoas')).toBe('pessoas');
  });

  it('a escrita volta para a aba de origem (test_escrita_volta_para_a_aba_de_origem)', () => {
    expect(caminhoDaAba(ID, 'pessoas')).toBe(`/ativos/${ID}?aba=pessoas`);
    expect(caminhoDaAba(ID, 'resumo')).toBe(`/ativos/${ID}`);
  });

  it('dentro do ativo, o menu continua no catálogo (test_menu_continua_marcado_dentro_de_um_ativo)', () => {
    expect(secaoDoCaminho(`/ativos/${ID}`)).toBe('catalogo');
    expect(secaoDoCaminho(`/ativos/${ID}/relacoes-lote`)).toBe('catalogo');
  });

  it('mostra a trilha clicável, da raiz ao pai (test_tela_do_ativo_mostra_a_trilha_clicavel)', () => {
    const html = pagina();
    expect(html).toContain('aria-label="Trilha hierárquica"');
    for (const ancestral of ['d-1', 's-1', 'c-1']) expect(html).toContain(`href="/ativos/${ancestral}"`);
    expect(html.indexOf('Contrato')).toBeLessThan(html.indexOf('Originação'));
    expect(html.indexOf('Originação')).toBeLessThan(html.indexOf('Simulação'));
  });
});

describe('caminho até a publicação', () => {
  it('aparece no rascunho e some no publicado (test_checklist_aparece_no_rascunho_e_some_no_publicado)', () => {
    expect(pagina('resumo', 'rascunho')).toContain('Caminho até a publicação');
    expect(pagina('resumo', 'em_revisao')).toContain('Caminho até a publicação');
    expect(pagina('resumo', 'publicado')).not.toContain('Caminho até a publicação');
  });

  it('mostra os cinco passos, o que falta e a aba que resolve (test_checklist_reflete_o_que_falta)', () => {
    const html = pagina();
    expect(html).toContain('2 de 5');
    expect(html).toContain('Falta o owner negocial.');
    expect(html).toContain(`href="/ativos/${ID}?aba=pessoas"`);
    expect(html).toContain('mínimo de 70% · atual 62%');
  });
});

describe('relações', () => {
  it('o formulário oferece mecanismo e busca de destino (test_formulario_de_relacao_oferece_mecanismo)', () => {
    const html = pagina('relacoes');
    expect(html).toContain('name="mecanismo"');
    expect(html).toContain('Assíncrono (evento/fila)');
    expect(html).toContain('id="destino-busca"');
    expect(html).toContain('Síncrono (HTTP/RPC)'); // o mecanismo da relação existente, pelo nome
  });

  it('relação sem destino avisa em vez de enviar (test_relacao_sem_destino_avisa_em_vez_de_quebrar)', () => {
    expect(problemaDaRelacao('', undefined)).toBe('Escolha um ativo de destino.');
    expect(problemaDaRelacao('XYZ', undefined)).toContain('Escolha um da lista');
    const destino = destinoDigitado('api-0001', ativos);
    expect(destino?.id).toBe('a-1');
    expect(problemaDaRelacao('api-0001', destino)).toBeNull();
  });

  it('os destinos sugeridos filtram a busca, e "ver todos" tira o filtro (test_destinos_sugeridos_cobrem_todas_as_relacoes)', () => {
    expect(candidatosAoDestino(ativos, 'a-1', 'expoe', metamodelo).map((a) => a.codigo)).toEqual(['END-0001']);
    expect(candidatosAoDestino(ativos, 'a-1', 'implementa', metamodelo).map((a) => a.codigo)).toEqual(['CAP-0001']);
    expect(candidatosAoDestino(ativos, 'a-1', 'expoe', metamodelo, true)).toHaveLength(5);
  });

  it('sem papel de escrita, não há formulário de relação', () => {
    expect(pagina('relacoes', 'rascunho', false)).not.toContain('id="destino-busca"');
  });
});

describe('relações em lote', () => {
  const lote = () => renderToStaticMarkup(
    <SelecaoDoLote ficha={{ ...ficha(), id: 'a-0', relacoes: [{ direcao: 'saida', ativoId: 'a-1', codigo: 'API-0001', nome: 'API Simulação', tipo: 'expoe', criticidade: 'media', mecanismo: null }] }}
      tipo="expoe" ativos={ativos} metamodelo={metamodelo} ocupado={false} onTipo={nada} onEnviar={nada} />,
  );

  it('marca o que já existe, travado (test_tela_de_lote_marca_o_que_ja_existe)', () => {
    const html = lote();
    const travada = html.match(/<input[^>]*checked[^>]*>/)?.[0] ?? '';
    expect(travada).toContain('disabled');
    expect(html).toContain('já relacionado');
    expect(html).toContain('value="e-1"');      // o endpoint ainda pode ser marcado
    expect(html).not.toContain('value="a-1"');  // a API já relacionada não vai no envio
  });

  it('volta para a aba de relações depois de enviar (test_post_do_lote_volta_para_a_aba_de_relacoes)', () => {
    expect(caminhoDaAba('a-0', 'relacoes')).toBe('/ativos/a-0?aba=relacoes');
    expect(lote()).toContain('href="/ativos/a-0?aba=relacoes"');
  });
});
