import { useState, type FormEvent } from 'react';
import { cadastrarAtivo, type AtivoResumo } from '../api';
import { Cabecalho } from '../componentes';
import { Link, navegar } from '../roteador';

type Formulario = { tipo: string; nome: string; descricao: string; criticidade: string; paiId: string; atributos: Record<string, string> };

// Hoje o formulário cobre a estrutura DDD. O assistente que lê o metamodelo da
// empresa, oferece só os blocos do seu papel e antecipa o rito do tipo chega
// na fase 3 (docs/PARIDADE.md).
const tiposEstrutura = [
  { chave: 'dominio', rotulo: 'Domínio', pai: null },
  { chave: 'subdominio', rotulo: 'Subdomínio', pai: 'dominio' },
  { chave: 'contexto', rotulo: 'Contexto delimitado', pai: 'subdominio' },
  { chave: 'capacidade', rotulo: 'Capacidade de negócio', pai: 'contexto' },
];
const camposEstrutura: Record<string, { nome: string; rotulo: string; obrigatorio?: boolean; opcoes?: string[] }[]> = {
  dominio: [{ nome: 'visao', rotulo: 'Visão de negócio', obrigatorio: true }],
  subdominio: [{ nome: 'classificacao', rotulo: 'Classificação', obrigatorio: true, opcoes: ['core', 'suporte', 'genérico'] }],
  contexto: [{ nome: 'linguagem_ubiqua', rotulo: 'Termos da linguagem ubíqua' }, { nome: 'estrategia_integracao', rotulo: 'Estratégia de integração', opcoes: ['parceria', 'cliente-fornecedor', 'conformista', 'camada anticorrupção', 'serviço aberto'] }],
  capacidade: [{ nome: 'resultado_esperado', rotulo: 'Resultado esperado', obrigatorio: true }, { nome: 'processo_negocio', rotulo: 'Processo de negócio' }],
};
const formularioInicial: Formulario = { tipo: 'dominio', nome: '', descricao: '', criticidade: 'media', paiId: '', atributos: {} };

type Props = { ativos: AtivoResumo[]; podeEscrever: boolean; onCadastrado: () => Promise<void>; onErro: (mensagem: string) => void };

export function NovoAtivo({ ativos, podeEscrever, onCadastrado, onErro }: Props) {
  const [formulario, setFormulario] = useState<Formulario>(formularioInicial);
  const [salvando, setSalvando] = useState(false);

  const tipo = tiposEstrutura.find((t) => t.chave === formulario.tipo);
  const rotuloDoPai = tiposEstrutura.find((t) => t.chave === tipo?.pai)?.rotulo;
  const pais = ativos.filter((ativo) => ativo.tipo.toLowerCase() === tipo?.pai);
  const campos = camposEstrutura[formulario.tipo] ?? [];
  const faltaPai = !!tipo?.pai && (!formulario.paiId || pais.length === 0);

  async function salvar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setSalvando(true);
    onErro('');
    try {
      const { id } = await cadastrarAtivo({
        tipo: formulario.tipo, nome: formulario.nome, descricao: formulario.descricao, criticidade: formulario.criticidade,
        atributos: { origem: 'mesa-do-catalogo', ...formulario.atributos }, idPai: formulario.paiId || undefined,
      });
      await onCadastrado();
      navegar(`/catalogo?ativo=${id}`);
    } catch (e) {
      // O que foi digitado continua no formulário: só a mensagem muda.
      onErro(e instanceof Error ? e.message : 'Não foi possível cadastrar o ativo.');
    } finally {
      setSalvando(false);
    }
  }

  if (!podeEscrever) {
    return (
      <>
        <Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" />
        <div className="panel screen-panel aviso-fase"><p>O seu papel só permite consulta. Para cadastrar, é preciso um papel de escrita; veja o que cada papel faz na <Link href="/cartilha">cartilha</Link>.</p></div>
      </>
    );
  }

  return (
    <>
      <Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" texto="O ativo nasce em rascunho, com código gerado pelo tipo. Depois, no catálogo, você completa responsáveis e evidências e submete." />
      <form className="panel screen-panel formulario-pagina" onSubmit={salvar}>
        <label>Tipo<select value={formulario.tipo} onChange={(e) => setFormulario({ ...formulario, tipo: e.target.value, paiId: '', atributos: {} })}>{tiposEstrutura.map((t) => <option key={t.chave} value={t.chave}>{t.rotulo}</option>)}</select></label>
        {tipo?.pai && (
          <label>{rotuloDoPai}
            <select required value={formulario.paiId} onChange={(e) => setFormulario({ ...formulario, paiId: e.target.value })}>
              <option value="">Selecione o item pai</option>
              {pais.map((pai) => <option key={pai.id} value={pai.id}>{pai.codigo} · {pai.nome}</option>)}
            </select>
            {pais.length === 0 && <small className="field-hint">Cadastre primeiro um item do tipo {rotuloDoPai} para continuar.</small>}
          </label>
        )}
        <label>Nome<input required value={formulario.nome} onChange={(e) => setFormulario({ ...formulario, nome: e.target.value })} placeholder={tipo?.rotulo ?? 'Nome do ativo'} /></label>
        <div className="form-grid">
          <label>Criticidade<select value={formulario.criticidade} onChange={(e) => setFormulario({ ...formulario, criticidade: e.target.value })}><option>baixa</option><option>media</option><option>alta</option><option>critica</option></select></label>
          <label>Estado inicial<input value="Rascunho" disabled /></label>
        </div>
        {campos.map((campo) => (
          <label key={campo.nome}>{campo.rotulo}
            {campo.opcoes
              ? <select required={campo.obrigatorio} value={formulario.atributos[campo.nome] ?? ''} onChange={(e) => setFormulario({ ...formulario, atributos: { ...formulario.atributos, [campo.nome]: e.target.value } })}><option value="">Selecione</option>{campo.opcoes.map((opcao) => <option key={opcao}>{opcao}</option>)}</select>
              : <textarea required={campo.obrigatorio} rows={3} value={formulario.atributos[campo.nome] ?? ''} onChange={(e) => setFormulario({ ...formulario, atributos: { ...formulario.atributos, [campo.nome]: e.target.value } })} />}
          </label>
        ))}
        <label>Descrição<textarea required rows={3} value={formulario.descricao} onChange={(e) => setFormulario({ ...formulario, descricao: e.target.value })} placeholder="Qual o papel deste item na empresa?" /></label>
        <div className="modal-actions">
          <Link className="outline-button" href="/catalogo">Cancelar</Link>
          <button className="primary-button" disabled={salvando || faltaPai}>{salvando ? 'Cadastrando...' : 'Cadastrar ativo'}</button>
        </div>
      </form>
    </>
  );
}
