import { useEffect, useState, type FormEvent } from 'react';
import { Link2, Paperclip, Plus, X } from 'lucide-react';
import {
  analisarImpacto, anexarEvidencia, arquivarAtivo, compararRevisoes, consultarHistorico, descontinuarAtivo,
  editarAtivo, relacionarAtivos,
  type AtivoResumo, type Comparacao, type FichaDoAtivo as Ficha, type Historico as DadosDoHistorico, type Impacto,
} from '../api';
import { Criticidade } from '../componentes';
import { Historico } from './Historico';
import { CartaoDeArquivamento, CartaoDeDescontinuacao, QuemDepende, podeSerArquivado, podeSerDescontinuado } from './Impacto';

type Aba = 'detalhes' | 'evidencias' | 'relacoes' | 'historico';
type Edicao = { nome: string; descricao: string; criticidade: string; atributos: Record<string, string> };

const tiposDeRelacao = ['depende_de', 'implementa', 'expoe', 'consome'];

type Props = {
  ficha: Ficha;
  ativos: AtivoResumo[];
  podeEscrever: boolean;
  podeDescontinuar: boolean;
  onFechar: () => void;
  onAlterado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

export function FichaDoAtivo({ ficha, ativos, podeEscrever, podeDescontinuar, onFechar, onAlterado, onErro }: Props) {
  const [aba, setAba] = useState<Aba>('detalhes');
  const [edicao, setEdicao] = useState<Edicao>({ nome: ficha.nome, descricao: ficha.descricao, criticidade: ficha.criticidade, atributos: { ...ficha.atributos } });
  const [salvandoEdicao, setSalvandoEdicao] = useState(false);
  const [novaEvidencia, setNovaEvidencia] = useState({ tipo: 'documento', titulo: '', url: '' });
  const [salvandoEvidencia, setSalvandoEvidencia] = useState(false);
  const [novaRelacao, setNovaRelacao] = useState({ destinoId: '', tipo: tiposDeRelacao[0], criticidade: 'media' });
  const [salvandoRelacao, setSalvandoRelacao] = useState(false);
  const [impacto, setImpacto] = useState<Impacto | null>(null);
  const [historico, setHistorico] = useState<DadosDoHistorico | null>(null);
  const [comparacao, setComparacao] = useState<Comparacao | null>(null);

  // O histórico só é buscado quando a aba abre, e de novo a cada mudança no ativo.
  useEffect(() => {
    if (aba !== 'historico') return;
    let vigente = true;
    setComparacao(null);
    consultarHistorico(ficha.id)
      .then((r) => { if (vigente) setHistorico(r); })
      .catch((e) => { if (vigente) onErro(e instanceof Error ? e.message : 'Não foi possível carregar o histórico.'); });
    return () => { vigente = false; };
  }, [aba, ficha.id, ficha.status, ficha.revisaoAtual, ficha.nome, ficha.evidencias.length, ficha.relacoes.length, onErro]);

  async function comparar() {
    onErro('');
    try {
      setComparacao(await compararRevisoes(ficha.id));
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível comparar as revisões.');
    }
  }

  // Recarrega a cada mudança de status: depois de descontinuar, o cartão some.
  useEffect(() => {
    let vigente = true;
    analisarImpacto(ficha.id)
      .then((r) => { if (vigente) setImpacto(r); })
      .catch(() => { if (vigente) setImpacto(null); });
    return () => { vigente = false; };
  }, [ficha.id, ficha.status, ficha.relacoes.length]);

  const outrosAtivos = ativos.filter((a) => a.id !== ficha.id);

  async function executar(acao: () => Promise<void>, falha: string, marcar: (v: boolean) => void) {
    marcar(true);
    onErro('');
    try {
      await acao();
      await onAlterado();
    } catch (e) {
      onErro(e instanceof Error ? e.message : falha);
    } finally {
      marcar(false);
    }
  }

  function salvarEdicao(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    void executar(() => editarAtivo(ficha.id, edicao), 'Não foi possível salvar as alterações.', setSalvandoEdicao);
  }

  function salvarEvidencia(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (!novaEvidencia.titulo.trim()) return;
    void executar(async () => {
      await anexarEvidencia(ficha.id, { tipo: novaEvidencia.tipo, titulo: novaEvidencia.titulo, url: novaEvidencia.url || undefined });
      setNovaEvidencia({ tipo: 'documento', titulo: '', url: '' });
    }, 'Não foi possível anexar a evidência.', setSalvandoEvidencia);
  }

  function salvarRelacao(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (!novaRelacao.destinoId) return;
    void executar(async () => {
      await relacionarAtivos(ficha.id, novaRelacao.destinoId, novaRelacao.tipo, novaRelacao.criticidade);
      setNovaRelacao({ destinoId: '', tipo: tiposDeRelacao[0], criticidade: 'media' });
    }, 'Não foi possível criar a relação.', setSalvandoRelacao);
  }

  async function descontinuar(motivo: string, confirmacao: string) {
    onErro('');
    try {
      await descontinuarAtivo(ficha.id, motivo, confirmacao || undefined);
      await onAlterado();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível descontinuar o ativo.');
    }
  }

  async function arquivar() {
    onErro('');
    try {
      await arquivarAtivo(ficha.id);
      await onAlterado();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível arquivar o ativo.');
    }
  }

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(evento) => evento.target === evento.currentTarget && onFechar()}>
      <div className="modal modal-wide" role="dialog" aria-modal="true" aria-labelledby="ficha-titulo">
        <div className="modal-header">
          <div><span className="panel-label">{ficha.codigo} · {ficha.tipoItem}</span><h3 id="ficha-titulo">{ficha.nome}</h3></div>
          <button type="button" className="icon-button" onClick={onFechar} aria-label="Fechar"><X size={19} /></button>
        </div>

        <div className="ficha-tabs" role="tablist">
          <button role="tab" aria-selected={aba === 'detalhes'} className={aba === 'detalhes' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAba('detalhes')}>Detalhes</button>
          <button role="tab" aria-selected={aba === 'evidencias'} className={aba === 'evidencias' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAba('evidencias')}>Evidências <span className="nav-count">{ficha.evidencias.length}</span></button>
          <button role="tab" aria-selected={aba === 'relacoes'} className={aba === 'relacoes' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAba('relacoes')}>Relações <span className="nav-count">{ficha.relacoes.length}</span></button>
          <button role="tab" aria-selected={aba === 'historico'} className={aba === 'historico' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAba('historico')}>Histórico <span className="nav-count">{ficha.revisaoAtual}</span></button>
        </div>

        <div className="ficha-body">
          {aba === 'detalhes' && (podeEscrever ? (
            <form onSubmit={salvarEdicao}>
              <label>Nome<input required value={edicao.nome} onChange={(e) => setEdicao({ ...edicao, nome: e.target.value })} /></label>
              <div className="form-grid">
                <label>Criticidade<select value={edicao.criticidade} onChange={(e) => setEdicao({ ...edicao, criticidade: e.target.value })}><option>baixa</option><option>media</option><option>alta</option><option>critica</option></select></label>
                <label>Status atual<input value={ficha.status} disabled /></label>
              </div>
              <label>Descrição<textarea rows={3} required value={edicao.descricao} onChange={(e) => setEdicao({ ...edicao, descricao: e.target.value })} /></label>
              {Object.keys(edicao.atributos).length > 0 && (
                <fieldset className="atributos">
                  <legend>Atributos</legend>
                  {Object.entries(edicao.atributos).map(([chave, valor]) => (
                    <label key={chave}>{chave}
                      <input value={valor} onChange={(e) => setEdicao({ ...edicao, atributos: { ...edicao.atributos, [chave]: e.target.value } })} />
                    </label>
                  ))}
                </fieldset>
              )}
              <Responsaveis ficha={ficha} />
              <div className="modal-actions"><button className="primary-button" disabled={salvandoEdicao}>{salvandoEdicao ? 'Salvando...' : 'Salvar alterações'}</button></div>
            </form>
          ) : (
            <>
              <p className="readonly-note">Seu papel só permite leitura. Nome: {ficha.nome} · Criticidade: {ficha.criticidade} · {ficha.descricao}</p>
              <Responsaveis ficha={ficha} />
            </>
          ))}

          {aba === 'evidencias' && (
            <>
              {ficha.evidencias.length === 0
                ? <div className="ficha-empty">Nenhuma evidência anexada.</div>
                : <div className="ficha-list">{ficha.evidencias.map((ev) => (
                    <div className="ficha-row" key={ev.id}>
                      <Paperclip size={15} aria-hidden="true" />
                      <div><strong>{ev.titulo}</strong>{ev.url && <small><a href={ev.url} target="_blank" rel="noreferrer">{ev.url}</a></small>}</div>
                      <span className="tag">{ev.tipo}</span>
                    </div>
                  ))}</div>}
              {podeEscrever && (
                <form onSubmit={salvarEvidencia} className="ficha-form">
                  <label>Título<input required value={novaEvidencia.titulo} onChange={(e) => setNovaEvidencia({ ...novaEvidencia, titulo: e.target.value })} placeholder="Ex.: ADR-0012" /></label>
                  <label>Tipo<select value={novaEvidencia.tipo} onChange={(e) => setNovaEvidencia({ ...novaEvidencia, tipo: e.target.value })}><option value="documento">Documento</option><option value="adr">ADR</option><option value="openapi">OpenAPI</option><option value="repositorio">Repositório</option><option value="link">Link</option></select></label>
                  <button className="primary-button" disabled={salvandoEvidencia} aria-label="Anexar evidência"><Plus size={15} /></button>
                  <label className="linha-inteira">URL (opcional)<input value={novaEvidencia.url} onChange={(e) => setNovaEvidencia({ ...novaEvidencia, url: e.target.value })} placeholder="https://..." /></label>
                </form>
              )}
            </>
          )}

          {aba === 'relacoes' && (
            <>
              {ficha.relacoes.length === 0
                ? <div className="ficha-empty">Nenhuma relação registrada.</div>
                : <div className="ficha-list">{ficha.relacoes.map((rel, i) => (
                    <div className="ficha-row" key={`${rel.ativoId}-${rel.tipo}-${i}`}>
                      <Link2 size={15} aria-hidden="true" />
                      <div><strong>{rel.direcao === 'saida' ? `${ficha.codigo} → ${rel.codigo}` : `${rel.codigo} → ${ficha.codigo}`}</strong><small>{rel.nome}</small></div>
                      <Criticidade valor={rel.criticidade} />
                      <span className="tag">{rel.tipo.replace('_', ' ')}</span>
                    </div>
                  ))}</div>}
              {podeEscrever && (
                <form onSubmit={salvarRelacao} className="ficha-form">
                  <label>Ativo de destino<select required value={novaRelacao.destinoId} onChange={(e) => setNovaRelacao({ ...novaRelacao, destinoId: e.target.value })}><option value="">Selecione</option>{outrosAtivos.map((a) => <option key={a.id} value={a.id}>{a.codigo} · {a.nome}</option>)}</select></label>
                  <label>Tipo de relação<select value={novaRelacao.tipo} onChange={(e) => setNovaRelacao({ ...novaRelacao, tipo: e.target.value })}>{tiposDeRelacao.map((t) => <option key={t} value={t}>{t.replace('_', ' ')}</option>)}</select></label>
                  <button className="primary-button" disabled={salvandoRelacao} aria-label="Criar relação"><Plus size={15} /></button>
                  <label className="linha-inteira">Criticidade da dependência<select value={novaRelacao.criticidade} onChange={(e) => setNovaRelacao({ ...novaRelacao, criticidade: e.target.value })}><option>baixa</option><option>media</option><option>alta</option><option>critica</option></select></label>
                </form>
              )}
              {impacto && <QuemDepende impacto={impacto} />}
              {impacto && podeDescontinuar && podeSerDescontinuado(ficha.status) && (
                <CartaoDeDescontinuacao key={ficha.status} impacto={impacto} onDescontinuar={descontinuar} />
              )}
              {podeDescontinuar && podeSerArquivado(ficha.status) && <CartaoDeArquivamento status={ficha.status} onArquivar={arquivar} />}
            </>
          )}

          {aba === 'historico' && (historico
            ? <Historico historico={historico} comparacao={comparacao} onComparar={() => void comparar()} />
            : <div className="ficha-empty">Carregando o histórico...</div>)}
        </div>
      </div>
    </div>
  );
}

function Responsaveis({ ficha }: { ficha: Ficha }) {
  return (
    <div className="detail-section">
      <span className="panel-label">RESPONSÁVEIS</span>
      {ficha.responsaveis.length === 0
        ? <p className="muted">Nenhum responsável atribuído ainda.</p>
        : <div className="ficha-list espaco-acima">{ficha.responsaveis.map((r, i) => (
            <div className="ficha-row" key={`${r.idPessoa}-${i}`}>
              <div><strong>{r.papel}</strong><small>desde {r.inicioVigencia}{r.fimVigencia ? ` até ${r.fimVigencia}` : ''}</small></div>
              <span className={r.vigente ? 'status status-good' : 'status'}><i aria-hidden="true" /> {r.vigente ? 'vigente' : 'encerrado'}</span>
            </div>
          ))}</div>}
    </div>
  );
}
