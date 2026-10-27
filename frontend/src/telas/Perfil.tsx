import { Check } from 'lucide-react';
import { Cabecalho } from '../componentes';
import { Link } from '../roteador';
import { empresaDe, empresas, iniciaisDe, unidadeDe, usuariosDeTeste, type UsuarioDeTeste } from '../usuarios';

type Props = { usuario: UsuarioDeTeste; onTrocar: (usuario: UsuarioDeTeste) => void };

// Hoje o perfil mostra quem você é e deixa trocar de usuário de teste. O
// cadastro com matrícula, os papéis concedidos e o pedido de papel (pleito)
// chegam na fase 2, junto com o modo de demonstração (docs/PARIDADE.md).
export function Perfil({ usuario, onTrocar }: Props) {
  const empresa = empresaDe(usuario);
  const unidade = unidadeDe(usuario);

  return (
    <>
      <Cabecalho rotulo="VOCÊ" titulo="Seu perfil" texto="Quem assina o que você cadastra, submete e decide." />

      <section className="panel screen-panel perfil-cartao" aria-labelledby="perfil-nome">
        <span className="avatar avatar-grande" aria-hidden="true">{iniciaisDe(usuario.nome)}</span>
        <div>
          <h2 id="perfil-nome">{usuario.nome}</h2>
          <dl className="perfil-dados">
            <div><dt>Papel</dt><dd>{usuario.rotuloPapel}</dd></div>
            <div><dt>Empresa</dt><dd>{empresa.nome}</dd></div>
            <div><dt>Unidade</dt><dd>{unidade.nome}</dd></div>
          </dl>
          <p className="muted">O que o seu papel permite fazer está na <Link href="/cartilha">cartilha</Link>.</p>
        </div>
      </section>

      <section className="panel screen-panel" aria-labelledby="troca-titulo">
        <div className="panel-heading">
          <div><span className="panel-label">AMBIENTE DE TESTE</span><h2 id="troca-titulo">Usar como outra pessoa</h2></div>
        </div>
        <p className="muted">Ainda não há identidade corporativa (ADR-0007). Troque de pessoa para testar papéis, a segregação de função (quem submete não decide) e o isolamento entre empresas.</p>
        <div className="troca-grade">
          {empresas.map((e) => (
            <div key={e.id}>
              <h3 className="troca-empresa">{e.nome}</h3>
              <ul className="troca-lista">
                {usuariosDeTeste.filter((u) => u.empresaId === e.id).map((candidato) => (
                  <li key={candidato.id}>
                    <button
                      type="button"
                      className={candidato.id === usuario.id ? 'switch-user active' : 'switch-user'}
                      aria-pressed={candidato.id === usuario.id}
                      onClick={() => onTrocar(candidato)}
                    >
                      <span className="avatar" aria-hidden="true">{iniciaisDe(candidato.nome)}</span>
                      <span><strong>{candidato.nome}</strong><small>{candidato.rotuloPapel} · {unidadeDe(candidato).nome}</small></span>
                      {candidato.id === usuario.id && <Check size={14} className="switch-check" aria-hidden="true" />}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </section>
    </>
  );
}
