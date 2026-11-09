import { useEffect, useState } from 'react';
import { consultarMesa, type ItemDaFila, type ItemDoCatalogo, type Mesa as DadosDaMesa, type Squad } from '../api';
import { Cabecalho, Criticidade, Status } from '../componentes';
import { Link } from '../roteador';
import { classeDoScore } from './Catalogo';
import { caminhoDaFila } from './Validacoes';

const data = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

function Etapas({ itens, vazio }: { itens: ItemDaFila[]; vazio: string }) {
  if (itens.length === 0) return <p className="vazio">{vazio}</p>;
  return (
    <ul className="lista-simples">
      {itens.map(({ validacao, ativo, slaRestante }) => (
        <li key={validacao.id}>
          <Link href={caminhoDaFila(validacao.etapa, validacao.id)}>{ativo.codigo} · {ativo.nome}</Link>
          <span className="discreto"> · etapa {validacao.etapa} · {slaRestante}</span> <Criticidade valor={ativo.criticidade} />
        </li>
      ))}
    </ul>
  );
}

/** Os ativos da mesa como a tabela do painel: tipo, status, qualidade, squad e a última alteração. */
function Ativos({ itens, squads, vazio }: { itens: ItemDoCatalogo[]; squads: Squad[]; vazio: string }) {
  if (itens.length === 0) return <p className="vazio">{vazio}</p>;
  return (
    <div className="rolagem">
      <table className="tabela">
        <thead><tr><th scope="col">Ativo</th><th scope="col">Tipo</th><th scope="col">Status</th><th scope="col">Qualidade</th><th scope="col">Squad</th><th scope="col">Atualizado</th></tr></thead>
        <tbody>
          {itens.map(({ ativo, score, idSquad, atualizadoEm }) => (
            <tr key={ativo.id}>
              <td><Link href={`/ativos/${ativo.id}`}>{ativo.nome}</Link><div className="mono discreto">{ativo.codigo}</div></td>
              <td>{ativo.tipo}</td>
              <td><Status status={ativo.status} /></td>
              <td>{score === null ? '—' : <span className={`score ${classeDoScore(score)}`}>{score}%</span>}</td>
              <td>{squads.find((s) => s.id === idSquad)?.sigla ?? '—'}</td>
              <td className="discreto">{data.format(new Date(atualizadoEm))}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** As quatro listas da mesa (servicos.minha_mesa), cada uma com o seu vazio. */
export function QuadroDaMesa({ mesa, squads = [] }: { mesa: DadosDaMesa; squads?: Squad[] }) {
  return (
    <div className="grade-da-mesa">
      <section className="panel screen-panel" aria-labelledby="mesa-minhas"><span className="panel-label" id="mesa-minhas">Minhas validações ({mesa.minhasValidacoes.length})</span>
        <Etapas itens={mesa.minhasValidacoes} vazio="Nenhuma etapa assumida por você." /></section>
      <section className="panel screen-panel" aria-labelledby="mesa-livre"><span className="panel-label" id="mesa-livre">Fila livre ({mesa.filaLivre.length})</span>
        <Etapas itens={mesa.filaLivre} vazio="Nenhuma etapa esperando quem a assuma." /></section>
      <section className="panel screen-panel mesa-larga" aria-labelledby="mesa-rascunhos"><span className="panel-label" id="mesa-rascunhos">Meus rascunhos ({mesa.meusRascunhos.length})</span>
        <Ativos itens={mesa.meusRascunhos} squads={squads} vazio="Nada cadastrado por você em rascunho ou revisão." /></section>
      <section className="panel screen-panel mesa-larga" aria-labelledby="mesa-aguardando"><span className="panel-label" id="mesa-aguardando">Aguardando decisão ({mesa.aguardandoDecisao.length})</span>
        <Ativos itens={mesa.aguardandoDecisao} squads={squads} vazio="Nada seu esperando validação." /></section>
    </div>
  );
}

// "Minha mesa" do painel (/meu-trabalho): o trabalho que é seu, em um lugar só.
export function Mesa({ squads, onErro }: { squads: Squad[]; onErro: (mensagem: string) => void }) {
  const [mesa, setMesa] = useState<DadosDaMesa | null>(null);
  useEffect(() => {
    consultarMesa().then(setMesa).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar a mesa.'));
  }, []);
  return (
    <>
      <Cabecalho rotulo="TRABALHAR" titulo="Minha mesa" texto="O que você assumiu, o que está livre para assumir, e o que você cadastrou." />
      {mesa ? <QuadroDaMesa mesa={mesa} squads={squads} /> : <p className="vazio">Carregando a mesa...</p>}
    </>
  );
}
