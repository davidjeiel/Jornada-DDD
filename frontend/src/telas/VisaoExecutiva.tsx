import { useEffect, useState, type FormEvent } from 'react';
import { Camera, FilePlus2 } from 'lucide-react';
import {
  consultarFila, consultarGrafoDoCatalogo, consultarIndicadores, fotografarIndicadores,
  type GrafoDoCatalogo, type Indicadores, type ItemDaFila, type PendenciaPrioritaria, type RecorteDoGrafo,
} from '../api';
import { Cabecalho, Criticidade } from '../componentes';
import { posicionarEmCirculo } from '../grafo';
import { Link, navegar } from '../roteador';
import { caminhoDaFila } from './Validacoes';

const numero = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 });

/**
 * Como ler a variação no mês (a macro delta do dashboard do painel): em
 * "sem responsável" e "pendências" cair é melhora, e a cor acompanha o sentido.
 * Sem variação, ou com uma competência só, não há o que dizer.
 */
export function leituraDaVariacao(variacao: number | undefined, quantoMenosMelhor: boolean): { classe: 'bom' | 'ruim'; seta: string; texto: string } | null {
  if (!variacao) return null;
  const melhorou = quantoMenosMelhor ? variacao < 0 : variacao > 0;
  return { classe: melhorou ? 'bom' : 'ruim', seta: variacao > 0 ? '▲' : '▼', texto: `${numero.format(Math.abs(variacao))} no mês` };
}

/** Os KPIs do topo, cada um levando ao recorte que o explica (test_kpis_do_painel_levam_ao_recorte). */
export const KPIS = [
  { chave: 'dominios', rotulo: 'Domínios vigentes', destino: '/catalogo?tipo=dominio' },
  { chave: 'capacidades', rotulo: 'Capacidades', destino: '/catalogo?tipo=capacidade' },
  { chave: 'cobertura', rotulo: 'Cobertura das capacidades', destino: '/mapas', porcentagem: true, meta: 80 },
  { chave: 'sem_owner', rotulo: 'Publicados sem responsável', destino: '/catalogo?semOwner=true&status=publicado' },
  { chave: 'apis_governadas', rotulo: 'APIs governadas', destino: '/catalogo?tipo=api', porcentagem: true, meta: 80 },
  { chave: 'pendencias', rotulo: 'Etapas pendentes', destino: '/validacoes' },
] as const;

function classeDoValor(kpi: (typeof KPIS)[number], valor: number): string {
  if ('meta' in kpi) return valor >= kpi.meta ? 'bom' : 'alerta';
  if (kpi.chave === 'sem_owner') return valor ? 'risco' : 'bom';
  if (kpi.chave === 'pendencias') return valor ? 'alerta' : 'bom';
  return '';
}

export function CartoesDosIndicadores({ indicadores }: { indicadores: Indicadores }) {
  return (
    <div className="metrics kpis">
      {KPIS.map((kpi) => {
        const valor = indicadores.atuais[kpi.chave] ?? 0;
        const leitura = leituraDaVariacao(indicadores.variacao[kpi.chave], indicadores.quantoMenosMelhor.includes(kpi.chave));
        return (
          <Link key={kpi.chave} className="metric kpi" href={kpi.destino}>
            <span>{kpi.rotulo}</span>
            <strong className={classeDoValor(kpi, valor)}>{numero.format(valor)}{'porcentagem' in kpi ? '%' : ''}</strong>
            {leitura
              ? <small className={`delta ${leitura.classe}`} title="Variação em relação à competência anterior"><span aria-hidden="true">{leitura.seta}</span> {leitura.texto}</small>
              : <small>sem variação registrada</small>}
          </Link>
        );
      })}
    </div>
  );
}

const NA_SERIE = [
  { chave: 'cobertura', rotulo: 'Cobertura', sufixo: '%' },
  { chave: 'apis_governadas', rotulo: 'APIs governadas', sufixo: '%' },
  { chave: 'qualidade_media', rotulo: 'Qualidade média', sufixo: '%' },
  { chave: 'sem_owner', rotulo: 'Sem responsável', sufixo: '' },
  { chave: 'pendencias', rotulo: 'Pendências', sufixo: '' },
  { chave: 'sla_vencido', rotulo: 'SLA vencido', sufixo: '' },
];

