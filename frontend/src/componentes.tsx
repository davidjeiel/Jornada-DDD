import type { ReactNode } from 'react';
import { CircleAlert, X } from 'lucide-react';

export function Cabecalho({ rotulo, titulo, texto, acoes }: { rotulo: string; titulo: string; texto?: string; acoes?: ReactNode }) {
  return (
    <div className="intro">
      <div>
        <p className="kicker">{rotulo}</p>
        <h1>{titulo}</h1>
        {texto && <p className="muted">{texto}</p>}
      </div>
      {acoes && <div className="intro-acoes">{acoes}</div>}
    </div>
  );
}

export function Erro({ mensagem, onFechar }: { mensagem: string; onFechar: () => void }) {
  if (!mensagem) return null;
  return (
    <div className="error-banner" role="alert">
      <CircleAlert size={18} aria-hidden="true" /> {mensagem}
      <button type="button" onClick={onFechar} aria-label="Fechar erro"><X size={16} /></button>
    </div>
  );
}

export function Metric({ label, value, note, accent }: { label: string; value: string | number; note: string; accent: string }) {
  return <div className={`metric metric-${accent}`}><span>{label}</span><strong>{value}</strong><small>{note}</small></div>;
}

/** Etiqueta por estado do ciclo de vida, como .etiqueta.<status> do painel. */
export function Status({ status }: { status: string }) {
  return <span className={`status status-${status.toLowerCase()}`}>{status.replace('_', ' ')}</span>;
}

/** Cada criticidade com estilo próprio: baixa e média não podem parecer "sem criticidade". */
export function Criticidade({ valor }: { valor: string }) {
  return <span className={`critical critical-${valor.toLowerCase()}`}>{valor}</span>;
}
