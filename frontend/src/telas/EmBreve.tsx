import { Cabecalho } from '../componentes';
import type { ItemDeMenu } from '../navegacao';
import { Link } from '../roteador';

/** Tela do menu que ainda não foi portada do painel-ddd: diz o que vem e quando. */
export function EmBreve({ item }: { item: ItemDeMenu }) {
  return (
    <>
      <Cabecalho rotulo={item.grupo?.toUpperCase() ?? 'CATÁLOGO'} titulo={item.rotulo} texto={item.descricao} />
      <div className="panel screen-panel aviso-fase">
        <span className="panel-label">EM CONSTRUÇÃO</span>
        <p>Esta tela chega na <strong>fase {item.fase}</strong> do plano de paridade com o painel-ddd. O que cada fase entrega está em <code>docs/PARIDADE.md</code>.</p>
        <Link className="outline-button" href="/">Voltar à visão executiva</Link>
      </div>
    </>
  );
}

export function NaoEncontrada() {
  return (
    <>
      <Cabecalho rotulo="404" titulo="Página não encontrada" texto="O endereço não corresponde a nenhuma tela." />
      <Link className="outline-button" href="/">Ir para a visão executiva</Link>
    </>
  );
}
