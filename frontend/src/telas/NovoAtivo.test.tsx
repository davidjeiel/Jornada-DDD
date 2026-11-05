// Equivalentes de tests/test_fluxo.py e test_jornada.py do painel-ddd para o
// assistente de cadastro. O nome do teste original vem no título.
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import type { AtivoResumo, Rito, Squad } from '../api';
import { metamodeloDeTeste } from '../testes/perfis';
import { AssistenteDeCadastro, etapasDoRito, tiposOferecidos, type Formulario } from './NovoAtivo';

const nada = () => {};
const ritoApiCritica: Rito = { tipoItem: 'api', rotulo: 'API', criticidade: 'critica', etapas: ['negocial', 'tecnica', 'arquitetural'], evidenciaMinima: 2, scoreMinimo: 80, slaHoras: 24, revisarACadaDias: 90 };
const squads: Squad[] = [{ id: 'sq-1', sigla: 'CRED', titulo: 'Squad Crédito', descricao: 'x', ativa: true }, { id: 'sq-2', sigla: 'OLD', titulo: 'Antiga', descricao: 'x', ativa: false }];
const dominio: AtivoResumo = { id: 'd-1', codigo: 'DOM-0001', nome: 'Contrato', tipo: 'dominio', status: 'publicado', criticidade: 'alta' };

const vazio: Formulario = { tipo: 'api', nome: '', descricao: '', criticidade: 'critica', paiId: '', idSquad: '', atributos: {} };

function assistente(formulario: Formulario, rito: Rito | null = ritoApiCritica, blocos = ['Estrutura DDD', 'Ativos técnicos']) {
  return renderToStaticMarkup(
    <AssistenteDeCadastro oferecidos={tiposOferecidos(metamodeloDeTeste, blocos)} metamodelo={metamodeloDeTeste} ativos={[dominio]} squads={squads}
      formulario={formulario} rito={rito} salvando={false} onMudar={nada} onSalvar={nada} />,
  );
}

describe('assistente de cadastro', () => {
  it('antecipa o rito do tipo e da criticidade (test_wizard_antecipa_o_rito_do_tipo)', () => {
    const html = assistente(vazio);
    expect(html).toContain('Rito de publicação');
    expect(html).toContain('negocial → tecnica → arquitetural');
    expect(html).toContain('80%');
    expect(etapasDoRito(ritoApiCritica)).toBe('negocial → tecnica → arquitetural');
  });

  it('devolve o que foi digitado depois do erro (test_wizard_devolve_o_que_foi_digitado_apos_erro)', () => {
    // O formulário é controlado e não se limpa na recusa: renderizar o estado é o que a tela mostra depois do erro.
    const html = assistente({
      tipo: 'dominio', nome: 'contrato', descricao: 'Outro domínio de contratos', criticidade: 'alta', paiId: '', idSquad: 'sq-1',
      atributos: { visao: 'Unificar a originação', forum: 'Comitê de Crédito' },
    });
    expect(html).toContain('Unificar a originação');
    expect(html).toContain('Comitê de Crédito');
    expect(html).toContain('Outro domínio de contratos');
    expect(html).toMatch(/<option value="alta" selected="">/);
    expect(html).toMatch(/<option value="sq-1" selected="">/);
  });

  it('preserva o pai escolhido e a opção do campo (test_wizard_preserva_o_pai_escolhido_apos_erro)', () => {
    const html = assistente({ ...vazio, tipo: 'subdominio', criticidade: 'media', paiId: 'd-1', atributos: { classificacao: 'suporte' } });
    expect(html).toMatch(/<option value="d-1" selected="">/);
    expect(html).toMatch(/<option value="suporte" selected="">/);
  });

  it('oferece os tipos do metamodelo só nos blocos do papel, e só squads ativos', () => {
    const tecnico = assistente(vazio, ritoApiCritica, ['Ativos técnicos']);
    expect(tecnico).toContain('value="api"');
    expect(tecnico).not.toContain('value="dominio"');
    expect(tecnico).toContain('CRED · Squad Crédito');
    expect(tecnico).not.toContain('Antiga');
  });

  it('sem rito ainda, avisa que está consultando', () => {
    expect(assistente(vazio, null)).toContain('Consultando o rito');
  });
});
