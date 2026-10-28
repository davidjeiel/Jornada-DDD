// Saída do catálogo: quem depende do ativo e o cartão de descontinuação, na
// aba Relações da ficha — como a visão 360° do painel-ddd. A regra (consumidor
// crítico exige o código) é da API; aqui ela só fica visível antes do clique.
import { useState, type FormEvent } from 'react';
import { Archive, CircleOff } from 'lucide-react';
import type { Impacto } from '../api';
import type { Papel } from '../usuarios';
import { Criticidade } from '../componentes';

// Espelho de PERMISSOES[descontinuar] (PoliticaDeAutorizacao). A API decide;
// isto só evita oferecer um botão que vai ser recusado.
const PAPEIS_QUE_DESCONTINUAM: Papel[] = ['admin', 'curador', 'arquiteto'];

export function podeDescontinuar(papel: Papel): boolean {
  return PAPEIS_QUE_DESCONTINUAM.includes(papel);
}

export function podeSerDescontinuado(status: string): boolean {
  return status === 'publicado' || status === 'em_revisao';
}

export function podeSerArquivado(status: string): boolean {
  return status === 'rascunho' || status === 'descontinuado';
}

function contagem(impacto: Impacto): string {
  if (impacto.total === 0) return 'Nenhum ativo em vigor depende deste.';
  const consumidores = impacto.total === 1 ? '1 consumidor em vigor' : `${impacto.total} consumidores em vigor`;
  return impacto.criticos > 0 ? `${consumidores}, ${impacto.criticos} de alta criticidade.` : `${consumidores}.`;
}

function ListaDeConsumidores({ impacto }: { impacto: Impacto }) {
  return (
    <div className="ficha-list espaco-acima">
      {impacto.consumidores.map((c) => (
        <div className="ficha-row" key={`${c.id}-${c.tipoRelacao}`}>
          <div><strong>{c.codigo} · {c.nome}</strong><small>{c.tipoItem} · {c.tipoRelacao.replace('_', ' ')}</small></div>
          <Criticidade valor={c.criticidade} />
        </div>
      ))}
    </div>
  );
}

/** Equivale ao bloco "Quem depende deste ativo" da aba Relações do painel. */
export function QuemDepende({ impacto }: { impacto: Impacto }) {
  return (
    <section className="detail-section quem-depende" aria-labelledby="quem-depende-titulo">
      <span className="panel-label" id="quem-depende-titulo">Quem depende deste ativo</span>
      <p className={impacto.criticos > 0 ? 'impacto-resumo impacto-critico' : 'impacto-resumo'}>{contagem(impacto)}</p>
      {impacto.total > 0 && <ListaDeConsumidores impacto={impacto} />}
    </section>
  );
}

type PropsDescontinuacao = {
  impacto: Impacto;
  onDescontinuar: (motivo: string, confirmacao: string) => Promise<void>;
};

/**
 * Confirmação em duas etapas quando há consumidor crítico: quem descontinua
 * vê quem seria afetado e digita o código do ativo (rota descontinuar do painel).
 */
export function CartaoDeDescontinuacao({ impacto, onDescontinuar }: PropsDescontinuacao) {
  const [motivo, setMotivo] = useState('');
  const [confirmacao, setConfirmacao] = useState('');
  const [enviando, setEnviando] = useState(false);

  const confirmado = !impacto.exigeConfirmacao || confirmacao.trim().toUpperCase() === impacto.codigo.toUpperCase();

  async function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (!motivo.trim() || !confirmado) return;
    setEnviando(true);
    try {
      await onDescontinuar(motivo.trim(), confirmacao.trim());
    } finally {
      setEnviando(false);
    }
  }

  return (
    <form className="detail-section cartao-saida" onSubmit={enviar} aria-labelledby="descontinuar-titulo">
      <span className="panel-label" id="descontinuar-titulo">Descontinuar este ativo</span>
      <p className="muted">O ativo sai de vigência e não volta a publicado. {contagem(impacto)}</p>
      {impacto.total > 0 && (
        <details>
          <summary>Ver quem seria afetado ({impacto.total})</summary>
          <ListaDeConsumidores impacto={impacto} />
        </details>
      )}
      <label>Motivo<textarea name="motivo" rows={2} required value={motivo} onChange={(e) => setMotivo(e.target.value)} /></label>
      {impacto.exigeConfirmacao && (
        <label>Digite o código <strong>{impacto.codigo}</strong> para confirmar
          <input name="confirmacao" autoComplete="off" value={confirmacao} onChange={(e) => setConfirmacao(e.target.value)} />
        </label>
      )}
      <div className="modal-actions">
        <button className="danger-button" disabled={enviando || !motivo.trim() || !confirmado}>
          <CircleOff size={15} aria-hidden="true" /> {enviando ? 'Descontinuando...' : 'Descontinuar'}
        </button>
      </div>
    </form>
  );
}

export function CartaoDeArquivamento({ status, onArquivar }: { status: string; onArquivar: () => Promise<void> }) {
  const [enviando, setEnviando] = useState(false);
  const texto = status === 'rascunho'
    ? 'Rascunho que não vai adiante. Arquivado, sai das listas de trabalho e não volta.'
    : 'Já descontinuado. Arquivado, sai das listas de trabalho e não volta.';

  async function arquivar() {
    setEnviando(true);
    try {
      await onArquivar();
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="detail-section cartao-saida">
      <span className="panel-label">Arquivar este ativo</span>
      <p className="muted">{texto}</p>
      <div className="modal-actions">
        <button type="button" className="danger-button" disabled={enviando} onClick={() => void arquivar()}>
          <Archive size={15} aria-hidden="true" /> {enviando ? 'Arquivando...' : 'Arquivar'}
        </button>
      </div>
    </div>
  );
}
