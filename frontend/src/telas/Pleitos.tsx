import { useEffect, useState, type FormEvent } from 'react';
import { decidirPleito, listarPleitos, type AtivoResumo, type FilaDePleitos as Fila, type Pleito } from '../api';
import { Cabecalho } from '../componentes';
import { PAPEIS, ROTULO_DO_PAPEL, rotuloDoPapel, situacaoDoPleito } from '../papeis';

export type Decisao = { aprovar: boolean; resposta: string; papelConcedido?: string; escopoTipo?: string; escopoId?: string };

function espera(desde: string): string {
  const dias = Math.floor((Date.now() - new Date(desde).getTime()) / 86_400_000);
  return dias <= 0 ? 'hoje' : dias === 1 ? 'há 1 dia' : `há ${dias} dias`;
}

type PropsDaFila = {
  fila: Fila;
  dominios: AtivoResumo[];
  decidindo: string | null;
  onDecidir: (pleito: Pleito, decisao: Decisao) => void;
};

/**
 * A fila, como a /acessos do painel: pendentes primeiro e, entre eles, quem
 * espera há mais tempo. Admin só aparece entre os papéis a conceder para quem
 * é admin; a API recusa do mesmo jeito se alguém tentar.
 */
export function FilaDePleitos({ fila, dominios, decidindo, onDecidir }: PropsDaFila) {
  const papeis = PAPEIS.filter((p) => p !== 'admin' || fila.podeConcederAdmin);
  const pendentes = fila.pleitos.filter((p) => p.status === 'pendente');
  const decididos = fila.pleitos.filter((p) => p.status !== 'pendente');

  function enviar(pleito: Pleito, evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const dados = new FormData(evento.currentTarget);
    const botao = (evento.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null;
    const escopoTipo = dados.get('escopoTipo')?.toString() ?? 'empresa';
    onDecidir(pleito, {
      aprovar: botao?.value !== 'negar',
      resposta: dados.get('resposta')?.toString() ?? '',
      papelConcedido: dados.get('papelConcedido')?.toString(),
      escopoTipo,
      escopoId: escopoTipo === 'dominio' ? dados.get('escopoId')?.toString() || undefined : undefined,
    });
  }

  return (
    <>
      <section className="panel screen-panel" aria-labelledby="pendentes-titulo">
        <span className="panel-label" id="pendentes-titulo">Aguardando decisão ({pendentes.length})</span>
        {pendentes.length === 0 && <p className="vazio">Nenhum pleito aguardando decisão.</p>}
        {pendentes.map((pleito) => (
          <article className="pleito" id={`pleito-${pleito.id}`} key={pleito.id} aria-labelledby={`pleito-${pleito.id}-nome`}>
            <header>
              <h3 id={`pleito-${pleito.id}-nome`}>{pleito.pessoa?.nome ?? 'pessoa removida'}</h3>
              <span className="discreto">
                <span className="mono">{pleito.pessoa?.matricula}</span> · unidade <span className="mono">{pleito.pessoa?.unidade}</span> · {pleito.pessoa?.email} · pediu {espera(pleito.criadoEm)}
              </span>
            </header>
            <p>Pleiteia <strong>{rotuloDoPapel(pleito.papelPleiteado)}</strong>{pleito.justificativa && <>: <q>{pleito.justificativa}</q></>}</p>
            <form className="decisao-pleito" onSubmit={(e) => enviar(pleito, e)}>
              <label>Papel concedido
                <select name="papelConcedido" defaultValue={papeis.includes(pleito.papelPleiteado as never) ? pleito.papelPleiteado : 'consulta'}>
                  {papeis.map((p) => <option key={p} value={p}>{ROTULO_DO_PAPEL[p]}</option>)}
                </select>
              </label>
              <label>Vale em
                <select name="escopoTipo" defaultValue="empresa">
                  <option value="empresa">todo o catálogo</option>
                  <option value="dominio">um domínio</option>
                </select>
              </label>
              <label>Domínio
                <select name="escopoId" defaultValue="">
                  <option value="">—</option>
                  {dominios.map((d) => <option key={d.id} value={d.id}>{d.codigo} · {d.nome}</option>)}
                </select>
              </label>
              <label className="largo">Resposta
                <textarea name="resposta" rows={2} placeholder="Obrigatória ao negar e ao conceder papel diferente do pedido." />
              </label>
              <div className="modal-actions">
                <button className="outline-button" name="decisao" value="negar" disabled={decidindo === pleito.id}>Negar</button>
                <button className="primary-button" name="decisao" value="aprovar" disabled={decidindo === pleito.id}>Conceder</button>
              </div>
            </form>
          </article>
        ))}
      </section>

      <section className="panel screen-panel" aria-labelledby="decididos-titulo">
        <span className="panel-label" id="decididos-titulo">Decididos</span>
        {decididos.length === 0 ? <p className="vazio">Nenhum pleito decidido ainda.</p> : (
          <div className="rolagem">
            <table className="tabela">
              <thead><tr><th scope="col">Pessoa</th><th scope="col">Pediu</th><th scope="col">Decisão</th><th scope="col">Resposta</th></tr></thead>
              <tbody>
                {decididos.map((p) => (
                  <tr key={p.id}>
                    <td>{p.pessoa?.nome} <span className="mono discreto">{p.pessoa?.matricula}</span></td>
                    <td>{rotuloDoPapel(p.papelPleiteado)}</td>
                    <td><span className={`status status-pleito-${p.status}`}>{situacaoDoPleito(p.status)}</span>
                      {p.status === 'aprovado' && <span className="discreto"> {rotuloDoPapel(p.papelConcedido)}, {p.escopoRotulo}</span>}</td>
                    <td className="discreto">{p.resposta || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

type Props = { ativos: AtivoResumo[]; onErro: (mensagem: string) => void; onDecidido: () => void };

export function Pleitos({ ativos, onErro, onDecidido }: Props) {
  const [fila, setFila] = useState<Fila | null>(null);
  const [decidindo, setDecidindo] = useState<string | null>(null);
  const dominios = ativos.filter((a) => a.tipo.toLowerCase() === 'dominio');

  async function carregar() {
    try {
      setFila(await listarPleitos());
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível carregar os pleitos.');
    }
  }

  useEffect(() => {
    void carregar();
  }, []);

  async function decidir(pleito: Pleito, decisao: Decisao) {
    setDecidindo(pleito.id);
    onErro('');
    try {
      await decidirPleito(pleito.id, decisao);
      await carregar();
      onDecidido();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível decidir o pleito.');
    } finally {
      setDecidindo(null);
    }
  }

  return (
    <>
      <Cabecalho rotulo="ADMINISTRAÇÃO" titulo="Pleitos de acesso"
        texto="Quem espera há mais tempo aparece primeiro. Negar, ou conceder papel diferente do pedido, exige resposta: a pessoa tem direito de saber por quê." />
      {fila ? <FilaDePleitos fila={fila} dominios={dominios} decidindo={decidindo} onDecidir={decidir} /> : <p className="vazio">Carregando...</p>}
    </>
  );
}
