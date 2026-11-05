import { useEffect, useState } from 'react';
import { consultarMesa, type AtivoResumo, type ItemDaFila, type Mesa as DadosDaMesa } from '../api';
import { Cabecalho, Criticidade, Status } from '../componentes';
import { Link } from '../roteador';
import { caminhoDaFila } from './Validacoes';

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

function Ativos({ ativos, vazio }: { ativos: AtivoResumo[]; vazio: string }) {
  if (ativos.length === 0) return <p className="vazio">{vazio}</p>;
  return (
    <ul className="lista-simples">
      {ativos.map((a) => <li key={a.id}><Link href={`/ativos/${a.id}`}>{a.codigo} · {a.nome}</Link> <Status status={a.status} /></li>)}
    </ul>
  );
}

/** As quatro listas da mesa (servicos.minha_mesa), cada uma com o seu vazio. */
export function QuadroDaMesa({ mesa }: { mesa: DadosDaMesa }) {
  return (
    <div className="grade-da-mesa">
      <section className="panel screen-panel" aria-labelledby="mesa-minhas"><span className="panel-label" id="mesa-minhas">Minhas validações ({mesa.minhasValidacoes.length})</span>
        <Etapas itens={mesa.minhasValidacoes} vazio="Nenhuma etapa assumida por você." /></section>
      <section className="panel screen-panel" aria-labelledby="mesa-livre"><span className="panel-label" id="mesa-livre">Fila livre ({mesa.filaLivre.length})</span>
        <Etapas itens={mesa.filaLivre} vazio="Nenhuma etapa esperando quem a assuma." /></section>
      <section className="panel screen-panel" aria-labelledby="mesa-rascunhos"><span className="panel-label" id="mesa-rascunhos">Meus rascunhos ({mesa.meusRascunhos.length})</span>
        <Ativos ativos={mesa.meusRascunhos} vazio="Nada cadastrado por você em rascunho ou revisão." /></section>
      <section className="panel screen-panel" aria-labelledby="mesa-aguardando"><span className="panel-label" id="mesa-aguardando">Aguardando decisão ({mesa.aguardandoDecisao.length})</span>
        <Ativos ativos={mesa.aguardandoDecisao} vazio="Nada seu esperando validação." /></section>
    </div>
  );
}

// "Minha mesa" do painel (/meu-trabalho): o trabalho que é seu, em um lugar só.
export function Mesa({ onErro }: { onErro: (mensagem: string) => void }) {
  const [mesa, setMesa] = useState<DadosDaMesa | null>(null);
  useEffect(() => {
    consultarMesa().then(setMesa).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar a mesa.'));
  }, []);
  return (
    <>
      <Cabecalho rotulo="TRABALHAR" titulo="Minha mesa" texto="O que você assumiu, o que está livre para assumir, e o que você cadastrou." />
      {mesa ? <QuadroDaMesa mesa={mesa} /> : <p className="vazio">Carregando a mesa...</p>}
    </>
  );
}
