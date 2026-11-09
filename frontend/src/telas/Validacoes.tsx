import { useEffect, useState, type FormEvent } from 'react';
import {
  assumirValidacao, consultarFila, consultarPainel, decidirValidacao, liberarValidacao,
  type FilaDeValidacao, type ItemDaFila, type PainelDoValidador as Painel, type PreCheck,
} from '../api';
import { Cabecalho, Criticidade } from '../componentes';
import { Link, navegar } from '../roteador';
import { nomeDaPessoa } from '../usuarios';
import { TrilhaHierarquica } from './PaginaDoAtivo';

// A fila de validação do painel (/validacoes): abas por etapa com contagem,
// a mais crítica primeiro, o painel do validador ao lado e "decidir e seguir
// para a próxima". Quem pode decidir cada etapa é o servidor que diz (papel da
// etapa, owner do ativo, segregação de função).

export const ABAS_DA_FILA = [
  { chave: '', rotulo: 'Todas' },
  { chave: 'negocial', rotulo: 'Negocial' },
  { chave: 'tecnica', rotulo: 'Técnica' },
  { chave: 'arquitetural', rotulo: 'Arquitetural' },
  { chave: 'minhas', rotulo: 'Minhas' },
] as const;

/** O endereço da fila: a aba e, se houver, a etapa selecionada. */
/** O que o pré-check diz da revisão, para quem decide: os bloqueios, os alertas e, sem bloqueio, o score. */
export function ChecklistDeGovernanca({ preCheck }: { preCheck: PreCheck }) {
  return (
    <>
      <h3>Checklist de governança</h3>
      <ul className="lista-checagem">
        {preCheck.bloqueios.map((b) => <li key={b.codigo} className="bloqueio">{b.mensagem}</li>)}
        {preCheck.alertas.map((a) => <li key={a} className="pendente">{a}</li>)}
        {preCheck.bloqueios.length === 0 && <li className="ok">Pré-check sem bloqueios (score {preCheck.score}%)</li>}
      </ul>
    </>
  );
}

export function caminhoDaFila(aba: string, idValidacao?: string | null): string {
  const busca = new URLSearchParams();
  if (aba) busca.set('etapa', aba);
  if (idValidacao) busca.set('validacao', idValidacao);
  const texto = busca.toString();
  return `/validacoes${texto ? `?${texto}` : ''}`;
}

/**
 * Para onde a tela vai depois de decidir (web._proxima_da_fila): seguindo, para
 * a próxima pendente da mesma aba; sem seguir, para a aba sem seleção.
 */
export function destinoDepoisDeDecidir(itens: ItemDaFila[], aba: string, decidida: string, seguir: boolean): string {
  const proxima = seguir ? itens.find((i) => i.validacao.id !== decidida)?.validacao.id : undefined;
  return caminhoDaFila(aba, proxima);
}

function total(contagem: Record<string, number>, aba: string): number {
  if (aba === '') return ['negocial', 'tecnica', 'arquitetural'].reduce((soma, e) => soma + (contagem[e] ?? 0), 0);
  return contagem[aba] ?? 0;
}

type PropsDaLista = { fila: FilaDeValidacao; aba: string; selecionada: string | null };

export function ListaDaFila({ fila, aba, selecionada }: PropsDaLista) {
  return (
    <>
      <nav className="ficha-tabs" aria-label="Etapas">
        {ABAS_DA_FILA.map((a) => (
          <Link key={a.chave} href={caminhoDaFila(a.chave)} aria-selected={aba === a.chave} role="tab"
            className={aba === a.chave ? 'ficha-tab active' : 'ficha-tab'}>
            {a.rotulo} <span className="conta">{total(fila.contagem, a.chave)}</span>
          </Link>
        ))}
      </nav>
      <div className="compact-list">
        {fila.itens.length === 0 && <div className="empty-state">Nenhuma etapa aguardando decisão nesta aba.</div>}
        {fila.itens.map(({ validacao, ativo, slaRestante }) => (
          <Link key={validacao.id} href={caminhoDaFila(aba, validacao.id)}
            className={validacao.id === selecionada ? 'validation-row selecionada' : 'validation-row'}>
            <span>
              <strong>{ativo.codigo} · {ativo.nome}</strong>
              <small>etapa {validacao.etapa} · revisão {validacao.revisao}{validacao.atribuidaA ? ` · com ${nomeDaPessoa(validacao.atribuidaA)}` : ' · livre'}</small>
            </span>
            <Criticidade valor={ativo.criticidade} />
            <span className={slaRestante === 'vencido' ? 'status status-arquivado' : 'status'} title="Tempo até o prazo da etapa">{slaRestante}</span>
          </Link>
        ))}
      </div>
    </>
  );
}

