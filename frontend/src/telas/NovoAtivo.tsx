import { useState, type FormEvent } from 'react';
import { cadastrarAtivo, type AtivoResumo } from '../api';
import { Cabecalho } from '../componentes';
import { Link, navegar } from '../roteador';

type Formulario = { tipo: string; nome: string; descricao: string; criticidade: string; paiId: string; atributos: Record<string, string> };

// Os tipos do metamodelo padrão (CatalogoDeTipos.Padrao), por bloco. O
// assistente que lê o metamodelo da empresa e antecipa o rito do tipo chega na
// fase 3; o que já vale é o painel: só os blocos que o seu papel escreve
// (acesso.BLOCOS_POR_PAPEL, que a API devolve em GET /v1/perfil).
export const BLOCO_ESTRUTURA = 'Estrutura DDD';
export const BLOCO_TECNICO = 'Ativos técnicos';

type Campo = { nome: string; rotulo: string; obrigatorio?: boolean; opcoes?: string[] };
type TipoDoFormulario = { chave: string; rotulo: string; pai: string | null; bloco: string; campos: Campo[] };

export const TIPOS: TipoDoFormulario[] = [
  { chave: 'dominio', rotulo: 'Domínio', pai: null, bloco: BLOCO_ESTRUTURA, campos: [{ nome: 'visao', rotulo: 'Visão de negócio', obrigatorio: true }] },
  { chave: 'subdominio', rotulo: 'Subdomínio', pai: 'dominio', bloco: BLOCO_ESTRUTURA, campos: [{ nome: 'classificacao', rotulo: 'Classificação', obrigatorio: true, opcoes: ['core', 'suporte', 'genérico'] }] },
  { chave: 'contexto', rotulo: 'Contexto delimitado', pai: 'subdominio', bloco: BLOCO_ESTRUTURA, campos: [{ nome: 'linguagem_ubiqua', rotulo: 'Termos da linguagem ubíqua' }, { nome: 'estrategia_integracao', rotulo: 'Estratégia de integração', opcoes: ['parceria', 'cliente-fornecedor', 'conformista', 'camada anticorrupção', 'serviço aberto'] }] },
  { chave: 'capacidade', rotulo: 'Capacidade de negócio', pai: 'contexto', bloco: BLOCO_ESTRUTURA, campos: [{ nome: 'resultado_esperado', rotulo: 'Resultado esperado', obrigatorio: true }, { nome: 'processo_negocio', rotulo: 'Processo de negócio' }] },
  { chave: 'sistema', rotulo: 'Sistema', pai: null, bloco: BLOCO_TECNICO, campos: [{ nome: 'plataforma', rotulo: 'Plataforma', obrigatorio: true, opcoes: ['mainframe', 'distribuído', 'nuvem', 'híbrido'] }, { nome: 'situacao', rotulo: 'Situação', opcoes: ['legado', 'em modernização', 'estratégico'] }] },
  { chave: 'aplicacao', rotulo: 'Aplicação', pai: 'sistema', bloco: BLOCO_TECNICO, campos: [{ nome: 'linguagem', rotulo: 'Linguagem principal' }, { nome: 'repositorio_url', rotulo: 'Repositório' }] },
  { chave: 'repositorio', rotulo: 'Repositório', pai: 'aplicacao', bloco: BLOCO_TECNICO, campos: [{ nome: 'url', rotulo: 'URL', obrigatorio: true }] },
  { chave: 'api', rotulo: 'API', pai: 'aplicacao', bloco: BLOCO_TECNICO, campos: [{ nome: 'contrato_url', rotulo: 'Contrato OpenAPI', obrigatorio: true }, { nome: 'versao', rotulo: 'Versão' }] },
  { chave: 'endpoint', rotulo: 'Endpoint', pai: 'api', bloco: BLOCO_TECNICO, campos: [{ nome: 'metodo', rotulo: 'Método', opcoes: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] }, { nome: 'caminho', rotulo: 'Caminho' }] },
  { chave: 'base_dados', rotulo: 'Base de dados', pai: 'sistema', bloco: BLOCO_TECNICO, campos: [{ nome: 'tecnologia', rotulo: 'Tecnologia' }] },
  { chave: 'objeto_dado', rotulo: 'Objeto de dado', pai: 'base_dados', bloco: BLOCO_TECNICO, campos: [{ nome: 'classificacao_dado', rotulo: 'Classificação do dado', opcoes: ['público', 'interno', 'confidencial', 'restrito'] }] },
  { chave: 'evento', rotulo: 'Evento de negócio', pai: 'contexto', bloco: BLOCO_TECNICO, campos: [{ nome: 'schema_url', rotulo: 'Schema' }, { nome: 'mecanismo', rotulo: 'Mecanismo', opcoes: ['sincrono', 'assincrono', 'batch'] }] },
];