/** As fotografias mensais, a mais recente primeiro. */
export function SerieHistorica({ indicadores }: { indicadores: Indicadores }) {
  if (indicadores.serie.length === 0)
    return <p className="vazio">Nenhuma fotografia mensal ainda. Sem ela, a visão mostra o número de hoje, mas não diz se melhorou.</p>;
  return (
    <div className="rolagem">
      <table className="tabela">
        <thead><tr><th scope="col">Competência</th>{NA_SERIE.map((c) => <th key={c.chave} scope="col">{c.rotulo}</th>)}</tr></thead>
        <tbody>
          {[...indicadores.serie].reverse().map((f) => (
            <tr key={f.competencia}>
              <td className="mono">{f.competencia}</td>
              {NA_SERIE.map((c) => <td key={c.chave}>{c.chave in f.valores ? `${numero.format(f.valores[c.chave])}${c.sufixo}` : '—'}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** O endereço da visão com o recorte do grafo (?dominio=&subdominio=&item=). */
export function caminhoDaVisao(recorte: RecorteDoGrafo): string {
  const busca = new URLSearchParams();
  for (const chave of ['dominio', 'subdominio', 'item'] as const) {
    const valor = recorte[chave];
    if (valor) busca.set(chave, valor);
  }
  const texto = busca.toString();
  return texto ? `/?${texto}` : '/';
}

export function recorteDaUrl(busca: URLSearchParams): RecorteDoGrafo {
  return { dominio: busca.get('dominio'), subdominio: busca.get('subdominio'), item: busca.get('item') };
}

const LARGURA_DO_GRAFO = 900;
const ALTURA_DO_GRAFO = 520;

type PropsDaExposicao = { grafo: GrafoDoCatalogo; recorte: RecorteDoGrafo; onRecorte: (recorte: RecorteDoGrafo) => void };

/**
 * "Exposição do catálogo" (o grafo do dashboard do painel): todos os ativos
 * numa elipse, pai → filho tracejado e as relações em linha cheia. O recorte
 * por domínio e subdomínio, ou o clique num nó, mostra só aquele ramo.
 */
export function ExposicaoDoCatalogo({ grafo, recorte, onRecorte }: PropsDaExposicao) {
  const [dominio, setDominio] = useState(recorte.dominio ?? '');
  const [subdominio, setSubdominio] = useState(recorte.subdominio ?? '');
  const subdominios = grafo.subdominios.filter((s) => !dominio || s.idPai === dominio);
  const filtrado = !!(recorte.dominio || recorte.subdominio || recorte.item);
  const { nos, arestas } = posicionarEmCirculo(grafo.nos, grafo.arestas, LARGURA_DO_GRAFO, ALTURA_DO_GRAFO);
  const recortadoEm = recorte.item ? grafo.nos.find((n) => n.id === recorte.item) : undefined;

  function aplicar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    onRecorte({ dominio: dominio || null, subdominio: subdominio || null });
  }

  return (
    <section className="panel screen-panel exposicao" aria-labelledby="exposicao-titulo">
      <div className="panel-heading">
        <div>
          <span className="panel-label">EXPOSIÇÃO DO CATÁLOGO</span>
          <h2 id="exposicao-titulo">{filtrado ? 'Ativos e relações do recorte' : 'Todos os ativos e suas relações'}</h2>
        </div>
        <Link className="outline-button" href="/catalogo">Ver catálogo</Link>
      </div>
      <form className="controles-do-grafo" onSubmit={aplicar} aria-label="Filtrar a exposição do catálogo">
        <label>Domínio
          <select value={dominio} onChange={(e) => { setDominio(e.target.value); setSubdominio(''); }}>
            <option value="">Todos os domínios</option>
            {grafo.dominios.map((d) => <option key={d.id} value={d.id}>{d.nome}</option>)}
          </select>
        </label>
        <label>Subdomínio
          <select value={subdominio} onChange={(e) => setSubdominio(e.target.value)}>
            <option value="">Todos os subdomínios</option>
            {subdominios.map((d) => <option key={d.id} value={d.id}>{d.nome}</option>)}
          </select>
        </label>
        <div className="acoes-do-grafo">
          <button className="primary-button">Aplicar filtros</button>
          {filtrado && <Link className="outline-button" href="/">Limpar</Link>}
        </div>
      </form>
      {recortadoEm && <p className="discreto">Recortado em <strong>{recortadoEm.nome}</strong> e no que está abaixo dele.</p>}
      {grafo.truncado && (
        <p className="alerta-integridade" role="status">
          O catálogo passou de {grafo.teto} ativos e o desenho foi cortado. Recorte por domínio ou subdomínio para ver o resto.
        </p>
      )}
      {nos.length === 0 ? <p className="vazio">Nenhum ativo neste recorte.</p> : (
        <div className="rolagem">
          <svg className="grafo resumo" viewBox={`0 0 ${LARGURA_DO_GRAFO} ${ALTURA_DO_GRAFO}`} role="img"
            aria-label={`${filtrado ? 'Ativos do recorte' : 'Ativos do catálogo'}: ${nos.length} ativos e ${arestas.length} ligações`}>
            <defs>
              <marker id="ponta-resumo" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="5" markerHeight="5" orient="auto">
                <path d="M 0 0 L 10 5 L 0 10 z" className="ponta" />
              </marker>
            </defs>
            {arestas.map((a, i) => (
              <line key={`${a.origem}-${a.destino}-${a.tipo}-${i}`} className={a.tipo === 'hierarquia' ? 'aresta-resumo hierarquia' : `aresta-resumo criticidade-${a.criticidade}`}
                x1={a.x1} y1={a.y1} x2={a.x2} y2={a.y2} markerEnd="url(#ponta-resumo)">
                <title>{a.tipo === 'hierarquia' ? 'pai → filho' : a.tipo.replace('_', ' ')}</title>
              </line>
            ))}
            {nos.map((n) => (
              <a key={n.id} href={caminhoDaVisao({ item: n.id })} onClick={(e) => { e.preventDefault(); onRecorte({ item: n.id }); }}>
                <g className={`no-resumo criticidade-${n.criticidade}`}>
                  <circle cx={n.x} cy={n.y} r={10} />
                  <text x={n.x} y={n.y - 16}>{n.nome}</text>
                  <title>{`${n.nome} · ${n.tipo} · ${n.codigo}`}</title>
                </g>
              </a>
            ))}
          </svg>
        </div>
      )}
      <p className="discreto legenda-do-grafo">Cada nó é um ativo; a cor da borda é a criticidade. Linha tracejada liga pai e filho; linha cheia é uma relação vigente{grafo.filtrado ? ' (no recorte, só a hierarquia aparece)' : ''}. Clique num nó para recortar pelo que está abaixo dele.</p>
    </section>
  );
}

/** Por domínio, as capacidades cobertas (servicos.cobertura_por_dominio). */
export function CoberturaPorDominio({ indicadores }: { indicadores: Indicadores }) {
  if (indicadores.coberturaPorDominio.length === 0) return <p className="vazio">Nenhum domínio com subdomínio ainda.</p>;
  return (
    <ul className="lista-de-barras">
      {indicadores.coberturaPorDominio.map((d) => (
        <li key={d.id}>
          <div className="linha-da-barra">
            <Link href={`/ativos/${d.id}`}>{d.nome}</Link>
            <span className="discreto">{d.cobertas}/{d.total} · {d.percentual}%</span>
          </div>
          <div className={d.percentual < 70 ? 'barra baixa' : 'barra'} role="img" aria-label={`${d.percentual}% das capacidades cobertas`}>
            <span style={{ width: `${d.percentual}%` }} />
          </div>
        </li>
      ))}
    </ul>
  );
}

/** Para onde a pendência leva: o catálogo recortado, a fila, ou lugar nenhum. */
export function destinoDaPendencia(p: PendenciaPrioritaria): string | null {
  if (p.tela === 'validacoes') return '/validacoes';
  if (!p.tipoItem) return null;
  return `/catalogo?tipo=${encodeURIComponent(p.tipoItem)}${p.semResponsavel ? '&semOwner=true' : ''}`;
}

/** As pendências prioritárias (servicos.pendencias_prioritarias), cada uma levando ao recorte. */
export function PendenciasPrioritarias({ indicadores }: { indicadores: Indicadores }) {
  return (
    <table className="tabela">
      <tbody>
        {indicadores.pendencias.map((p) => {
          const destino = destinoDaPendencia(p);
          return (
            <tr key={p.chave}>
              <td>{destino ? <Link href={destino}>{p.titulo}</Link> : p.titulo}</td>
              <td className="numero"><strong className={p.total ? 'contagem ruim' : 'contagem bom'}>{p.total}</strong></td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

/** As cinco primeiras da fila de validação, com etapa e prazo. */
export function FilaResumida({ itens }: { itens: ItemDaFila[] }) {
  if (itens.length === 0) return <p className="vazio">Sem validações pendentes.</p>;
  return (
    <ul className="lista-simples">
      {itens.slice(0, 5).map(({ validacao, ativo, slaRestante }) => (
        <li key={validacao.id} className="linha-da-fila">
          <Link href={caminhoDaFila(validacao.etapa, validacao.id)}>{ativo.nome}</Link>
          <span className="discreto">{validacao.etapa} · {slaRestante}</span> <Criticidade valor={ativo.criticidade} />
        </li>
      ))}
    </ul>
  );
}

/** A qualidade cadastral média, com a barra (qualidade.media_geral). */
export function QualidadeCadastral({ indicadores }: { indicadores: Indicadores }) {
  const valor = indicadores.atuais.qualidade_media ?? 0;
  const leitura = leituraDaVariacao(indicadores.variacao.qualidade_media, false);
  return (
    <>
      <strong className={`valor-grande ${valor >= 80 ? 'bom' : valor >= 60 ? 'alerta' : 'risco'}`}>{numero.format(valor)}%</strong>
      {leitura && <small className={`delta ${leitura.classe}`}> <span aria-hidden="true">{leitura.seta}</span> {leitura.texto}</small>}
      <div className={valor < 70 ? 'barra baixa' : 'barra'} role="img" aria-label={`Qualidade média de ${valor}%`}><span style={{ width: `${valor}%` }} /></div>
      <p className="discreto">Média do score de qualidade dos ativos não arquivados: completude, consistência, ownership, evidência e temporalidade, as mesmas dimensões do pré-check.</p>
    </>
  );
}

type Props = {
  busca: URLSearchParams;
  nomeDaEmpresa: string;
  podeEscrever: boolean;
  podeFotografar: boolean;
  onErro: (mensagem: string) => void;
};

// A visão executiva do painel (o dashboard): os indicadores que levam ao
// recorte, a exposição do catálogo, a cobertura por domínio, a qualidade, as
// pendências, a fila e a série das fotografias.
export function VisaoExecutiva({ busca, nomeDaEmpresa, podeEscrever, podeFotografar, onErro }: Props) {
  const [indicadores, setIndicadores] = useState<Indicadores | null>(null);
  const [grafo, setGrafo] = useState<GrafoDoCatalogo | null>(null);
  const [fila, setFila] = useState<ItemDaFila[] | null>(null);
  const [versao, setVersao] = useState(0);
  const [ocupado, setOcupado] = useState(false);
  const recorte = recorteDaUrl(busca);
  const chaveDoRecorte = caminhoDaVisao(recorte);
  const falhou = (padrao: string) => (e: unknown) => onErro(e instanceof Error ? e.message : padrao);

  useEffect(() => {
    consultarIndicadores().then(setIndicadores).catch(falhou('Não foi possível carregar os indicadores.'));
    consultarFila().then((f) => setFila(f.itens)).catch(falhou('Não foi possível carregar a fila de validação.'));
  }, [versao]);

  useEffect(() => {
    let vigente = true;
    consultarGrafoDoCatalogo(recorte).then((g) => { if (vigente) setGrafo(g); }).catch(falhou('Não foi possível carregar o grafo do catálogo.'));
    return () => { vigente = false; };
  }, [chaveDoRecorte]);

  async function fotografar() {
    setOcupado(true);
    onErro('');
    try { await fotografarIndicadores(); setVersao((v) => v + 1); }
    catch (e) { onErro(e instanceof Error ? e.message : 'Não foi possível fotografar os indicadores.'); }
    finally { setOcupado(false); }
  }

  return (
    <>
      <Cabecalho
        rotulo={nomeDaEmpresa.toUpperCase()}
        titulo="Visão executiva"
        texto="A saúde do catálogo e da governança, num relance. Cada número leva ao recorte que o explica."
        acoes={podeEscrever && <Link className="primary-button" href="/novo"><FilePlus2 size={17} aria-hidden="true" /> Novo ativo</Link>}
      />

      {indicadores ? <CartoesDosIndicadores indicadores={indicadores} /> : <p className="vazio">Calculando os indicadores...</p>}

      {grafo
        ? <ExposicaoDoCatalogo key={chaveDoRecorte} grafo={grafo} recorte={recorte} onRecorte={(r) => navegar(caminhoDaVisao(r))} />
        : <section className="panel screen-panel"><p className="vazio">Desenhando o catálogo...</p></section>}

      <div className="grade-executiva">
        <div className="coluna-executiva">
          <section className="panel screen-panel" aria-labelledby="cobertura-titulo">
            <div className="panel-heading"><div><span className="panel-label">CAPACIDADES</span><h2 id="cobertura-titulo">Cobertura por domínio</h2></div></div>
            {indicadores ? <CoberturaPorDominio indicadores={indicadores} /> : <p className="vazio">Calculando...</p>}
          </section>
          <section className="panel screen-panel" aria-labelledby="serie-titulo">
            <div className="panel-heading">
              <div><span className="panel-label">EVOLUÇÃO</span><h2 id="serie-titulo">Fotografias mensais</h2></div>
              {podeFotografar && (
                <button type="button" className="outline-button" disabled={ocupado} onClick={() => void fotografar()}
                  title="Grava os indicadores de agora na competência do mês; de novo no mesmo mês, substitui">
                  <Camera size={16} aria-hidden="true" /> Fotografar o mês
                </button>
              )}
            </div>
            {indicadores && <SerieHistorica indicadores={indicadores} />}
          </section>
        </div>
        <div className="coluna-executiva">
          <section className="panel screen-panel" aria-labelledby="qualidade-titulo">
            <div className="panel-heading"><div><span className="panel-label">CADASTRO</span><h2 id="qualidade-titulo">Qualidade cadastral</h2></div></div>
            {indicadores ? <QualidadeCadastral indicadores={indicadores} /> : <p className="vazio">Calculando...</p>}
          </section>
          <section className="panel screen-panel" aria-labelledby="pendencias-titulo">
            <div className="panel-heading"><div><span className="panel-label">AGIR</span><h2 id="pendencias-titulo">Pendências prioritárias</h2></div></div>
            {indicadores ? <PendenciasPrioritarias indicadores={indicadores} /> : <p className="vazio">Calculando...</p>}
          </section>
          <section className="panel screen-panel" aria-labelledby="fila-titulo">
            <div className="panel-heading">
              <div><span className="panel-label">GOVERNANÇA</span><h2 id="fila-titulo">Fila de validação</h2></div>
              <Link className="text-button" href="/validacoes">Ver a fila toda</Link>
            </div>
            {fila ? <FilaResumida itens={fila} /> : <p className="vazio">Carregando a fila...</p>}
          </section>
        </div>
      </div>
    </>
  );
}