type PropsDoPainel = {
  painel: Painel;
  idPessoa: string | null;
  podeDecidir: boolean;
  ocupado: boolean;
  onAssumir: () => void;
  onLiberar: () => void;
  onDecidir: (aprovada: boolean, parecer: string, seguir: boolean) => void;
};

/** O painel do validador: o ativo, as evidências, os responsáveis, o que mudou e a decisão. */
export function PainelDoValidador({ painel, idPessoa, podeDecidir, ocupado, onAssumir, onLiberar, onDecidir }: PropsDoPainel) {
  const { validacao, ficha, preCheck, mudancas } = painel;
  const minha = !!idPessoa && validacao.atribuidaA === idPessoa;

  function decidir(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    const botao = (evento.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null;
    onDecidir(botao?.value !== 'ajustes', d.get('parecer')?.toString() ?? '', d.get('seguir') === 'on');
  }

  return (
    <section className="panel screen-panel painel-do-validador" aria-labelledby="painel-titulo">
      <TrilhaHierarquica trilha={ficha.trilha} atual={ficha.nome} />
      <div className="panel-heading">
        <div>
          <span className="panel-label">{ficha.codigo} · etapa {validacao.etapa} · revisão #{validacao.revisao}</span>
          <h2 id="painel-titulo"><Link href={`/ativos/${ficha.id}`}>{ficha.nome}</Link></h2>
        </div>
        <div className={preCheck.aprovado ? 'score-ring good' : 'score-ring'} title="Score do pré-check">{preCheck.score}</div>
      </div>
      <p className="muted">{ficha.descricao}</p>

      <h3>O que mudou</h3>
      {painel.primeiraRevisao
        ? <p className="vazio">Primeira revisão deste ativo: não há versão anterior para comparar.</p>
        : mudancas === null
          ? <p className="vazio">A revisão anterior não tem retrato para comparar.</p>
          : mudancas.diferencas.length === 0
            ? <p className="vazio">Nenhuma diferença desde a revisão #{mudancas.a}.</p>
            : (
              <div className="rolagem">
                <table className="tabela diff">
                  <thead><tr><th scope="col">Campo</th><th scope="col">Revisão #{mudancas.a}</th><th scope="col">revisão #{mudancas.b}</th></tr></thead>
                  <tbody>{mudancas.diferencas.map((d) => <tr key={d.campo}><td>{d.rotulo}</td><td className="antes">{d.antes}</td><td className="depois">{d.depois}</td></tr>)}</tbody>
                </table>
              </div>
            )}

      <div className="duas-colunas">
        <div>
          <h3>Evidências</h3>
          {ficha.evidencias.length === 0 ? <p className="vazio">Nenhuma evidência.</p>
            : <ul className="lista-simples">{ficha.evidencias.map((e) => <li key={e.id}>{e.url ? <a href={e.url} target="_blank" rel="noreferrer">{e.titulo}</a> : e.titulo} <span className="tag">{e.tipo}</span></li>)}</ul>}
        </div>
        <div>
          <h3>Responsáveis</h3>
          {ficha.responsaveis.filter((r) => r.vigente).length === 0 ? <p className="vazio">Nenhum responsável vigente.</p>
            : <ul className="lista-simples">{ficha.responsaveis.filter((r) => r.vigente).map((r) => <li key={`${r.idPessoa}-${r.papel}`}>{nomeDaPessoa(r.idPessoa)} <span className="discreto">· {r.papel}</span></li>)}</ul>}
        </div>
      </div>

      <ChecklistDeGovernanca preCheck={preCheck} />

      {podeDecidir && validacao.status === 'aberta' && (
        <>
          <div className="detail-actions">
            {validacao.atribuidaA === null && <button type="button" className="outline-button" disabled={ocupado} onClick={onAssumir}>Assumir a análise</button>}
            {minha && <button type="button" className="outline-button" disabled={ocupado} onClick={onLiberar}>Devolver à fila livre</button>}
            {validacao.atribuidaA !== null && !minha && <span className="discreto">Em análise por {nomeDaPessoa(validacao.atribuidaA)}.</span>}
          </div>
          <form className="formulario-pagina" onSubmit={decidir}>
            <label>Parecer<textarea name="parecer" rows={3} placeholder="O que você conferiu, e o que falta se pedir ajustes." /></label>
            <label className="caixa-de-marcar"><input type="checkbox" name="seguir" defaultChecked /> Ir para a próxima da fila depois de decidir</label>
            <div className="modal-actions">
              <button className="outline-button" name="decisao" value="ajustes" disabled={ocupado}>Pedir ajustes</button>
              <button className="primary-button" name="decisao" value="aprovar" disabled={ocupado}>Aprovar a etapa</button>
            </div>
          </form>
        </>
      )}
    </section>
  );
}

type Props = { aba: string; selecionada: string | null; idPessoa: string | null; podeDecidir: boolean; onAlterado: () => Promise<void>; onErro: (mensagem: string) => void };

export function Validacoes({ aba, selecionada, idPessoa, podeDecidir, onAlterado, onErro }: Props) {
  const [fila, setFila] = useState<FilaDeValidacao | null>(null);
  const [painel, setPainel] = useState<Painel | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [versao, setVersao] = useState(0);

  useEffect(() => {
    const filtro = aba === 'minhas' ? { minhas: true } : { etapa: aba || undefined };
    consultarFila(filtro).then(setFila).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar a fila.'));
  }, [aba, versao]);

  useEffect(() => {
    setPainel(null);
    if (selecionada) consultarPainel(selecionada).then(setPainel).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível abrir a etapa.'));
  }, [selecionada, versao]);

  async function agir(acao: () => Promise<unknown>, depois?: () => void) {
    setOcupado(true);
    onErro('');
    try {
      await acao();
      await onAlterado();
      if (depois) depois(); else setVersao((v) => v + 1);
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível concluir.');
    } finally {
      setOcupado(false);
    }
  }

  return (
    <>
      <Cabecalho rotulo="GOVERNANÇA" titulo="Validações" texto="Cada etapa decidida por quem responde por ela. Quem submeteu não decide." />
      <div className="fila-e-painel">
        <section className="panel screen-panel" aria-label="Fila de validação">
          {fila ? <ListaDaFila fila={fila} aba={aba} selecionada={selecionada} /> : <p className="vazio">Carregando a fila...</p>}
        </section>
        {selecionada && (painel
          ? <PainelDoValidador painel={painel} idPessoa={idPessoa} podeDecidir={podeDecidir} ocupado={ocupado}
              onAssumir={() => void agir(() => assumirValidacao(painel.validacao.id))}
              onLiberar={() => void agir(() => liberarValidacao(painel.validacao.id))}
              onDecidir={(aprovada, parecer, seguir) => void agir(
                () => decidirValidacao(painel.validacao.id, aprovada, parecer || (aprovada ? 'Aprovado pela revisão' : 'Precisa de ajustes antes da publicação')),
                () => { setVersao((v) => v + 1); navegar(destinoDepoisDeDecidir(fila?.itens ?? [], aba, painel.validacao.id, seguir)); })} />
          : <p className="vazio">Abrindo a etapa...</p>)}
      </div>
    </>
  );
}
