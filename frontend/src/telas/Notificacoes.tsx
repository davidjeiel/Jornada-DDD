import { useEffect, useState, type FormEvent } from 'react';
import {
  consultarPreferencias, listarNotificacoes, marcarNotificacaoLida, marcarTodasLidas, salvarPreferencias,
  type CaixaDeNotificacoes, type Notificacao, type PreferenciasDeNotificacao,
} from '../api';
import { Cabecalho } from '../componentes';
import { Link, navegar } from '../roteador';

const quando = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

/** A bolha do sino: só aparece com aviso não lido, e passa de 99 como "99+". */
export function BolhaDoSino({ naoLidas }: { naoLidas: number }) {
  if (naoLidas <= 0) return null;
  return <span className="bolha" aria-hidden="true">{naoLidas > 99 ? '99+' : naoLidas}</span>;
}

type PropsDaLista = {
  caixa: CaixaDeNotificacoes;
  apenasNaoLidas: boolean;
  ocupado: boolean;
  onAbrir: (n: Notificacao) => void;
  onLer: (n: Notificacao) => void;
  onLerTodas: () => void;
};

/** A caixa (notificacoes.html do painel): urgentes destacadas, não lidas em negrito, e o aviso leva aonde agir. */
export function ListaDeNotificacoes({ caixa, apenasNaoLidas, ocupado, onAbrir, onLer, onLerTodas }: PropsDaLista) {
  return (
    <>
      <div className="barra-da-caixa">
        <nav className="ficha-tabs" aria-label="Filtro da caixa">
          <Link href="/notificacoes" className={apenasNaoLidas ? 'ficha-tab' : 'ficha-tab active'} aria-selected={!apenasNaoLidas} role="tab">Todas</Link>
          <Link href="/notificacoes?naoLidas=1" className={apenasNaoLidas ? 'ficha-tab active' : 'ficha-tab'} aria-selected={apenasNaoLidas} role="tab">
            Não lidas <span className="conta">{caixa.naoLidas}</span>
          </Link>
        </nav>
        {caixa.naoLidas > 0 && <button type="button" className="outline-button" disabled={ocupado} onClick={onLerTodas}>Marcar todas como lidas</button>}
      </div>
      {caixa.itens.length === 0
        ? <p className="vazio">{apenasNaoLidas ? 'Nenhum aviso por ler.' : 'Nenhum aviso ainda. Quando algo pedir a sua atenção, aparece aqui.'}</p>
        : (
          <ul className="caixa-de-avisos">
            {caixa.itens.map((n) => (
              <li key={n.id} className={[n.lidaEm ? 'lida' : 'nao-lida', n.urgente ? 'urgente' : ''].join(' ').trim()}>
                <div>
                  <span className={n.urgente ? 'tag tag-urgente' : 'tag'}>{n.rotulo}</span>
                  <small className="discreto"> · {quando.format(new Date(n.criadaEm))}</small>
                </div>
                {n.url
                  ? <a href={n.url} className="titulo-do-aviso" onClick={(e) => { e.preventDefault(); onAbrir(n); }}>{n.titulo}</a>
                  : <strong className="titulo-do-aviso">{n.titulo}</strong>}
                {n.corpo && <p>{n.corpo}</p>}
                {!n.lidaEm && <button type="button" className="link-button" disabled={ocupado} onClick={() => onLer(n)}>Marcar como lida</button>}
              </li>
            ))}
          </ul>
        )}
    </>
  );
}

type PropsDasPreferencias = { preferencias: PreferenciasDeNotificacao; ocupado: boolean; onSalvar: (ativas: { tipo: string; canal: string }[]) => void };

