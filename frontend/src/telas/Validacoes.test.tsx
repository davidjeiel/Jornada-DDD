// Equivalentes de tests/test_fluxo.py do painel-ddd para a fila de validação,
// o painel do validador e a mesa. O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { FichaDoAtivo, ItemDaFila, Mesa, PainelDoValidador as Painel, Validacao } from '../api';
import { MENU, secaoDoCaminho } from '../navegacao';
import { QuadroDaMesa } from './Mesa';
import { ListaDaFila, PainelDoValidador, caminhoDaFila, destinoDepoisDeDecidir } from './Validacoes';

const nada = () => {};

function validacao(id: string, etapa = 'tecnica', atribuidaA: string | null = null): Validacao {
  return { id, ativo: `a-${id}`, revisao: 1, etapa, status: 'aberta', submetidaPor: 's-1', decididaPor: null, motivo: null, abertaEm: null, prazo: null, atribuidaA };
}
const item = (id: string, etapa = 'tecnica', atribuidaA: string | null = null): ItemDaFila => ({
  validacao: validacao(id, etapa, atribuidaA), slaRestante: '47h',
  ativo: { id: `a-${id}`, codigo: `SIS-000${id}`, nome: `Sistema ${id}`, tipo: 'sistema', status: 'em_validacao', criticidade: 'alta' },
});

const ficha: FichaDoAtivo = {
  id: 'a-1', codigo: 'CAP-0001', nome: 'Simular Financiamento', tipoItem: 'capacidade', status: 'em_validacao', criticidade: 'alta',
  descricao: 'Calcula condições.', revisaoAtual: 2, idSquad: null, trilha: [], atributos: {},
  evidencias: [{ id: 'e-1', tipo: 'documento', titulo: 'Política de crédito', url: null }],
  responsaveis: [{ idPessoa: '50000000-0000-0000-0000-000000000001', papel: 'OwnerNegocial', inicioVigencia: '2026-10-01', fimVigencia: null, vigente: true }],
  relacoes: [], filhos: [],
};
const preCheck = {
  aprovado: true, score: 90, dimensoes: {}, bloqueios: [], alertas: ['Relação com ativo fora de vigência.'],
  politica: { scoreMinimo: 70, evidenciaMinima: 1, slaHoras: 48, etapas: ['negocial'], revisarACadaDias: 180 }, caminho: [], proximaRevisao: '2027-03-21',
};

function painel(dados: Partial<Painel> = {}, idPessoa: string | null = 'p-1', podeDecidir = true) {
  const p: Painel = { validacao: validacao('1'), ficha, preCheck, mudancas: null, primeiraRevisao: true, ...dados };
  return renderToStaticMarkup(<PainelDoValidador painel={p} idPessoa={idPessoa} podeDecidir={podeDecidir} ocupado={false} onAssumir={nada} onLiberar={nada} onDecidir={nada} />);
}

describe('painel do validador', () => {
  it('mostra evidências e responsáveis, e avisa a primeira revisão (test_painel_do_validador_mostra_evidencias_e_responsaveis)', () => {
    const html = painel();
    expect(html).toContain('Política de crédito');
    expect(html).toContain('Eduardo Melo');
    expect(html).toContain('Primeira revisão deste ativo');
  });

  it('mostra o que mudou na segunda revisão (test_painel_mostra_o_que_mudou_na_segunda_revisao)', () => {
    const html = painel({ primeiraRevisao: false, validacao: { ...validacao('1'), revisao: 2 },
      mudancas: { a: 1, b: 2, diferencas: [{ campo: 'descricao', rotulo: 'Descrição', antes: 'Calcula condições', depois: 'Calcula condições e taxas' }] } });
    expect(html).toContain('O que mudou');
    expect(html).toContain('revisão #2');
    expect(html).toContain('Calcula condições e taxas');
    expect(html).toContain('class="antes"');
    expect(html).toContain('Descrição');
    expect(html).not.toContain('revisaoAtual');
  });

  it('livre oferece assumir; minha oferece devolver; de outra pessoa diz com quem está (test_assumir_e_liberar_uma_validacao)', () => {
    expect(painel()).toContain('Assumir a análise');
    expect(painel({ validacao: validacao('1', 'tecnica', 'p-1') })).toContain('Devolver à fila livre');
    const deOutra = painel({ validacao: validacao('1', 'tecnica', '50000000-0000-0000-0000-000000000001') });
    expect(deOutra).toContain('Em análise por Eduardo Melo');
    expect(deOutra).not.toContain('Assumir a análise');
  });

  it('sem poder decidir, não há formulário de decisão', () => {
    expect(painel({}, 'p-1', false)).not.toMatch(/<form\b/);
  });
});

