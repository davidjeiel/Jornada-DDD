import { useEffect, useState } from 'react';
import { analisarImpacto, consultarVizinhanca, type Impacto, type Metamodelo, type Vizinhanca } from '../api';
import { Cabecalho } from '../componentes';
import { posicionar } from '../grafo';
import { Link, navegar } from '../roteador';
import { QuemDepende } from './Impacto';
import { caminhoDaAba } from './PaginaDoAtivo';

const LARGURA = 900;
const ALTURA = 560;

/** O endereço do grafo com estes parâmetros. */
export function caminhoDoGrafo(id: string, saltos: number, tipos: string[]): string {
  const busca = new URLSearchParams();
  if (saltos !== 1) busca.set('saltos', String(saltos));
  if (tipos.length) busca.set('tipos', tipos.join(','));
  const texto = busca.toString();
  return `/ativos/${id}/grafo${texto ? `?${texto}` : ''}`;
}

/** O SVG da vizinhança: o centro no meio e um anel por salto. */
export function DesenhoDoGrafo({ dados }: { dados: Vizinhanca }) {
  const { nos, arestas } = posicionar(dados, LARGURA, ALTURA);
  return (
    <svg className="grafo" viewBox={`0 0 ${LARGURA} ${ALTURA}`} role="img" aria-label={`Grafo com ${nos.length} ativos e ${arestas.length} relações`}>
      <defs>
        <marker id="seta" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" className="ponta" />
        </marker>
      </defs>
      {arestas.map((a) => (
        <g key={`${a.origem}-${a.destino}-${a.tipo}`} className={`aresta criticidade-${a.criticidade}`}>
          <line x1={a.x1} y1={a.y1} x2={a.x2} y2={a.y2} markerEnd="url(#seta)" />
          <text x={(a.x1 + a.x2) / 2} y={(a.y1 + a.y2) / 2 - 4}>{a.tipo.replace('_', ' ')}</text>
        </g>
      ))}
      {nos.map((n) => (
        <a key={n.ativo.id} href={`/ativos/${n.ativo.id}/grafo`} onClick={(e) => { e.preventDefault(); navegar(`/ativos/${n.ativo.id}/grafo`); }}>
          <g className={n.salto === 0 ? 'no centro' : `no salto-${n.salto}`}>
            <circle cx={n.x} cy={n.y} r={n.salto === 0 ? 14 : 9} />
            <text x={n.x} y={n.y + (n.salto === 0 ? 30 : 23)}>{n.ativo.codigo}</text>
            <title>{`${n.ativo.codigo} · ${n.ativo.nome} (${n.ativo.tipo})`}</title>
          </g>
        </a>
      ))}
    </svg>
  );
}

/** Os mesmos nós do desenho, em tabela: quem não enxerga o grafo lê a lista (o "Ativos no grafo" do painel). */
export function AtivosNoGrafo({ dados }: { dados: Vizinhanca }) {
  return (
    <section className="detail-section ativos-no-grafo" aria-labelledby="ativos-no-grafo">
      <span className="panel-label" id="ativos-no-grafo">Ativos no grafo ({dados.nos.length})</span>
      <div className="rolagem">
        <table className="tabela">
          <thead><tr><th scope="col">Ativo</th><th scope="col">Tipo</th><th scope="col">Salto</th></tr></thead>
          <tbody>
            {dados.nos.map((n) => (
              <tr key={n.ativo.id}>
                <td><Link href={`/ativos/${n.ativo.id}`}>{n.ativo.nome}</Link> <span className="mono discreto">{n.ativo.codigo}</span></td>
                <td>{n.ativo.tipo}</td>
                <td>{n.salto === 0 ? 'centro' : n.salto}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

type PropsDaVisao = {
  dados: Vizinhanca;
  impacto: Impacto | null;
  metamodelo: Metamodelo | null;
  tipos: string[];
  onSaltos: (saltos: number) => void;
  onTipos: (tipos: string[]) => void;
};

export function VisaoDoGrafo({ dados, impacto, metamodelo, tipos, onSaltos, onTipos }: PropsDaVisao) {
  const centro = dados.nos.find((n) => n.salto === 0)?.ativo;
  return (
    <>
      <section className="panel screen-panel">
        <div className="controles-do-grafo">
          <label>Distância
            <select value={dados.saltos} onChange={(e) => onSaltos(Number(e.target.value))}>
              <option value={1}>1 salto</option><option value={2}>2 saltos</option><option value={3}>3 saltos</option>
            </select>
          </label>
          <fieldset>
            <legend>Tipos de relação</legend>
            {(metamodelo?.relacoes ?? []).map((r) => (
              <label key={r.chave} className="caixa-de-marcar">
                <input type="checkbox" checked={tipos.includes(r.chave)}
                  onChange={(e) => onTipos(e.target.checked ? [...tipos, r.chave] : tipos.filter((t) => t !== r.chave))} /> {r.rotulo}
              </label>
            ))}
          </fieldset>
        </div>
        {dados.truncado && (
          <p className="alerta-integridade" role="status">
            O grafo passou do teto de {dados.teto} ativos e foi cortado: há mais relações do que as desenhadas. Diminua a distância ou filtre por tipo.
          </p>
        )}
        <p className="discreto">{dados.nos.length} ativo(s) e {dados.arestas.length} relação(ões). Clique num ativo para centrar o grafo nele.</p>
        <DesenhoDoGrafo dados={dados} />
        <AtivosNoGrafo dados={dados} />
        {centro && <p><Link href={caminhoDaAba(centro.id, 'relacoes')}>Voltar às relações de {centro.codigo}</Link></p>}
      </section>
      <section className="panel screen-panel">
        {impacto ? <QuemDepende impacto={impacto} /> : <p className="vazio">Analisando quem depende deste ativo...</p>}
      </section>
    </>
  );
}

type Props = { id: string; saltos: number; tipos: string[]; metamodelo: Metamodelo | null; onErro: (mensagem: string) => void };

// O grafo do painel (/ativo/&lt;id&gt;/grafo): a vizinhança em anéis e quem depende do ativo.
export function Grafo({ id, saltos, tipos, metamodelo, onErro }: Props) {
  const [dados, setDados] = useState<Vizinhanca | null>(null);
  const [impacto, setImpacto] = useState<Impacto | null>(null);

  useEffect(() => {
    consultarVizinhanca(id, saltos, tipos).then(setDados).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar o grafo.'));
  }, [id, saltos, tipos.join(',')]);

  useEffect(() => {
    setImpacto(null);
    analisarImpacto(id).then(setImpacto).catch(() => setImpacto(null));
  }, [id]);

  const centro = dados?.nos.find((n) => n.salto === 0)?.ativo;
  return (
    <>
      <Cabecalho rotulo={centro ? `${centro.codigo} · GRAFO` : 'GRAFO'} titulo={centro ? centro.nome : 'Grafo de relações'}
        texto="As relações deste ativo e dos vizinhos, em anéis por distância. O anel diz quantas relações separam cada ativo do centro." />
      {dados
        ? <VisaoDoGrafo dados={dados} impacto={impacto} metamodelo={metamodelo} tipos={tipos}
            onSaltos={(s) => navegar(caminhoDoGrafo(id, s, tipos))} onTipos={(t) => navegar(caminhoDoGrafo(id, dados.saltos, t))} />
        : <p className="vazio">Carregando o grafo...</p>}
    </>
  );
}
