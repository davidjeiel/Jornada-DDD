import { FilePlus2 } from 'lucide-react';
import type { AtivoResumo, Validacao } from '../api';
import { Cabecalho, Metric } from '../componentes';
import { Link } from '../roteador';

type Props = { ativos: AtivoResumo[]; validacoes: Validacao[]; nomeDaEmpresa: string; podeEscrever: boolean };

// Os indicadores aqui são contagens do que existe hoje. KPIs que levam ao
// recorte, cobertura por domínio, snapshots e série histórica chegam na fase 7.
export function VisaoExecutiva({ ativos, validacoes, nomeDaEmpresa, podeEscrever }: Props) {
  const publicados = ativos.filter((ativo) => ativo.status === 'publicado').length;
  const emValidacao = ativos.filter((ativo) => ativo.status === 'em_validacao').length;
  const rascunhos = ativos.filter((ativo) => ativo.status === 'rascunho' || ativo.status === 'em_revisao').length;
  const abertas = validacoes.filter((validacao) => validacao.status === 'aberta').length;
  const tipos = [...new Set(ativos.map((ativo) => ativo.tipo))].sort();
  const taxa = ativos.length ? Math.round((publicados / ativos.length) * 100) : 0;

  return (
    <>
      <Cabecalho
        rotulo={nomeDaEmpresa.toUpperCase()}
        titulo="Visão executiva"
        texto="A saúde do catálogo e da governança, num relance."
        acoes={podeEscrever && <Link className="primary-button" href="/novo"><FilePlus2 size={17} aria-hidden="true" /> Novo ativo</Link>}
      />

      <div className="metrics">
        <Metric label="Ativos no catálogo" value={ativos.length} accent="ink" note="total registrado" />
        <Metric label="Publicados" value={publicados} accent="mint" note={`${taxa}% do catálogo`} />
        <Metric label="Em validação" value={emValidacao} accent="amber" note={`${abertas} etapas aguardando decisão`} />
        <Metric label="Em rascunho" value={rascunhos} accent="coral" note="ainda não submetidos" />
      </div>

      <div className="screen-grid governance-screen">
        <Link className="panel screen-panel governance-summary" href="/validacoes">
          <span className="panel-label">FILA DE GOVERNANÇA</span>
          <strong>{abertas}</strong>
          <p>etapas de validação aguardando decisão</p>
          <div className="governance-bars"><span style={{ width: `${validacoes.length ? (abertas / validacoes.length) * 100 : 0}%` }} /></div>
          <small>{validacoes.length - abertas} encerradas</small>
        </Link>
        <section className="panel screen-panel" aria-labelledby="composicao-titulo">
          <div className="panel-heading"><div><span className="panel-label">DISTRIBUIÇÃO</span><h2 id="composicao-titulo">Composição do catálogo</h2></div><span className="result-count">{tipos.length} tipos</span></div>
          <div className="indicator-list">
            {tipos.map((tipo) => {
              const total = ativos.filter((ativo) => ativo.tipo === tipo).length;
              return <div className="indicator-line" key={tipo}><span>{tipo}</span><div className="bar"><i style={{ width: `${(total / ativos.length) * 100}%` }} /></div><b>{total}</b></div>;
            })}
          </div>
        </section>
      </div>
    </>
  );
}
