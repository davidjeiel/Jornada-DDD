// Equivalentes de tests/test_escala.py (tela de descobertas) do painel-ddd.
// O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Descobertas, ItemDoCatalogo, PlanoDeImportacao } from '../api';
import { MENU } from '../navegacao';
import { BandejaDeDescobertas, FormularioDeImportacao, PreviaDaImportacao } from './Descobertas';

const nada = () => {};
const dados: Descobertas = {
  fontes: [
    { chave: 'openapi', rotulo: 'Contrato OpenAPI', ajuda: 'JSON do contrato.', tipoDoPai: 'aplicacao' },
    { chave: 'git', rotulo: 'Inventário Git', ajuda: 'Lista JSON.', tipoDoPai: 'aplicacao' },
  ],
  bandeja: [],
};
const plano: PlanoDeImportacao = {
  fonte: 'openapi', titulo: 'API Simulação', versao: '2.1', criar: 4, manter: 0, idPai: 'app-1',
  acoes: [
    { acao: 'criar', tipo: 'API', nome: 'API Simulação', detalhe: 'versão 2.1' },
    { acao: 'criar', tipo: 'Endpoint', nome: 'POST /simulacoes', detalhe: 'Cria uma simulação' },
    { acao: 'criar', tipo: 'Endpoint', nome: 'GET /simulacoes', detalhe: 'Lista simulações' },
    { acao: 'criar', tipo: 'Endpoint', nome: 'GET /simulacoes/{id}', detalhe: 'Consulta uma simulação' },
  ],
};
const formulario = (extra: { plano?: PlanoDeImportacao | null; erro?: string } = {}) => renderToStaticMarkup(
  <FormularioDeImportacao dados={dados} aplicacoes={[{ id: 'app-1', codigo: 'APP-0001', nome: 'Motor de Simulação', tipo: 'aplicacao', status: 'rascunho', criticidade: 'media' }]}
    pedido={{ fonte: 'openapi', conteudo: '{}', idPai: 'app-1' }} plano={extra.plano ?? null} erro={extra.erro ?? ''} ocupado={false}
    onMudar={nada} onPrevia={nada} onImportar={nada} />);

describe('descobertas', () => {
  it('a tela mostra a prévia antes de gravar (test_tela_de_descobertas_mostra_previa_antes_de_gravar)', () => {
    const html = renderToStaticMarkup(<PreviaDaImportacao plano={plano} ocupado={false} onImportar={nada} />);
    expect(html).toContain('API Simulação');
    expect(html).toContain('4 a criar');
    expect(html).toContain('NADA FOI GRAVADO');
    expect(html).toContain('GET /simulacoes/{id}');
  });

  it('importar só aparece depois da prévia (test_tela_de_descobertas_importa_ao_confirmar)', () => {
    expect(formulario()).not.toContain('Importar 4 ativo(s)');
    expect(formulario()).toContain('Ver a prévia');
    expect(formulario({ plano })).toContain('Importar 4 ativo(s)');
    const nadaNovo = renderToStaticMarkup(<PreviaDaImportacao plano={{ ...plano, criar: 0, manter: 4, acoes: plano.acoes.map((a) => ({ ...a, acao: 'manter' as const })) }} ocupado={false} onImportar={nada} />);
    expect(nadaNovo.match(/<button[^>]*>/)?.[0]).toContain('disabled');
    expect(nadaNovo).toContain('já existe');
  });

  it('JSON inválido avisa em vez de quebrar (test_json_invalido_avisa_em_vez_de_quebrar)', () => {
    const html = formulario({ erro: 'Não consegui ler esse conteúdo como JSON: linha 1.' });
    expect(html).toContain('role="alert"');
    expect(html).toContain('Não consegui ler esse conteúdo');
  });

  it('a bandeja lista as descobertas com seleção, e avisa quando está vazia', () => {
    const itens: ItemDoCatalogo[] = [{ ativo: { id: 'a1', codigo: 'API-0001', nome: 'API Simulação', tipo: 'api', status: 'rascunho', criticidade: 'media' },
      origem: 'automatica', idSquad: null, temResponsavel: false, atualizadoEm: '2026-11-05T10:00:00Z',
      pai: { id: 'app', codigo: 'APP-0001', nome: 'Motor', tipo: 'aplicacao', status: 'publicado', criticidade: 'media' }, score: 55,
      criadoPor: '40000000-0000-0000-0000-000000000002' }];
    const html = renderToStaticMarkup(<BandejaDeDescobertas itens={itens} selecionados={['a1']} ocupado={false} onSelecionar={nada} onTriar={nada} />);
    expect(html).toContain('href="/ativos/a1"');
    expect(html).toContain('href="/ativos/app"');            // o pai, como a coluna Contexto/pai do painel
    expect(html).toContain('Trazido por');
    expect(html).toContain('1 por triar');
    expect(html).toContain('Aceitar 1');
    expect(renderToStaticMarkup(<BandejaDeDescobertas itens={[]} selecionados={[]} ocupado={false} onSelecionar={nada} onTriar={nada} />)).toContain('Nada na bandeja');
    expect(MENU.find((m) => m.secao === 'descobertas')?.fase).toBeUndefined();
  });
});
