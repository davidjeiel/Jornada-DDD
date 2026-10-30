// A aba Histórico da ficha, como a da visão 360° do painel-ddd: as revisões
// submetidas (com o hash do retrato), a comparação entre as duas últimas e a
// trilha de auditoria (ADR-0017).
import type { Comparacao, Historico as DadosDoHistorico, RegistroDeAuditoria } from '../api';
import { nomeDaPessoa } from '../usuarios';

const ACOES: Record<string, string> = {
  criar: 'Cadastrou',
  alterar: 'Alterou',
  definir_responsavel: 'Definiu responsável',
  relacionar: 'Relacionou',
  anexar_evidencia: 'Anexou evidência',
  submeter: 'Submeteu',
  validar: 'Decidiu etapa',
  publicar: 'Publicou',
  abrir_revisao: 'Abriu revisão',
  descontinuar: 'Descontinuou',
  arquivar: 'Arquivou',
};

export function rotuloDaAcao(acao: string): string {
  return ACOES[acao] ?? acao.replaceAll('_', ' ');
}

function quando(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** O que a linha de auditoria diz, numa linha: "nome: A → B" na alteração, "chave: valor" no resto. */
export function detalheDaAuditoria(registro: RegistroDeAuditoria): string {
  const depois = registro.depois ?? {};
  const antes = registro.antes ?? {};
  return Object.entries(depois)
    .filter(([, valor]) => valor !== '')
    .map(([chave, valor]) => {
      const texto = chave === 'hash' ? valor.slice(0, 12) : valor;
      return chave in antes ? `${chave}: ${antes[chave] || '—'} → ${texto}` : `${chave}: ${texto}`;
    })
    .join('; ');
}

type Props = {
  historico: DadosDoHistorico;
  comparacao: Comparacao | null;
  onComparar: () => void;
};

export function Historico({ historico, comparacao, onComparar }: Props) {
  const { revisoes, auditoria } = historico;
  return (
    <>
      <section className="detail-section" aria-labelledby="revisoes-titulo">
        <span className="panel-label" id="revisoes-titulo">Revisões</span>
        <div className="rolagem">
          <table className="tabela">
            <thead><tr><th scope="col">Revisão</th><th scope="col">Motivo</th><th scope="col">Autor</th><th scope="col">Publicada</th><th scope="col">Hash</th></tr></thead>
            <tbody>
              {revisoes.length === 0
                ? <tr><td colSpan={5} className="vazio">Nenhuma revisão submetida.</td></tr>
                : revisoes.map((r) => (
                    <tr key={r.numero}>
                      <td>#{r.numero}</td>
                      <td>{r.motivo || '—'}</td>
                      <td>{nomeDaPessoa(r.autor)}</td>
                      <td>{r.publicadaEm ? quando(r.publicadaEm) : 'não publicada'}</td>
                      <td>
                        <span className="mono" title={r.hash}>{r.hash.slice(0, 12)}</span>
                        {!r.integra && <span className="alerta-integridade"> não confere</span>}
                      </td>
                    </tr>
                  ))}
            </tbody>
          </table>
        </div>
        {revisoes.length > 1 && !comparacao && (
          <button type="button" className="text-button" onClick={onComparar}>Comparar as duas últimas revisões</button>
        )}
        {comparacao && <TabelaDeComparacao comparacao={comparacao} />}
      </section>

      <section className="detail-section" aria-labelledby="auditoria-titulo">
        <span className="panel-label" id="auditoria-titulo">Auditoria</span>
        <div className="rolagem">
          <table className="tabela">
            <thead><tr><th scope="col">Quando</th><th scope="col">Ação</th><th scope="col">Quem</th><th scope="col">Detalhe</th></tr></thead>
            <tbody>
              {auditoria.length === 0
                ? <tr><td colSpan={4} className="vazio">Nenhum registro.</td></tr>
                : auditoria.slice(0, 15).map((a, i) => (
                    <tr key={`${a.em}-${i}`}>
                      <td className="sem-quebra">{quando(a.em)}</td>
                      <td>{rotuloDaAcao(a.acao)}</td>
                      <td>{nomeDaPessoa(a.pessoa)}</td>
                      <td className="discreto">{detalheDaAuditoria(a)}</td>
                    </tr>
                  ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

function TabelaDeComparacao({ comparacao }: { comparacao: Comparacao }) {
  return (
    <div className="comparacao">
      <h4>Revisão #{comparacao.a} vs #{comparacao.b}</h4>
      {comparacao.diferencas.length === 0
        ? <p className="vazio">Nenhuma diferença entre as revisões escolhidas.</p>
        : (
          <div className="rolagem">
            <table className="tabela diff">
              <thead><tr><th scope="col">Campo</th><th scope="col">Revisão #{comparacao.a}</th><th scope="col">Revisão #{comparacao.b}</th></tr></thead>
              <tbody>
                {comparacao.diferencas.map((d) => (
                  <tr key={d.campo}><td>{d.rotulo}</td><td className="antes">{d.antes}</td><td className="depois">{d.depois}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
    </div>
  );
}
