import { useState } from 'react';
import { Check } from 'lucide-react';
import { decidirValidacao, type AtivoResumo, type Validacao } from '../api';
import { Cabecalho } from '../componentes';
import { Link } from '../roteador';

type Props = {
  validacoes: Validacao[];
  ativos: AtivoResumo[];
  podeDecidir: boolean;
  onAlterado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

// Assumir/liberar, abas com contagem, painel do validador e "decidir e seguir
// para a próxima" chegam na fase 4. Quem pode decidir cada etapa é o servidor
// que diz (papel da etapa, owner do ativo, segregação de função).
export function Validacoes({ validacoes, ativos, podeDecidir, onAlterado, onErro }: Props) {
  const [decidindo, setDecidindo] = useState<string | null>(null);
  const abertas = validacoes.filter((validacao) => validacao.status === 'aberta');
  const porId = new Map(ativos.map((ativo) => [ativo.id, ativo]));

  async function decidir(id: string, aprovada: boolean) {
    setDecidindo(id);
    onErro('');
    try {
      await decidirValidacao(id, aprovada, aprovada ? 'Aprovado pela revisão' : 'Precisa de ajustes antes da publicação');
      await onAlterado();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível registrar a decisão.');
    } finally {
      setDecidindo(null);
    }
  }

  return (
    <>
      <Cabecalho rotulo="GOVERNANÇA" titulo="Validações" texto="Cada etapa decidida por quem responde por ela. Quem submeteu não decide." />
      <section className="screen-grid governance-screen">
        <div className="panel screen-panel governance-summary">
          <span className="panel-label">FILA</span>
          <strong>{abertas.length}</strong>
          <p>etapas aguardando decisão</p>
          <div className="governance-bars"><span style={{ width: `${validacoes.length ? (abertas.length / validacoes.length) * 100 : 0}%` }} /></div>
          <small>{validacoes.length - abertas.length} encerradas</small>
        </div>
        <div className="panel screen-panel">
          <div className="panel-heading"><div><span className="panel-label">ETAPAS</span><h2>Abertas primeiro</h2></div><span className="result-count">{validacoes.length} no total</span></div>
          <div className="compact-list">
            {validacoes.length === 0 ? <div className="empty-state">Submeta um ativo para abrir uma validação.</div> : validacoes.map((validacao) => {
              const ativo = porId.get(validacao.ativo);
              return (
                <div className="validation-row" key={validacao.id}>
                  <span className={validacao.status === 'aberta' ? 'step-mark' : 'step-mark done'} aria-hidden="true">{validacao.status === 'aberta' ? '!' : <Check size={13} />}</span>
                  <span>
                    <strong>{ativo ? <Link href={`/catalogo?ativo=${ativo.id}`}>{ativo.codigo} · {ativo.nome}</Link> : validacao.ativo}</strong>
                    <small>etapa {validacao.etapa} · revisão {validacao.revisao} · {validacao.status}</small>
                  </span>
                  {validacao.status === 'aberta'
                    ? (podeDecidir
                      ? <span className="validation-buttons">
                          <button className="text-button" disabled={decidindo !== null} onClick={() => void decidir(validacao.id, false)}>Ajustes</button>
                          <button className="text-button" disabled={decidindo !== null} onClick={() => void decidir(validacao.id, true)}>Aprovar</button>
                        </span>
                      : <span className="status">aguardando</span>)
                    : <span className={validacao.status === 'aprovada' ? 'status status-good' : 'status'}><i aria-hidden="true" /> {validacao.status}</span>}
                </div>
              );
            })}
          </div>
        </div>
      </section>
    </>
  );
}
