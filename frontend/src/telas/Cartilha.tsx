import { useEffect, useMemo, useState } from 'react';
import { marked } from 'marked';
// Fonte única da prosa: o mesmo docs/CARTILHA.md que orienta quem lê o
// repositório. As TABELAS não moram lá: vêm da API (GET /v1/cartilha), que as
// lê das regras que aplica. Uma cartilha que repete a regra à mão envelhece
// em silêncio na primeira mudança, e uma cartilha errada é pior que nenhuma.
import cartilhaMd from '../../../docs/CARTILHA.md?raw';
import { consultarCartilha, type DadosDaCartilha, type Rito } from '../api';
import { Cabecalho } from '../componentes';

type TokenMarked = ReturnType<typeof marked.lexer>[number];
export type SecaoCartilha = { id: string; titulo: string; html: string };

/** A âncora que o GitHub dá a um título: minúsculas, acentos mantidos, espaço vira hífen. */
export function ancora(titulo: string): string {
  return titulo.trim().toLowerCase().replace(/[^\p{L}\p{N}\s-]/gu, '').replace(/\s/g, '-');
}

/**
 * Os perfis, na ordem do painel (cartilha.PERFIS). A chave dá a cor; a âncora
 * é a do título no markdown, no formato do GitHub, para que os links do próprio
 * CARTILHA.md ("[Negócio](#negócio)") funcionem também aqui.
 */
export const PERFIS = [
  { chave: 'negocio', titulo: 'Negócio', lema: 'Você responde pelo significado' },
  { chave: 'tech', titulo: 'Time técnico', lema: 'Você responde pela realidade técnica' },
  { chave: 'arquiteto', titulo: 'Arquiteto', lema: 'Você responde pela coerência do todo' },
  { chave: 'curador', titulo: 'Curador', lema: 'Você mantém o cadastro vivo' },
  { chave: 'admin', titulo: 'Administrador', lema: 'Você opera a ferramenta e concede acesso' },
  { chave: 'consulta', titulo: 'Consulta', lema: 'Você lê e não altera nada' },
].map((p) => ({ ...p, ancora: ancora(p.titulo) }));

/** O nome curto do papel na coluna da matriz (cartilha.PAPEL_CURTO). */
const PAPEL_CURTO: Record<string, string> = {
  negocio: 'Negócio', techlead: 'Time técnico', arquiteto: 'Arquiteto', curador: 'Curador', admin: 'Admin', consulta: 'Consulta',
};
const ETAPA: Record<string, string> = { negocial: 'Negocial', tecnica: 'Técnica', arquitetural: 'Arquitetural' };
const CRITICIDADE: Record<string, string> = { critica: 'crítica', alta: 'alta', media: 'média', baixa: 'baixa' };

export const SECAO_DAS_TABELAS = 'Tabela de referência';

// Uma seção por ## do markdown, com âncora estável.
export function dividirCartilhaEmSecoes(md: string): SecaoCartilha[] {
  const grupos: { titulo: string; tokens: TokenMarked[] }[] = [{ titulo: 'Visão geral', tokens: [] }];
  for (const token of marked.lexer(md)) {
    if (token.type === 'heading' && token.depth === 2) grupos.push({ titulo: token.text, tokens: [token] });
    else grupos[grupos.length - 1].tokens.push(token);
  }
  return grupos
    .filter((grupo) => grupo.tokens.length > 0)
    .map((grupo) => ({ id: ancora(grupo.titulo), titulo: grupo.titulo, html: marked.parser(grupo.tokens) as string }));
}

function Etapas({ etapas }: { etapas: string[] }) {
  if (etapas.length === 0) return <span className="nao">—</span>;
  return <>{etapas.map((e) => <span key={e} className={`etapa etapa-${e}`}>{ETAPA[e] ?? e}</span>)}</>;
}

