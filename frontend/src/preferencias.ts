// Preferências de quem está vendo a tela: tema e menu recolhido. Ficam no
// navegador (localStorage), e o script do index.html as aplica ANTES do CSS,
// para não piscar a tela clara. As chaves e valores aqui e lá são os mesmos.
//
// Tema com três estados, como no painel-ddd: o do sistema (sem valor salvo: o
// navegador decide por prefers-color-scheme), claro e escuro (forçados).

export type Tema = 'sistema' | 'light' | 'dark';

export const CHAVE_TEMA = 'jornada-ddd:tema';
export const CHAVE_MENU = 'jornada-ddd:menu';

export const ESTADOS_DE_TEMA: { tema: Tema; glifo: string; rotulo: string }[] = [
  { tema: 'sistema', glifo: '◐', rotulo: 'Tema do sistema' },
  { tema: 'light', glifo: '☀', rotulo: 'Tema claro' },
  { tema: 'dark', glifo: '☾', rotulo: 'Tema escuro' },
];

export function proximoTema(atual: Tema): Tema {
  const indice = ESTADOS_DE_TEMA.findIndex((estado) => estado.tema === atual);
  return ESTADOS_DE_TEMA[(indice + 1) % ESTADOS_DE_TEMA.length].tema;
}

export function estadoDoTema(tema: Tema) {
  return ESTADOS_DE_TEMA.find((estado) => estado.tema === tema) ?? ESTADOS_DE_TEMA[0];
}

type Armazenamento = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

// O storage pode estar bloqueado (aba anônima, política do navegador): a
// preferência vale para esta visita mesmo assim, só não é lembrada.
function ler(armazenamento: Armazenamento | undefined, chave: string): string | null {
  try {
    return armazenamento?.getItem(chave) ?? null;
  } catch {
    return null;
  }
}

function gravar(armazenamento: Armazenamento | undefined, chave: string, valor: string | null): void {
  try {
    if (valor === null) armazenamento?.removeItem(chave);
    else armazenamento?.setItem(chave, valor);
  } catch {
    // segue sem persistir
  }
}

export function lerTema(armazenamento?: Armazenamento): Tema {
  const valor = ler(armazenamento, CHAVE_TEMA);
  return valor === 'light' || valor === 'dark' ? valor : 'sistema';
}

export function gravarTema(armazenamento: Armazenamento | undefined, tema: Tema): void {
  gravar(armazenamento, CHAVE_TEMA, tema === 'sistema' ? null : tema);
}

export function lerMenuCompacto(armazenamento?: Armazenamento): boolean {
  return ler(armazenamento, CHAVE_MENU) === 'compacto';
}

export function gravarMenuCompacto(armazenamento: Armazenamento | undefined, compacto: boolean): void {
  gravar(armazenamento, CHAVE_MENU, compacto ? 'compacto' : null);
}

/** O localStorage, se o navegador deixar tocar nele. */
export function armazenamentoDoNavegador(): Armazenamento | undefined {
  try {
    return typeof window === 'undefined' ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}
