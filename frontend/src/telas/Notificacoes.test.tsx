// Equivalentes de tests/test_acesso.py (sino e caixa) do painel-ddd.
// O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { CaixaDeNotificacoes, Notificacao, PreferenciasDeNotificacao } from '../api';
import { Layout } from '../Layout';
import { MENU } from '../navegacao';
import { perfilDe } from '../testes/perfis';
import { BolhaDoSino, ListaDeNotificacoes, TabelaDePreferencias } from './Notificacoes';

const nada = () => {};
const aviso = (id: string, extra: Partial<Notificacao> = {}): Notificacao => ({
  id, tipo: 'revisao_submetida', rotulo: 'Revisão aguardando sua análise', urgente: true,
  titulo: 'Core Bancário aguarda validação tecnica', corpo: 'SIS-0001 entrou na fila da etapa tecnica.',
  url: '/validacoes?validacao=v1', idAtivo: 'a1', criadaEm: '2026-11-05T10:00:00Z', lidaEm: null, ...extra,
});
const caixa: CaixaDeNotificacoes = { naoLidas: 1, itens: [aviso('n1'), aviso('n2', { lidaEm: '2026-11-05T11:00:00Z', urgente: false, rotulo: 'Ativo que você consome foi publicado' })] };

function topo(naoLidas: number) {
  return renderToStaticMarkup(
    <Layout secao="catalogo" perfil={perfilDe('negocio')} nomeDaEmpresa="Horizonte" tema="sistema" onAlternarTema={nada}
      menuCompacto={false} onAlternarMenu={nada} termo="" onBuscar={nada} naoLidas={naoLidas}><p>x</p></Layout>);
}

describe('notificações', () => {
  it('o sino mostra a contagem de não lidas (test_sino_mostra_a_contagem_de_nao_lidas)', () => {
    expect(topo(3)).toContain('class="bolha"');
    expect(topo(3)).toContain('Notificações: 3 não lida(s)');
    expect(topo(0)).not.toContain('class="bolha"');
    expect(renderToStaticMarkup(<BolhaDoSino naoLidas={140} />)).toContain('99+');
    const html = renderToStaticMarkup(<ListaDeNotificacoes caixa={caixa} apenasNaoLidas={false} ocupado={false} onAbrir={nada} onLer={nada} onLerTodas={nada} />);
    expect(html).toContain('aguarda validação');
  });

  it('a caixa destaca o urgente não lido e só oferece marcar o que falta ler', () => {
    const html = renderToStaticMarkup(<ListaDeNotificacoes caixa={caixa} apenasNaoLidas={false} ocupado={false} onAbrir={nada} onLer={nada} onLerTodas={nada} />);
    expect(html).toContain('class="nao-lida urgente"');
    expect(html).toContain('class="lida"');
    expect(html.match(/Marcar como lida/g)).toHaveLength(1);
    expect(html).toContain('Marcar todas como lidas');
    expect(html).toContain('href="/validacoes?validacao=v1"');
    const vazia = renderToStaticMarkup(<ListaDeNotificacoes caixa={{ naoLidas: 0, itens: [] }} apenasNaoLidas ocupado={false} onAbrir={nada} onLer={nada} onLerTodas={nada} />);
    expect(vazia).toContain('Nenhum aviso por ler.');
    expect(vazia).not.toContain('Marcar todas');
  });

  it('as preferências: o canal app liga e desliga; e-mail e Teams aparecem desligados', () => {
    const prefs: PreferenciasDeNotificacao = {
      canais: [{ chave: 'app', rotulo: 'No catálogo', disponivel: true }, { chave: 'email', rotulo: 'E-mail', disponivel: false }],
      tipos: [{ tipo: 'sla_vencido', rotulo: 'SLA de validação vencido', urgente: true, canais: { app: true, email: false } }],
    };
    const html = renderToStaticMarkup(<TabelaDePreferencias preferencias={prefs} ocupado={false} onSalvar={nada} />);
    const app = html.match(/<input[^>]*name="sla_vencido:app"[^>]*>/)?.[0] ?? '';
    const email = html.match(/<input[^>]*name="sla_vencido:email"[^>]*>/)?.[0] ?? '';
    expect(app).toContain('checked');
    expect(app).not.toContain('disabled');
    expect(email).toContain('disabled');
    expect(html).toContain('(em breve)');
  });

  it('notificações saiu do aviso de fase', () => {
    expect(MENU.find((m) => m.secao === 'notificacoes')?.fase).toBeUndefined();
  });
});
