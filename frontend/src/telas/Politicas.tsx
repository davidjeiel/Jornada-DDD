import { useEffect, useState } from 'react';
import { consultarCartilha, type Rito } from '../api';
import { Cabecalho } from '../componentes';
import { Link } from '../roteador';
import { TabelaDeRitos } from './Cartilha';

// A página de políticas do painel (/politicas): o rito de validação por tipo e
// criticidade, lido das políticas que a API aplica, a mesma tabela da cartilha.
export function Politicas({ onErro }: { onErro: (mensagem: string) => void }) {
  const [ritos, setRitos] = useState<Rito[] | null>(null);

  useEffect(() => {
    consultarCartilha().then((d) => setRitos(d.ritos)).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar as políticas.'));
  }, []);

  return (
    <>
      <Cabecalho rotulo="GOVERNANÇA" titulo="Políticas de governança"
        texto="Governança proporcional: cada tipo e criticidade tem o seu rito. Marcar um ativo como crítico aperta o rito de verdade." />
      <section className="panel screen-panel">
        {ritos ? <TabelaDeRitos ritos={ritos} /> : <p className="vazio">Carregando as políticas...</p>}
        <p className="discreto">O prazo vale para cada etapa, contado da submissão; vencido, a etapa aparece assim na <Link href="/validacoes">fila de validação</Link>. Quem decide cada etapa está na <Link href="/cartilha">cartilha</Link>.</p>
      </section>
    </>
  );
}
