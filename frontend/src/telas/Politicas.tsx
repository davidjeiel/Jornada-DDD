import { useEffect, useState } from 'react';
import { consultarCartilha, type Mudanca, type Rito } from '../api';
import { Cabecalho } from '../componentes';
import { Link } from '../roteador';
import { TabelaDeRitos } from './Cartilha';

const EXIGENCIA: Record<string, string> = { obrigatoria: 'obrigatória', condicional: 'condicional', opcional: 'opcional', nao: 'não se aplica' };

/** A matriz mínima de aprovação por tipo de mudança (governanca.MATRIZ_MUDANCA). */
export function MatrizDeMudanca({ linhas }: { linhas: Mudanca[] }) {
  return (
    <div className="rolagem">
      <table className="tabela matriz-de-mudanca">
        <thead><tr><th scope="col">Mudança</th><th scope="col">Negocial</th><th scope="col">Técnica</th><th scope="col">Arquitetural</th></tr></thead>
        <tbody>
          {linhas.map((l) => (
            <tr key={l.mudanca}>
              <th scope="row">{l.rotulo}</th>
              {[l.negocial, l.tecnica, l.arquitetural].map((v, i) => <td key={i} className={`exigencia-${v}`}>{EXIGENCIA[v] ?? v}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// A página de políticas do painel (/politicas): o rito de validação por tipo e
// criticidade, lido das políticas que a API aplica (a mesma tabela da
// cartilha), e a matriz mínima de aprovação por tipo de mudança.
export function Politicas({ onErro }: { onErro: (mensagem: string) => void }) {
  const [ritos, setRitos] = useState<Rito[] | null>(null);
  const [matriz, setMatriz] = useState<Mudanca[] | null>(null);

  useEffect(() => {
    consultarCartilha().then((d) => { setRitos(d.ritos); setMatriz(d.matrizDeMudanca); })
      .catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar as políticas.'));
  }, []);

  return (
    <>
      <Cabecalho rotulo="GOVERNANÇA" titulo="Políticas de governança"
        texto="Governança proporcional: cada tipo e criticidade tem o seu rito. Marcar um ativo como crítico aperta o rito de verdade." />
      <section className="panel screen-panel">
        {ritos ? <TabelaDeRitos ritos={ritos} /> : <p className="vazio">Carregando as políticas...</p>}
        <p className="discreto">O prazo vale para cada etapa, contado da submissão; vencido, a etapa aparece assim na <Link href="/validacoes">fila de validação</Link>. Quem decide cada etapa está na <Link href="/cartilha">cartilha</Link>.</p>
      </section>
      <section className="panel screen-panel" aria-labelledby="matriz-titulo">
        <div className="panel-heading"><div><span className="panel-label">REFERÊNCIA</span><h2 id="matriz-titulo">Matriz mínima de aprovação por tipo de mudança</h2></div></div>
        {matriz ? <MatrizDeMudanca linhas={matriz} /> : <p className="vazio">Carregando a matriz...</p>}
        <p className="discreto">Para cada tipo de mudança, o que cada etapa de validação precisa olhar. O rito aplicado continua sendo o da política do tipo e da criticidade, acima.</p>
      </section>
    </>
  );
}
