// Os papéis pela chave da API (acesso.PAPEIS do painel-ddd). Quem tem cada um
// e o que cada um pode é o servidor que diz; aqui fica só o nome para a tela.
export const PAPEIS = ['admin', 'curador', 'arquiteto', 'techlead', 'negocio', 'consulta'] as const;

export type ChaveDePapel = (typeof PAPEIS)[number];

export const ROTULO_DO_PAPEL: Record<ChaveDePapel, string> = {
  admin: 'Administrador do catálogo',
  curador: 'Curador (cadastra e submete)',
  arquiteto: 'Arquiteto',
  techlead: 'Time técnico',
  negocio: 'Negócio',
  consulta: 'Somente consulta',
};

export function rotuloDoPapel(chave: string | null | undefined): string {
  return (chave && ROTULO_DO_PAPEL[chave as ChaveDePapel]) || chave || '—';
}

/** "aguardando decisão", como no perfil do painel. */
export function situacaoDoPleito(status: string): string {
  return status === 'pendente' ? 'aguardando decisão' : status === 'aprovado' ? 'aprovado' : 'negado';
}
