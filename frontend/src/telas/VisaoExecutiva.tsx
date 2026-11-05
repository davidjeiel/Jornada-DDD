import { useEffect, useState } from 'react';
import { Camera, FilePlus2 } from 'lucide-react';
import { consultarIndicadores, fotografarIndicadores, type Indicadores, type Validacao } from '../api';
import { Cabecalho } from '../componentes';
import { Link } from '../roteador';

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

type Props = {
  validacoes: Validacao[];
  nomeDaEmpresa: string;
  podeEscrever: boolean;
  podeFotografar: boolean;
  onErro: (mensagem: string) => void;
};

// A visão executiva do painel (o dashboard): os indicadores que levam ao
// recorte, a variação contra a competência anterior e a série das fotografias.
export function VisaoExecutiva({ validacoes, nomeDaEmpresa, podeEscrever, podeFotografar, onErro }: Props) {
  const [indicadores, setIndicadores] = useState<Indicadores | null>(null);
  const [versao, setVersao] = useState(0);
  const [ocupado, setOcupado] = useState(false);

  useEffect(() => {
    consultarIndicadores().then(setIndicadores).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar os indicadores.'));
  }, [versao]);

  async function fotografar() {
    setOcupado(true);
    onErro('');
    try { await fotografarIndicadores(); setVersao((v) => v + 1); }
    catch (e) { onErro(e instanceof Error ? e.message : 'Não foi possível fotografar os indicadores.'); }
    finally { setOcupado(false); }
  }

  const abertas = validacoes.filter((v) => v.status === 'aberta').length;
  const atuais = indicadores?.atuais ?? {};
  return (
    <>
      <Cabecalho
        rotulo={nomeDaEmpresa.toUpperCase()}
        titulo="Visão executiva"
        texto="A saúde do catálogo e da governança, num relance. Cada número leva ao recorte que o explica."
        acoes={podeEscrever && <Link className="primary-button" href="/novo"><FilePlus2 size={17} aria-hidden="true" /> Novo ativo</Link>}
      />

      {indicadores ? <CartoesDosIndicadores indicadores={indicadores} /> : <p className="vazio">Calculando os indicadores...</p>}

      <div className="screen-grid governance-screen">
        <Link className="panel screen-panel governance-summary" href="/validacoes">
          <span className="panel-label">FILA DE GOVERNANÇA</span>
          <strong>{abertas}</strong>
          <p>etapas de validação aguardando decisão</p>
          <div className="governance-bars"><span style={{ width: `${validacoes.length ? (abertas / validacoes.length) * 100 : 0}%` }} /></div>
          <small>{atuais.sla_vencido ?? 0} com o prazo vencido · {atuais.dependencias_criticas ?? 0} dependência(s) crítica(s) no catálogo</small>
        </Link>
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
    </>
  );
}
