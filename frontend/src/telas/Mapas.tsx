import { useEffect, useState } from 'react';
import { consultarArvore, type NoDaArvore } from '../api';
import { Cabecalho, Status } from '../componentes';
import { Link } from '../roteador';

/**
 * A árvore só com os ramos que levam a algo que casa com o filtro: o nó que
 * casa fica com os filhos todos; o que não casa só fica se algum descendente casar.
 */
export function filtrarArvore(nos: NoDaArvore[], termo: string): NoDaArvore[] {
  const limpo = termo.trim().toLowerCase();
  if (!limpo) return nos;
  const casa = (n: NoDaArvore) => `${n.ativo.codigo} ${n.ativo.nome}`.toLowerCase().includes(limpo);
  return nos.flatMap((n) => {
    if (casa(n)) return [n];
    const filhos = filtrarArvore(n.filhos, termo);
    return filhos.length ? [{ ...n, filhos }] : [];
  });
}

function Ramo({ no, aberto }: { no: NoDaArvore; aberto: boolean }) {
  const rotulo = (
    <>
      <Link href={`/ativos/${no.ativo.id}`}>{no.ativo.nome}</Link>
      <span className="discreto mono"> {no.ativo.codigo}</span> <Status status={no.ativo.status} />
      {no.ativo.tipo === 'capacidade' && (
        <span className={no.implementacoes === 0 ? 'discreto alerta-texto' : 'discreto'}> · {no.implementacoes} implementação(ões)</span>
      )}
    </>
  );
  if (no.filhos.length === 0) return <li className="folha">{rotulo}</li>;
  return (
    <li>
      <details open={aberto}>
        <summary>{rotulo}</summary>
        <ul>{no.filhos.map((f) => <Ramo key={f.ativo.id} no={f} aberto={aberto} />)}</ul>
      </details>
    </li>
  );
}

/** As raízes e os descendentes; com filtro, tudo aberto para mostrar onde está o que casou. */
export function ArvoreDoCatalogo({ raizes, filtro }: { raizes: NoDaArvore[]; filtro: string }) {
  const visiveis = filtrarArvore(raizes, filtro);
  if (raizes.length === 0) return <p className="vazio">Nenhum ativo nesta árvore ainda.</p>;
  if (visiveis.length === 0) return <p className="vazio">Nada na árvore casa com “{filtro}”.</p>;
  return <ul className="arvore">{visiveis.map((r) => <Ramo key={r.ativo.id} no={r} aberto={!!filtro.trim() || visiveis.length <= 3} />)}</ul>;
}

type Props = { visao: 'negocio' | 'tecnica'; onErro: (mensagem: string) => void };

// Os mapas do painel (/mapa): a árvore de negócio (Domínio → Capacidade) e a
// técnica (Sistema → Endpoint), com busca dentro da árvore. As relações que
// cruzam as árvores estão no grafo de cada ativo.
export function Mapas({ visao, onErro }: Props) {
  const [raizes, setRaizes] = useState<NoDaArvore[] | null>(null);
  const [filtro, setFiltro] = useState('');

  useEffect(() => {
    setRaizes(null);
    consultarArvore(visao).then((r) => setRaizes(r.raizes)).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar o mapa.'));
  }, [visao]);

  return (
    <>
      <Cabecalho rotulo="INVENTÁRIO" titulo="Mapas" texto="A hierarquia do catálogo. As relações que cruzam domínios e sistemas aparecem no grafo de cada ativo." />
      <nav className="ficha-tabs" aria-label="Visões do mapa">
        <Link href="/mapas" className={visao === 'negocio' ? 'ficha-tab active' : 'ficha-tab'} aria-selected={visao === 'negocio'} role="tab">Negócio</Link>
        <Link href="/mapas?visao=tecnica" className={visao === 'tecnica' ? 'ficha-tab active' : 'ficha-tab'} aria-selected={visao === 'tecnica'} role="tab">Técnica</Link>
      </nav>
      <section className="panel screen-panel">
        <label className="busca-na-arvore">Buscar na árvore
          <input id="filtro-arvore" type="search" value={filtro} onChange={(e) => setFiltro(e.target.value)} placeholder="Nome ou código" />
        </label>
        {raizes ? <ArvoreDoCatalogo raizes={raizes} filtro={filtro} /> : <p className="vazio">Carregando o mapa...</p>}
      </section>
    </>
  );
}
