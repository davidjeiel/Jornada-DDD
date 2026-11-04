// Equivalentes de tests/test_cartilha.py do painel-ddd. O risco da cartilha é
// envelhecer: as tabelas da página vêm da API, que as lê das regras aplicadas.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import cartilhaMd from '../../../docs/CARTILHA.md?raw';
import type { DadosDaCartilha } from '../api';
import { PERFIS, PaginaDaCartilha, SECAO_DAS_TABELAS, dividirCartilhaEmSecoes } from './Cartilha';

const PAPEIS = ['negocio', 'techlead', 'arquiteto', 'curador', 'admin', 'consulta'];
const todos = (pode: string[]) => Object.fromEntries(PAPEIS.map((p) => [p, pode.includes(p)]));

const dados: DadosDaCartilha = {
  papeis: PAPEIS,
  matriz: [
    { acao: 'decidir', rotulo: 'Decidir validação', papeis: todos(['admin', 'arquiteto', 'techlead', 'negocio']) },
    { acao: 'descontinuar', rotulo: 'Descontinuar ativo', papeis: todos(['admin', 'curador', 'arquiteto']) },
  ],
  etapasPorPapel: { negocio: ['negocial'], techlead: ['tecnica'], arquiteto: ['arquitetural'], curador: [], admin: ['negocial', 'tecnica', 'arquitetural'], consulta: [] },
  blocosPorPapel: { negocio: ['Estrutura DDD'], techlead: ['Ativos técnicos'], arquiteto: [], curador: [], admin: [], consulta: [] },
  ritos: [{ tipoItem: 'dominio', rotulo: 'Domínio', criticidade: '*', etapas: ['negocial', 'arquitetural'], evidenciaMinima: 1, scoreMinimo: 70, slaHoras: 72, revisarACadaDias: 365 },
    { tipoItem: 'api', rotulo: 'API', criticidade: 'critica', etapas: ['negocial', 'tecnica', 'arquitetural'], evidenciaMinima: 2, scoreMinimo: 80, slaHoras: 24, revisarACadaDias: 90 }],
};

const secoes = dividirCartilhaEmSecoes(cartilhaMd);
const pagina = (d: DadosDaCartilha | null = dados) => renderToStaticMarkup(<PaginaDaCartilha secoes={secoes} dados={d} />);

/** A linha da matriz daquela ação, do <tr> ao </tr>. */
function linha(html: string, rotulo: string): string {
  const inicio = html.indexOf(`<td>${rotulo}</td>`);
  return html.slice(inicio, html.indexOf('</tr>', inicio));
}

describe('cartilha', () => {
  it('abre sem identificação (test_cartilha_abre_sem_identificacao)', () => {
    // A página não recebe perfil nenhum: não depende de quem você é.
    expect(pagina()).toContain('O fluxo de cada perfil');
  });

  it('não escreve nada: nenhum formulário (test_cartilha_nao_escreve_nada)', () => {
    expect(pagina()).not.toMatch(/<(form|input|textarea|select)\b/);
  });

  it('cada perfil tem seção e atalho (test_cada_perfil_tem_secao_e_atalho)', () => {
    const html = pagina();
    for (const perfil of PERFIS) {
      expect(html, perfil.chave).toContain(`id="${perfil.ancora}"`);
      expect(html, perfil.chave).toContain(`href="#${perfil.ancora}"`);
      expect(html, perfil.chave).toContain(perfil.lema);
    }
  });

  it('a matriz é a que a API devolve, célula por célula (test_matriz_reflete_as_permissoes_reais)', () => {
    const decidir = linha(pagina(), 'Decidir validação');
    expect(decidir.match(/class="sim"/g)).toHaveLength(4);
    expect(decidir.match(/aria-label="não"/g)).toHaveLength(2);
  });

  it('mudou a regra, muda a página (test_matriz_acompanha_mudanca_de_permissao)', () => {
    expect(linha(pagina(), 'Descontinuar ativo')).toContain('class="sim"');
    const ninguem = { ...dados, matriz: dados.matriz.map((l) => l.acao === 'descontinuar' ? { ...l, papeis: todos([]) } : l) };
    expect(linha(pagina(ninguem), 'Descontinuar ativo')).not.toContain('class="sim"');
  });

  it('os ritos vêm da API, com score e prazo (test_ritos_vem_da_tabela_de_politicas)', () => {
    const html = pagina();
    for (const rito of dados.ritos) {
      expect(html).toContain(`${rito.scoreMinimo}%`);
      expect(html).toContain(`${rito.slaHoras}h`);
    }
    expect(html).toContain('API<strong> crítica</strong>');
  });

  it('as tabelas ficam na seção de referência, e o markdown não as repete à mão', () => {
    expect(secoes.map((s) => s.titulo)).toContain(SECAO_DAS_TABELAS);
    expect(cartilhaMd).not.toContain('| Cadastrar ativo |');
    expect(pagina(null)).toContain('Carregando as tabelas');
  });
});
