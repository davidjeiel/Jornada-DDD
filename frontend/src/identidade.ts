// Quem você é para a API, até a identidade corporativa entrar (ADR-0007): a
// empresa, a pessoa e o modo de trabalho. É o session["id_pessoa"] e o
// session["modo"] do painel-ddd. Os PAPÉIS não ficam aqui: são os concedidos
// à pessoa, e quem diz quais são é a API (GET /v1/perfil, ADR-0018).
import { empresas, usuariosDeTeste } from './usuarios';

export type Identidade = {
  empresaId: string;
  /** Nulo: ainda não disse quem é. Consulta tudo e não escreve nada. */
  pessoaId: string | null;
  modoLeitura: boolean;
};

const CHAVE = 'jornada-ddd:identidade';
// Antes da fase 2 o front guardava só o usuário de teste escolhido.
const CHAVE_ANTIGA = 'jornada-ddd:usuario-de-teste';

export const identidadeInicial: Identidade = { empresaId: empresas[0].id, pessoaId: null, modoLeitura: false };

type Armazenamento = Pick<Storage, 'getItem' | 'setItem'>;

function armazenamento(): Armazenamento | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}

export function lerIdentidade(fonte: Armazenamento | null = armazenamento()): Identidade {
  try {
    const salva = fonte?.getItem(CHAVE);
    if (salva) {
      const dados = JSON.parse(salva) as Partial<Identidade>;
      if (typeof dados.empresaId === 'string') {
        return { empresaId: dados.empresaId, pessoaId: dados.pessoaId ?? null, modoLeitura: dados.modoLeitura === true };
      }
    }
    const antigo = usuariosDeTeste.find((u) => u.id === fonte?.getItem(CHAVE_ANTIGA));
    if (antigo) return { empresaId: antigo.empresaId, pessoaId: antigo.id, modoLeitura: false };
  } catch {
    // armazenamento indisponível ou corrompido: começa sem pessoa.
  }
  return identidadeInicial;
}

export function gravarIdentidade(identidade: Identidade, fonte: Armazenamento | null = armazenamento()): void {
  try {
    fonte?.setItem(CHAVE, JSON.stringify(identidade));
  } catch {
    // conveniência do navegador: falhar aqui não pode impedir a troca.
  }
}

/** Os cabeçalhos que o middleware de tenant lê. Sem X-Papel: o papel é o concedido. */
export function cabecalhosDe(identidade: Identidade): Record<string, string> {
  return {
    'X-Empresa': identidade.empresaId,
    ...(identidade.pessoaId ? { 'X-Pessoa': identidade.pessoaId } : {}),
    ...(identidade.modoLeitura ? { 'X-Modo': 'leitura' } : {}),
  };
}
