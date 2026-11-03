import { Check } from 'lucide-react';
import type { Perfil as DadosDoPerfil, Pessoa } from '../api';
import { Cabecalho } from '../componentes';
import type { Identidade } from '../identidade';
import { rotuloDoPapel, situacaoDoPleito } from '../papeis';
import { Link } from '../roteador';
import { empresaDe, empresas, iniciaisDe } from '../usuarios';

type Props = {
  perfil: DadosDoPerfil | null;
  identidade: Identidade;
  pessoas: Pessoa[];
  onIdentificar: (pessoaId: string | null) => void;
  onTrocarEmpresa: (empresaId: string) => void;
  onModo: (leitura: boolean) => void;
};

function quando(iso: string | null): string {
  return iso ? new Date(iso).toLocaleDateString('pt-BR') : '—';
}

// Quem você é e em que modo trabalha. Portado de /perfil do painel-ddd:
// enquanto a identidade corporativa não chega (ADR-0007), a pessoa é escolhida
// aqui; quando chegar, esta tela perde o seletor e mantém o resto. Os papéis
// são os concedidos (ADR-0018): para ter outro, peça em "Solicitar acesso".
export function Perfil({ perfil, identidade, pessoas, onIdentificar, onTrocarEmpresa, onModo }: Props) {
  const pessoa = perfil?.pessoa ?? null;
  const empresa = empresaDe(identidade.empresaId);

  return (
    <>
      <Cabecalho rotulo="VOCÊ" titulo="Seu perfil" texto="Quem assina o que você cadastra, submete e decide." />

      <section className="panel screen-panel perfil-cartao" aria-labelledby="perfil-nome">
        {pessoa ? (
          <>
            <span className="avatar avatar-grande" aria-hidden="true">{iniciaisDe(pessoa.nome)}</span>
            <div>
              <h2 id="perfil-nome">{pessoa.nome}</h2>
              <dl className="perfil-dados">
                <div><dt>Matrícula</dt><dd className="mono">{pessoa.matricula}</dd></div>
                <div><dt>Unidade</dt><dd className="mono">{pessoa.unidade}</dd></div>
                <div><dt>E-mail</dt><dd>{pessoa.email}</dd></div>
                <div><dt>Empresa</dt><dd>{empresa.nome}</dd></div>
              </dl>
              <p className="muted">O que cada papel permite fazer está na <Link href="/cartilha">cartilha</Link>.</p>
            </div>
          </>
        ) : (
          <div>
            <h2 id="perfil-nome">Você ainda não se identificou</h2>
            <p className="muted">Sem identificação você consulta o catálogo inteiro, mas não cadastra, submete nem decide nada. Escolha quem você é abaixo ou, se ainda não tem cadastro, <Link href="/acesso/solicitar">cadastre-se e peça um papel</Link>.</p>
          </div>
        )}
      </section>

      <section className="panel screen-panel" aria-labelledby="papeis-titulo">
        <div className="panel-heading">
          <div><span className="panel-label">ACESSO</span><h2 id="papeis-titulo">Seus papéis</h2></div>
          {pessoa && <Link className="outline-button" href="/acesso/solicitar">Pedir um papel</Link>}
        </div>
        {perfil && perfil.papeis.length > 0 ? (
          <div className="rolagem">
            <table className="tabela">
              <thead><tr><th scope="col">Papel</th><th scope="col">Vale em</th><th scope="col">Escreve em</th><th scope="col">Desde</th></tr></thead>
              <tbody>
                {perfil.papeis.map((p) => (
                  <tr key={`${p.papel}-${p.escopoTipo}-${p.escopoId ?? ''}`}>
                    <td>{p.rotulo}</td>
                    <td>{p.escopoRotulo}</td>
                    <td>{p.blocos.length > 0 ? p.blocos.join(', ') : 'só consulta'}</td>
                    <td className="sem-quebra">{quando(p.desde)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="vazio">Nenhum papel concedido: você consulta o catálogo inteiro. Um papel de escrita sai de um pleito decidido por um curador ou administrador.</p>
        )}
      </section>

      {pessoa && (
        <section className="panel screen-panel" aria-labelledby="pleitos-titulo">
          <span className="panel-label" id="pleitos-titulo">Seus pleitos</span>
          {perfil && perfil.pleitos.length > 0 ? (
            <div className="rolagem">
              <table className="tabela">
                <thead><tr><th scope="col">Pedido em</th><th scope="col">Papel pedido</th><th scope="col">Situação</th><th scope="col">Resposta</th></tr></thead>
                <tbody>
                  {perfil.pleitos.map((p) => (
                    <tr key={p.id}>
                      <td className="sem-quebra">{quando(p.criadoEm)}</td>
                      <td>{rotuloDoPapel(p.papelPleiteado)}</td>
                      <td>
                        <span className={`status status-pleito-${p.status}`}>{situacaoDoPleito(p.status)}</span>
                        {p.status === 'aprovado' && <span className="discreto"> {rotuloDoPapel(p.papelConcedido)}, {p.escopoRotulo}</span>}
                      </td>
                      <td className="discreto">{p.resposta || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="vazio">Nenhum pleito. <Link href="/acesso/solicitar">Pedir um papel</Link>.</p>
          )}
        </section>
      )}

      <section className="panel screen-panel" aria-labelledby="modo-titulo">
        <div className="panel-heading">
          <div><span className="panel-label">MODO DE TRABALHO</span><h2 id="modo-titulo">Edição ou leitura</h2></div>
        </div>
        <p className="muted">Em modo de leitura nada que escreve aparece, e a API recusa toda escrita, mesmo que o seu papel permita. Serve para navegar sem risco de alterar algo.</p>
        <div className="modos" role="group" aria-label="Modo de trabalho">
          <button type="button" className={identidade.modoLeitura ? 'outline-button' : 'primary-button'} aria-pressed={!identidade.modoLeitura} onClick={() => onModo(false)}>Edição</button>
          <button type="button" className={identidade.modoLeitura ? 'primary-button' : 'outline-button'} aria-pressed={identidade.modoLeitura} onClick={() => onModo(true)}>Leitura</button>
        </div>
      </section>

      <section className="panel screen-panel" aria-labelledby="troca-titulo">
        <div className="panel-heading">
          <div><span className="panel-label">IDENTIFICAÇÃO</span><h2 id="troca-titulo">Quem é você</h2></div>
        </div>
        <p className="muted">Ainda não há identidade corporativa (ADR-0007): escolha a pessoa cadastrada que você é. Os papéis são os concedidos a ela.</p>
        {perfil?.modoDemonstracao && (
          <div className="modos" role="group" aria-label="Empresa">
            {empresas.map((e) => (
              <button key={e.id} type="button" className={e.id === identidade.empresaId ? 'primary-button' : 'outline-button'}
                aria-pressed={e.id === identidade.empresaId} onClick={() => onTrocarEmpresa(e.id)}>{e.nome}</button>
            ))}
          </div>
        )}
        <ul className="troca-lista troca-grade-unica">
          {pessoas.map((candidata) => (
            <li key={candidata.id}>
              <button
                type="button"
                className={candidata.id === identidade.pessoaId ? 'switch-user active' : 'switch-user'}
                aria-pressed={candidata.id === identidade.pessoaId}
                onClick={() => onIdentificar(candidata.id)}
              >
                <span className="avatar" aria-hidden="true">{iniciaisDe(candidata.nome)}</span>
                <span><strong>{candidata.nome}</strong><small>{candidata.matricula} · {candidata.papeis.map(rotuloDoPapel).join(', ') || 'sem papel'}</small></span>
                {candidata.id === identidade.pessoaId && <Check size={14} className="switch-check" aria-hidden="true" />}
              </button>
            </li>
          ))}
          <li>
            <button type="button" className={identidade.pessoaId ? 'switch-user' : 'switch-user active'} aria-pressed={!identidade.pessoaId} onClick={() => onIdentificar(null)}>
              <span className="avatar" aria-hidden="true">?</span>
              <span><strong>Ninguém</strong><small>só consultar, sem me identificar</small></span>
            </button>
          </li>
        </ul>
      </section>
    </>
  );
}