describe('fila de validação', () => {
  const fila = { itens: [item('1'), item('2'), item('3', 'tecnica', 'p-9')], contagem: { tecnica: 3, negocial: 1, minhas: 0 } };

  it('as abas mostram a contagem (test_abas_da_fila_mostram_a_contagem)', () => {
    const html = renderToStaticMarkup(<ListaDaFila fila={fila} aba="tecnica" selecionada={null} />);
    expect(html).toContain('class="conta"');
    expect(html).toMatch(/Todas <span class="conta">4<\/span>/);
    expect(html).toMatch(/Técnica <span class="conta">3<\/span>/);
  });

  it('decidir e seguir vai para a próxima da mesma aba (test_decidir_preserva_a_etapa_e_segue_para_a_proxima)', () => {
    const destino = destinoDepoisDeDecidir(fila.itens, 'tecnica', '1', true);
    expect(destino).toContain('etapa=tecnica');
    expect(destino).toContain('validacao=2');
  });

  it('decidir sem seguir não seleciona ninguém (test_decidir_sem_seguir_nao_seleciona_ninguem)', () => {
    const destino = destinoDepoisDeDecidir(fila.itens, 'tecnica', '1', false);
    expect(destino).not.toContain('validacao');
    expect(destino).toContain('etapa=tecnica');
    expect(caminhoDaFila('')).toBe('/validacoes');
  });
});

describe('minha mesa', () => {
  it('separa o que é meu (test_minha_mesa_separa_o_que_e_meu)', () => {
    const mesa: Mesa = { minhasValidacoes: [item('1', 'tecnica', 'p-1')], filaLivre: [item('2')], meusRascunhos: [], aguardandoDecisao: [{
      ativo: item('3').ativo, origem: 'manual', idSquad: 'sq-1', temResponsavel: true, atualizadoEm: '2026-11-05T12:00:00Z', pai: null, score: 72, criadoPor: 'p-1',
    }] };
    const html = renderToStaticMarkup(<QuadroDaMesa mesa={mesa} />);
    expect(html).toContain('Minhas validações (1)');
    expect(html).toContain('Fila livre (1)');
    expect(html).toContain('Nada cadastrado por você em rascunho ou revisão.');
    expect(html).toContain('Aguardando decisão (1)');
    expect(html).toContain('<span class="score medio">72%</span>');   // a qualidade, como a coluna do painel
  });

  it('a tela da mesa entra no menu (test_tela_da_mesa_responde_e_entra_no_menu)', () => {
    const mesa = MENU.find((m) => m.secao === 'mesa')!;
    expect(mesa.fase).toBeUndefined();
    expect(secaoDoCaminho(mesa.caminho)).toBe('mesa');
    expect(MENU.find((m) => m.secao === 'politicas')?.fase).toBeUndefined();
  });

});

describe('checklist de governança', () => {
  it('o painel do validador mostra o checklist de governança', () => {
    const html = painel();
    expect(html).toContain('Checklist de governança');
    expect(html).toContain('<li class="pendente">Relação com ativo fora de vigência.</li>');
    expect(html).toContain('Pré-check sem bloqueios (score 90%)');
    const bloqueado = painel({ preCheck: { ...preCheck, aprovado: false, bloqueios: [{ codigo: 'owner', mensagem: 'Falta o owner técnico.' }] } });
    expect(bloqueado).toContain('<li class="bloqueio">Falta o owner técnico.</li>');
    expect(bloqueado).not.toContain('Pré-check sem bloqueios');
  });
});

