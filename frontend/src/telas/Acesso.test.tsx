// Equivalentes de tests/test_acesso_pleito.py do painel-ddd para as telas do
// rito de acesso. O nome do teste original vem no fim de cada título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { identidadeInicial } from '../identidade';
import { metamodeloDeTeste, perfilDe, pleito } from '../testes/perfis';
import { NovoAtivo } from './NovoAtivo';
import { Perfil } from './Perfil';
import { FilaDePleitos } from './Pleitos';
import { OQueCadaPapelExerce, SolicitarAcesso } from './SolicitarAcesso';

const nada = () => {};
const assincrono = async () => {};

function novoAtivo(blocos: string[], opcoes: { identificado?: boolean; modoLeitura?: boolean } = {}) {
  return renderToStaticMarkup(
    <NovoAtivo ativos={[]} metamodelo={metamodeloDeTeste} squads={[]} blocos={blocos} identificado={opcoes.identificado ?? true} modoLeitura={opcoes.modoLeitura ?? false}
      onCadastrado={assincrono} onErro={nada} />,
  );
}

function perfil(dados: ReturnType<typeof perfilDe>) {
  return renderToStaticMarkup(
    <Perfil perfil={dados} identidade={{ ...identidadeInicial, pessoaId: 'p-1' }} pessoas={[]}
      onIdentificar={nada} onTrocarEmpresa={nada} onModo={nada} />,
  );
}

describe('cadastro e pleito', () => {
  it('a tela de cadastro abre sem identificação (test_tela_de_cadastro_abre_sem_identificacao)', () => {
    const html = renderToStaticMarkup(<SolicitarAcesso onCadastrado={nada} onErro={nada} />);
    expect(html).toContain('name="matricula"');
    expect(html).toContain('name="unidade"');
    expect(html).toContain('name="papelPleiteado"');
    expect(html).toContain('name="justificativa"');
  });

  it('o perfil mostra o pleito aguardando decisão (test_cadastro_pela_tela_identifica_a_pessoa_na_hora)', () => {
    const html = perfil(perfilDe(null, [pleito()]));
    expect(html).toContain('Davi Souza');
    expect(html).toContain('aguardando decisão');
    expect(html).toContain('Nenhum papel concedido');
  });

  it('a decisão aparece no perfil (test_decisao_pela_tela_concede_e_aparece_no_perfil)', () => {
    const html = perfil(perfilDe('techlead', [pleito({ status: 'aprovado', papelConcedido: 'techlead', escopoRotulo: 'todo o catálogo', resposta: 'Ok.' })]));
    expect(html).toContain('aprovado');
    expect(html).toContain('Time técnico');
    expect(html).toContain('Ok.');
  });
});

describe('fila de pleitos', () => {
  const fila = (podeConcederAdmin: boolean) => renderToStaticMarkup(
    <FilaDePleitos fila={{ total: 1, pendentes: 1, podeConcederAdmin, pleitos: [pleito({ papelPleiteado: 'admin' })] }}
      dominios={[]} decidindo={null} onDecidir={nada} />,
  );

  it('mostra o pleito a quem concede (test_fila_mostra_o_pleito_a_quem_concede)', () => {
    const html = fila(false);
    expect(html).toContain('Davi Souza');
    expect(html).toContain('C123456');
    expect(html).toContain('Vou cadastrar APIs.');
  });

  it('não oferece admin a curador (test_fila_nao_oferece_admin_a_curador)', () => {
    expect(fila(false)).not.toContain('value="admin"');
    expect(fila(true)).toContain('value="admin"');
  });
});

describe('novo ativo', () => {
  it('só oferece os blocos do papel (test_wizard_so_oferece_os_blocos_do_papel)', () => {
    const negocio = novoAtivo(['Estrutura DDD']);
    expect(negocio).toContain('Estrutura DDD');
    expect(negocio).not.toContain('Ativos técnicos');
    const tech = novoAtivo(['Ativos técnicos']);
    expect(tech).toContain('value="sistema"');
    expect(tech).not.toContain('value="dominio"');
  });

  it('sem papel, manda pleitear (test_sem_papel_o_wizard_manda_pleitear)', () => {
    expect(novoAtivo([])).toContain('href="/acesso/solicitar"');
    expect(novoAtivo([])).not.toMatch(/<form\b/);
  });

  it('em modo leitura, não há formulário e o caminho é o perfil (test_modo_leitura_esconde_os_formularios)', () => {
    const html = novoAtivo([], { modoLeitura: true });
    expect(html).toContain('modo de leitura');
    expect(html).toContain('href="/perfil"');
    expect(html).not.toMatch(/<form\b/);
  });
});

describe('escopo de squad', () => {
  it('a fila oferece conceder nos ativos de um squad, só com squads ativos', () => {
    const html = renderToStaticMarkup(
      <FilaDePleitos fila={{ total: 1, pendentes: 1, podeConcederAdmin: false, pleitos: [pleito()] }} dominios={[]}
        squads={[{ id: 'sq-1', sigla: 'CRED', titulo: 'Squad Crédito', descricao: 'x', ativa: true }, { id: 'sq-2', sigla: 'OLD', titulo: 'Antiga', descricao: 'x', ativa: false }]}
        decidindo={null} onDecidir={nada} />,
    );
    expect(html).toContain('value="squad"');
    expect(html).toContain('CRED · Squad Crédito');
    expect(html).not.toContain('Antiga');
  });
});

describe('ao lado do pedido de acesso', () => {
  it('cada papel diz onde escreve, e consulta não escreve nada', () => {
    const html = renderToStaticMarkup(<OQueCadaPapelExerce blocosPorPapel={{ negocio: ['Estrutura DDD'], techlead: ['Ativos técnicos'], admin: ['Estrutura DDD', 'Ativos técnicos'], consulta: [] }} />);
    expect(html).toContain('Ativos técnicos e Estrutura DDD');
    expect(html).toContain('nada: só consulta');
    expect(html).toContain('href="/cartilha"');
  });

  it('a tela explica o que acontece depois do pedido', () => {
    const html = renderToStaticMarkup(<SolicitarAcesso onCadastrado={() => {}} onErro={() => {}} />);
    expect(html).toContain('O que cada papel exerce');
    expect(html).toContain('O que acontece depois');
    expect(html).toContain('não cria um segundo pedido');
  });
});