/** As três tabelas de referência, como a API as devolve. Nada aqui é escrito à mão. */
export function TabelasDeReferencia({ dados }: { dados: DadosDaCartilha }) {
  return (
    <>
      <h3>Quem pode fazer o quê</h3>
      <div className="rolagem">
        <table className="tabela matriz">
          <thead>
            <tr><th scope="col">Ação</th>{dados.papeis.map((p) => <th scope="col" key={p}>{PAPEL_CURTO[p] ?? p}</th>)}</tr>
          </thead>
          <tbody>
            {dados.matriz.map((linha) => (
              <tr key={linha.acao}>
                <td>{linha.rotulo}</td>
                {dados.papeis.map((p) => (
                  <td key={p}>{linha.papeis[p] ? <span className="sim">sim</span> : <span className="nao" aria-label="não">—</span>}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <h3>Que etapa cada papel decide, e onde escreve</h3>
      <div className="rolagem">
        <table className="tabela">
          <thead><tr><th scope="col">Papel</th><th scope="col">Decide a etapa</th><th scope="col">Escreve em</th></tr></thead>
          <tbody>
            {dados.papeis.map((p) => (
              <tr key={p}>
                <td>{PAPEL_CURTO[p] ?? p}</td>
                <td><Etapas etapas={dados.etapasPorPapel[p] ?? []} /></td>
                <td>{(dados.blocosPorPapel[p] ?? []).join(', ') || <span className="nao">só consulta</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <h3>O rito de cada tipo de ativo</h3>
      <TabelaDeRitos ritos={dados.ritos} />
    </>
  );
}

/** O rito de cada tipo e criticidade, como a política aplica. A cartilha e a página Políticas mostram esta tabela. */
export function TabelaDeRitos({ ritos }: { ritos: Rito[] }) {
  return (
    <div className="rolagem">
      <table className="tabela">
        <thead>
          <tr><th scope="col">Tipo</th><th scope="col">Etapas de validação</th><th scope="col">Evidências mínimas</th><th scope="col">Score mínimo</th><th scope="col">Prazo por etapa</th><th scope="col">Revisar a cada</th></tr>
        </thead>
        <tbody>
          {ritos.map((r) => (
            <tr key={`${r.tipoItem}-${r.criticidade}`}>
              <td>{r.rotulo}{r.criticidade !== '*' && <strong> {CRITICIDADE[r.criticidade] ?? r.criticidade}</strong>}</td>
              <td><Etapas etapas={r.etapas} /></td>
              <td>{r.evidenciaMinima}</td>
              <td>{r.scoreMinimo}%</td>
              <td>{r.slaHoras}h</td>
              <td>{r.revisarACadaDias} dias</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** A página inteira, numa rolagem só, com atalhos por âncora (como a do painel). */
export function PaginaDaCartilha({ secoes, dados }: { secoes: SecaoCartilha[]; dados: DadosDaCartilha | null }) {
  return (
    <div className="panel screen-panel cartilha-shell">
      <nav className="cartilha-nav" aria-label="Seções da cartilha">
        {secoes.map((secao) => <a key={secao.id} className="cartilha-nav-item" href={`#${secao.id}`}>{secao.titulo}</a>)}
      </nav>
      <div className="cartilha-pane cartilha-content">
        <h2>O fluxo de cada perfil</h2>
        <div className="perfis-grade">
          {PERFIS.map((p) => (
            <a key={p.chave} className={`cartao-perfil perfil-${p.chave}`} href={`#${p.ancora}`}>
              <strong>{p.titulo}</strong><span>{p.lema}</span>
            </a>
          ))}
        </div>
        {secoes.map((secao) => {
          const perfil = PERFIS.find((p) => p.ancora === secao.id);
          return (
            <section key={secao.id} id={secao.id} className={perfil ? `cartilha-secao perfil-${perfil.chave}` : 'cartilha-secao'}>
              <div dangerouslySetInnerHTML={{ __html: secao.html }} />
              {secao.titulo === SECAO_DAS_TABELAS && (dados ? <TabelasDeReferencia dados={dados} /> : <p className="vazio">Carregando as tabelas...</p>)}
            </section>
          );
        })}
      </div>
    </div>
  );
}

// Página de leitura: nenhum formulário de escrita, para nenhum papel. Abre
// sem identificação: quem ainda não disse quem é, é quem mais precisa do guia.
export function Cartilha() {
  const secoes = useMemo(() => dividirCartilhaEmSecoes(cartilhaMd), []);
  const [dados, setDados] = useState<DadosDaCartilha | null>(null);

  useEffect(() => {
    consultarCartilha().then(setDados).catch(() => setDados(null));
  }, []);

  return (
    <>
      <Cabecalho rotulo="GUIA DE USO POR PAPEL" titulo="Cartilha" texto="O que cada papel faz na ferramenta, e o rito de cada tipo de ativo." />
      <PaginaDaCartilha secoes={secoes} dados={dados} />
    </>
  );
}