/** As preferências (preferencias.html do painel): tipo por canal; os canais que ainda não entregam aparecem desligados. */
export function TabelaDePreferencias({ preferencias, ocupado, onSalvar }: PropsDasPreferencias) {
  function salvar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const dados = new FormData(evento.currentTarget);
    onSalvar([...dados.keys()].map((chave) => {
      const [tipo, canal] = chave.split(':');
      return { tipo, canal };
    }));
  }
  return (
    <form onSubmit={salvar}>
      <div className="rolagem">
        <table className="tabela preferencias">
          <thead>
            <tr>
              <th scope="col">Aviso</th>
              {preferencias.canais.map((c) => <th key={c.chave} scope="col">{c.rotulo}{!c.disponivel && <small className="discreto"> (em breve)</small>}</th>)}
            </tr>
          </thead>
          <tbody>
            {preferencias.tipos.map((t) => (
              <tr key={t.tipo}>
                <td>{t.rotulo}{t.urgente && <span className="tag tag-urgente">urgente</span>}</td>
                {preferencias.canais.map((c) => (
                  <td key={c.chave}>
                    <input type="checkbox" name={`${t.tipo}:${c.chave}`} defaultChecked={t.canais[c.chave]} disabled={!c.disponivel}
                      aria-label={`${t.rotulo} por ${c.rotulo}`} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="ajuda">E-mail e Teams dependem da identidade corporativa e ficam desligados até lá.</p>
      <div className="modal-actions"><button className="primary-button" disabled={ocupado}>Salvar preferências</button></div>
    </form>
  );
}

type Props = { aba: 'caixa' | 'preferencias'; apenasNaoLidas: boolean; onAlterado: () => void; onErro: (mensagem: string) => void };

// A caixa de avisos e as preferências (/notificacoes e /preferencias do painel).
export function Notificacoes({ aba, apenasNaoLidas, onAlterado, onErro }: Props) {
  const [caixa, setCaixa] = useState<CaixaDeNotificacoes | null>(null);
  const [preferencias, setPreferencias] = useState<PreferenciasDeNotificacao | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [aviso, setAviso] = useState('');
  const [versao, setVersao] = useState(0);
  const falhou = (e: unknown) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar as notificações.');

  useEffect(() => {
    if (aba === 'caixa') listarNotificacoes(apenasNaoLidas).then(setCaixa).catch(falhou);
    else consultarPreferencias().then(setPreferencias).catch(falhou);
  }, [aba, apenasNaoLidas, versao]);

  async function agir(acao: () => Promise<unknown>, depois?: () => void) {
    setOcupado(true);
    onErro('');
    try { await acao(); onAlterado(); if (depois) depois(); else setVersao((v) => v + 1); }
    catch (e) { falhou(e); }
    finally { setOcupado(false); }
  }

  return (
    <>
      <Cabecalho rotulo="VOCÊ" titulo="Notificações" texto="O que pede a sua atenção: etapas para decidir, prazos, respostas a pedidos seus." />
      <nav className="ficha-tabs" aria-label="Notificações">
        <Link href="/notificacoes" className={aba === 'caixa' ? 'ficha-tab active' : 'ficha-tab'} aria-selected={aba === 'caixa'} role="tab">Caixa</Link>
        <Link href="/notificacoes?aba=preferencias" className={aba === 'preferencias' ? 'ficha-tab active' : 'ficha-tab'} aria-selected={aba === 'preferencias'} role="tab">Preferências</Link>
      </nav>
      <section className="panel screen-panel">
        {aviso && <p className="aviso-de-sucesso" role="status">{aviso}</p>}
        {aba === 'caixa'
          ? caixa
            ? <ListaDeNotificacoes caixa={caixa} apenasNaoLidas={apenasNaoLidas} ocupado={ocupado}
                onAbrir={(n) => void agir(async () => { if (!n.lidaEm) await marcarNotificacaoLida(n.id); }, () => navegar(n.url!))}
                onLer={(n) => void agir(() => marcarNotificacaoLida(n.id))}
                onLerTodas={() => void agir(() => marcarTodasLidas())} />
            : <p className="vazio">Carregando a caixa...</p>
          : preferencias
            ? <TabelaDePreferencias preferencias={preferencias} ocupado={ocupado}
                onSalvar={(ativas) => void agir(async () => { setPreferencias(await salvarPreferencias(ativas)); setAviso('Preferências salvas.'); }, () => {})} />
            : <p className="vazio">Carregando as preferências...</p>}
      </section>
    </>
  );
}
