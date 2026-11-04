// A gestão de acessos e os squads do painel-ddd (/gestao-acessos, /squads).
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { Cadastro, Squad } from '../api';
import { PainelDoCadastro, TabelaDeCadastros } from './GestaoDeAcessos';
import { ListaDeSquads } from './Squads';

const nada = () => {};

function cadastro(id: string, nome = `Pessoa ${id}`, ativa = true): Cadastro {
  return {
    pessoa: { id, matricula: 'C123456', nome, email: 'x@empresa.com', unidade: '0427', ativa, papeis: ['negocio'], idSquad: null },
    squad: null,
    papeis: [{ papel: 'negocio', rotulo: 'Negócio', escopoTipo: 'empresa', escopoId: null, escopoRotulo: 'todo o catálogo', blocos: ['Estrutura DDD'], desde: '2026-10-01' }],
  };
}

const tabela = (cadastros: Cadastro[], idDoAdmin: string | null = null) => renderToStaticMarkup(
  <TabelaDeCadastros cadastros={cadastros} pagina={1} idDoAdmin={idDoAdmin} selecionados={[]} aberto={null}
    onSelecionar={nada} onAbrir={nada} onPagina={nada} />,
);

const painel = (vinculos: string[] | null, ehVoce = false) => renderToStaticMarkup(
  <PainelDoCadastro cadastro={cadastro('p-1')} squads={[]} dominios={[]} vinculos={vinculos} ehVoce={ehVoce} ocupado={false}
    onCorrigir={nada} onConceder={nada} onRevogar={nada} onExcluir={nada} />,
);

describe('gestão de acessos', () => {
  it('pagina de 20 em 20', () => {
    const muitos = Array.from({ length: 25 }, (_, i) => cadastro(`p-${i}`));
    const html = tabela(muitos);
    expect(html).toContain('Página 1 de 2');
    expect(html.match(/>Detalhes</g)).toHaveLength(20);
  });

  it('o administrador conectado não entra na seleção para inativar', () => {
    const html = tabela([cadastro('eu', 'Ana Admin'), cadastro('outra', 'Bia'), cadastro('inativa', 'Caio', false)], 'eu');
    expect(html).toContain('(você)');
    expect(html).not.toContain('aria-label="Selecionar Ana Admin"');
    expect(html).toContain('aria-label="Selecionar Bia"');
    expect(html).not.toContain('aria-label="Selecionar Caio"');
  });

  it('cadastro com vínculo não oferece exclusão, e diz por quê', () => {
    const html = painel(['papéis atribuídos', 'pleitos de acesso']);
    expect(html).toContain('Não pode ser excluído: papéis atribuídos, pleitos de acesso');
    expect(html).not.toContain('Excluir cadastro');
    expect(painel([])).toContain('Excluir cadastro');
  });

  it('o próprio cadastro: sem revogar, sem conceder, sem excluir e sem se inativar', () => {
    const html = painel([], true);
    expect(html).not.toContain('>Revogar<');
    expect(html).not.toContain('>Conceder<');
    expect(html).not.toContain('Excluir cadastro');
    const caixa = html.match(/<input[^>]*name="ativa"[^>]*>/)?.[0] ?? '';
    expect(caixa).toContain('disabled');
    expect(painel([])).toContain('>Revogar<');
  });
});

describe('squads', () => {
  const squads: Squad[] = [
    { id: 's-1', sigla: 'CRED', titulo: 'Squad Crédito', descricao: 'Contratos', ativa: true },
    { id: 's-2', sigla: 'GAR', titulo: 'Squad Garantias', descricao: 'Garantias', ativa: false },
  ];

  it('inativo não se inativa de novo, mas ainda se edita', () => {
    const html = renderToStaticMarkup(<ListaDeSquads squads={squads} ocupado={false} onEditar={nada} onInativar={nada} />);
    expect(html.match(/>Inativar</g)).toHaveLength(1);
    expect(html.match(/>Editar</g)).toHaveLength(2);
  });
});