type Props = {
  ativos: AtivoResumo[];
  /** Blocos em que o papel concedido escreve. Vazio: sem papel de escrita, ou em modo leitura. */
  blocos: string[];
  identificado: boolean;
  modoLeitura: boolean;
  onCadastrado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

export function NovoAtivo({ ativos, blocos, identificado, modoLeitura, onCadastrado, onErro }: Props) {
  const oferecidos = TIPOS.filter((t) => blocos.includes(t.bloco));
  const [formulario, setFormulario] = useState<Formulario>(() => ({
    tipo: oferecidos[0]?.chave ?? 'dominio', nome: '', descricao: '', criticidade: 'media', paiId: '', atributos: {},
  }));
  const [salvando, setSalvando] = useState(false);

  const tipo = oferecidos.find((t) => t.chave === formulario.tipo) ?? oferecidos[0];
  const rotuloDoPai = TIPOS.find((t) => t.chave === tipo?.pai)?.rotulo;
  const pais = ativos.filter((ativo) => ativo.tipo.toLowerCase() === tipo?.pai);
  const campos = tipo?.campos ?? [];
  const faltaPai = !!tipo?.pai && (!formulario.paiId || pais.length === 0);

  async function salvar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    if (!tipo) return;
    setSalvando(true);
    onErro('');
    try {
      const { id } = await cadastrarAtivo({
        tipo: tipo.chave, nome: formulario.nome, descricao: formulario.descricao, criticidade: formulario.criticidade,
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

  if (!tipo) {
    // Sem bloco de escrita, o assistente manda pedir papel (test_sem_papel_o_wizard_manda_pleitear).
    return (
      <>
        <Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" />
        <div className="panel screen-panel aviso-fase">
          {modoLeitura
            ? <p>Você está em modo de leitura. Para cadastrar, troque para edição no <Link href="/perfil">seu perfil</Link>.</p>
            : !identificado
              ? <p>Diga quem você é no <Link href="/perfil">seu perfil</Link> antes de cadastrar. Ainda sem cadastro? <Link href="/acesso/solicitar">Cadastre-se e peça um papel</Link>.</p>
              : <p>O seu papel só permite consulta. Para cadastrar, <Link href="/acesso/solicitar">peça um papel de escrita</Link>; veja o que cada papel faz na <Link href="/cartilha">cartilha</Link>.</p>}
        </div>
      </>
    );
  }

  const grupos = [BLOCO_ESTRUTURA, BLOCO_TECNICO].filter((b) => blocos.includes(b));

  return (
    <>
      <Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" texto="O ativo nasce em rascunho, com código gerado pelo tipo. Depois, no catálogo, você completa responsáveis e evidências e submete." />
      <form className="panel screen-panel formulario-pagina" onSubmit={salvar}>
        <label>Tipo
          <select value={tipo.chave} onChange={(e) => setFormulario({ ...formulario, tipo: e.target.value, paiId: '', atributos: {} })}>
            {grupos.map((bloco) => (
              <optgroup key={bloco} label={bloco}>
                {oferecidos.filter((t) => t.bloco === bloco).map((t) => <option key={t.chave} value={t.chave}>{t.rotulo}</option>)}
              </optgroup>
            ))}
          </select>
        </label>
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
