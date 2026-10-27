import { useMemo, useState } from 'react';
import { marked } from 'marked';
// Fonte única: o mesmo docs/CARTILHA.md que orienta quem lê o repositório.
// Nada de copiar o texto para dentro do frontend — é exatamente o que a
// própria cartilha pede para não acontecer (ver o aviso no topo do arquivo).
import cartilhaMd from '../../../docs/CARTILHA.md?raw';
import { Cabecalho } from '../componentes';

type TokenMarked = ReturnType<typeof marked.lexer>[number];
type SecaoCartilha = { titulo: string; html: string };

// Um item de menu por seção (## do markdown).
export function dividirCartilhaEmSecoes(md: string): SecaoCartilha[] {
  const grupos: { titulo: string; tokens: TokenMarked[] }[] = [{ titulo: 'Visão geral', tokens: [] }];
  for (const token of marked.lexer(md)) {
    if (token.type === 'heading' && token.depth === 2) grupos.push({ titulo: token.text, tokens: [] });
    else grupos[grupos.length - 1].tokens.push(token);
  }
  return grupos
    .filter((grupo) => grupo.tokens.length > 0)
    .map((grupo) => ({ titulo: grupo.titulo, html: marked.parser(grupo.tokens) as string }));
}

// Página de leitura: nenhum formulário de escrita, para nenhum papel.
export function Cartilha() {
  const secoes = useMemo(() => dividirCartilhaEmSecoes(cartilhaMd), []);
  const [ativa, setAtiva] = useState(0);

  return (
    <>
      <Cabecalho rotulo="GUIA DE USO POR PAPEL" titulo="Cartilha" texto="O que cada papel faz na ferramenta, e o rito de cada tipo de ativo." />
      <div className="panel screen-panel cartilha-shell">
        <nav className="cartilha-nav" aria-label="Seções da cartilha">
          {secoes.map((secao, indice) => (
            <button
              type="button"
              key={secao.titulo}
              className={indice === ativa ? 'cartilha-nav-item active' : 'cartilha-nav-item'}
              aria-current={indice === ativa ? 'true' : undefined}
              onClick={() => setAtiva(indice)}
            >
              {secao.titulo}
            </button>
          ))}
        </nav>
        <div className="cartilha-pane">
          <div className="cartilha-content" dangerouslySetInnerHTML={{ __html: secoes[ativa]?.html ?? '' }} />
        </div>
      </div>
    </>
  );
}
