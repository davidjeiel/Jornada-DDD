import { useEffect, useState, type FormEvent } from 'react';
import { cadastrarAtivo, consultarRito, type AtivoResumo, type Metamodelo, type Rito, type Squad, type TipoDeAtivo } from '../api';
import { Cabecalho } from '../componentes';
import { Link, navegar } from '../roteador';

// O assistente de cadastro do painel (/ativo/novo): lê o metamodelo da empresa
// (GET /v1/metamodelo), oferece só os blocos que o papel escreve
// (acesso.BLOCOS_POR_PAPEL, via GET /v1/perfil) e antecipa o rito do tipo e da
// criticidade (GET /v1/ritos) antes de salvar. Recusado, o que foi digitado
// continua no formulário: o estado não é limpo no erro.

export type Formulario = {
  tipo: string; nome: string; descricao: string; criticidade: string; paiId: string; idSquad: string; atributos: Record<string, string>;
};

const CRITICIDADE: Record<string, string> = { baixa: 'baixa', media: 'média', alta: 'alta', critica: 'crítica' };

/** O que o tipo exige para publicar (o "Exigências deste tipo" do assistente do painel). */
export function ExigenciasDoTipo({ tipo, rotuloDoPai }: { tipo: TipoDeAtivo; rotuloDoPai: string }) {
  return (
    <>
      <span className="panel-label">Exigências deste tipo</span>
      <ul className="lista-checagem">
        <li className={tipo.exigeOwnerNegocial ? 'pendente' : 'ok'}>Owner negocial {tipo.exigeOwnerNegocial ? 'obrigatório' : 'opcional'}</li>
        <li className={tipo.exigeOwnerTecnico ? 'pendente' : 'ok'}>Owner técnico {tipo.exigeOwnerTecnico ? 'obrigatório' : 'opcional'}</li>
        {tipo.pai && <li className="pendente">Vínculo com {rotuloDoPai}</li>}
        {tipo.campos.filter((c) => c.obrigatorio).map((c) => <li key={c.nome} className="pendente">{c.rotulo} é obrigatório para publicar</li>)}
      </ul>
    </>
  );
}

/** "negocial → tecnica → arquitetural", como o painel escreve o rito. */
export function etapasDoRito(rito: Rito): string {
  return rito.etapas.join(' → ');
}

/** Os tipos que o assistente oferece: os do metamodelo, nos blocos que o papel escreve. */
export function tiposOferecidos(metamodelo: Metamodelo | null, blocos: string[]): TipoDeAtivo[] {
  return (metamodelo?.tipos ?? []).filter((t) => blocos.includes(t.bloco));
}

type PropsDoAssistente = {
  oferecidos: TipoDeAtivo[];
  metamodelo: Metamodelo;
  ativos: AtivoResumo[];
  squads: Squad[];
  formulario: Formulario;
  rito: Rito | null;
  salvando: boolean;
  onMudar: (formulario: Formulario) => void;
  onSalvar: () => void;
};

/** O formulário controlado: tudo o que aparece vem de `formulario`, e nada se perde num erro. */
export function AssistenteDeCadastro({ oferecidos, metamodelo, ativos, squads, formulario, rito, salvando, onMudar, onSalvar }: PropsDoAssistente) {
  const tipo = oferecidos.find((t) => t.chave === formulario.tipo) ?? oferecidos[0];
  const rotuloDoPai = metamodelo.tipos.find((t) => t.chave === tipo.pai)?.rotulo;
  const pais = ativos.filter((a) => a.tipo.toLowerCase() === tipo.pai);
  const faltaPai = !!tipo.pai && (!formulario.paiId || pais.length === 0);
  const blocos = [...new Set(oferecidos.map((t) => t.bloco))];
  const mudar = (campos: Partial<Formulario>) => onMudar({ ...formulario, ...campos });
  const atributo = (nome: string, valor: string) => mudar({ atributos: { ...formulario.atributos, [nome]: valor } });

  function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    onSalvar();
  }

  return (
    <div className="assistente">
      <form className="panel screen-panel formulario-pagina" onSubmit={enviar}>
        <label>Tipo
          <select name="tipo_item" value={tipo.chave} onChange={(e) => mudar({ tipo: e.target.value, paiId: '', atributos: {} })}>
            {blocos.map((bloco) => (
              <optgroup key={bloco} label={bloco}>
                {oferecidos.filter((t) => t.bloco === bloco).map((t) => <option key={t.chave} value={t.chave}>{t.rotulo}</option>)}
              </optgroup>
            ))}
          </select>
        </label>
        {tipo.pai && (
          <label>{rotuloDoPai}
            <select name="id_pai" required value={formulario.paiId} onChange={(e) => mudar({ paiId: e.target.value })}>
              <option value="">Selecione o item pai</option>
              {pais.map((pai) => <option key={pai.id} value={pai.id}>{pai.codigo} · {pai.nome}</option>)}
            </select>
            {pais.length === 0 && <small className="field-hint">Cadastre primeiro um item do tipo {rotuloDoPai} para continuar.</small>}
          </label>
        )}
        <label>Nome<input name="nome" required value={formulario.nome} onChange={(e) => mudar({ nome: e.target.value })} placeholder={tipo.rotulo} /></label>
        <div className="form-grid">
          <label>Criticidade
            <select name="criticidade" value={formulario.criticidade} onChange={(e) => mudar({ criticidade: e.target.value })}>
              <option value="baixa">baixa</option><option value="media">média</option><option value="alta">alta</option><option value="critica">crítica</option>
            </select>
          </label>
          <label>Squad
            <select name="id_squad" value={formulario.idSquad} onChange={(e) => mudar({ idSquad: e.target.value })}>
              <option value="">sem squad</option>
              {squads.filter((s) => s.ativa).map((s) => <option key={s.id} value={s.id}>{s.sigla} · {s.titulo}</option>)}
            </select>
          </label>
        </div>
        {tipo.campos.map((campo) => (
          <label key={campo.nome}>{campo.rotulo}{campo.obrigatorio && ' *'}
            {campo.opcoes.length > 0
              ? <select name={`attr_${campo.nome}`} required={campo.obrigatorio} value={formulario.atributos[campo.nome] ?? ''} onChange={(e) => atributo(campo.nome, e.target.value)}><option value="">Selecione</option>{campo.opcoes.map((o) => <option key={o} value={o}>{o}</option>)}</select>
              : campo.tipo === 'area'
                ? <textarea name={`attr_${campo.nome}`} required={campo.obrigatorio} rows={3} value={formulario.atributos[campo.nome] ?? ''} onChange={(e) => atributo(campo.nome, e.target.value)} />
                : <input name={`attr_${campo.nome}`} type={campo.tipo === 'url' ? 'url' : 'text'} required={campo.obrigatorio} value={formulario.atributos[campo.nome] ?? ''} onChange={(e) => atributo(campo.nome, e.target.value)} />}
          </label>
        ))}
        <label>Descrição<textarea name="descricao" required rows={3} value={formulario.descricao} onChange={(e) => mudar({ descricao: e.target.value })} placeholder="Qual o papel deste item na empresa?" /></label>
        <div className="modal-actions">
          <Link className="outline-button" href="/catalogo">Cancelar</Link>
          <button className="primary-button" disabled={salvando || faltaPai}>{salvando ? 'Cadastrando...' : 'Cadastrar ativo'}</button>
        </div>
      </form>

      <aside className="panel screen-panel rito" aria-labelledby="rito-titulo">
        <span className="panel-label" id="rito-titulo">Rito de publicação</span>
        {rito ? (
          <>
            <p><strong>{tipo.rotulo}</strong>{rito.criticidade !== '*' && <> de criticidade <strong>{CRITICIDADE[rito.criticidade] ?? rito.criticidade}</strong></>} passa por:</p>
            <p className="etapas-do-rito">{etapasDoRito(rito)}</p>
            <dl className="dados-do-ativo">
              <div><dt>Score mínimo</dt><dd>{rito.scoreMinimo}%</dd></div>
              <div><dt>Evidências mínimas</dt><dd>{rito.evidenciaMinima}</dd></div>
              <div><dt>Prazo por etapa</dt><dd>{rito.slaHoras}h</dd></div>
              <div><dt>Revisar a cada</dt><dd>{rito.revisarACadaDias} dias</dd></div>
            </dl>
            <p className="discreto">O ativo nasce em rascunho. Responsáveis e evidências você completa na ficha, antes de submeter.</p>
          </>
        ) : <p className="vazio">Consultando o rito...</p>}
        <ExigenciasDoTipo tipo={tipo} rotuloDoPai={metamodelo?.tipos.find((t) => t.chave === tipo.pai)?.rotulo ?? tipo.pai ?? ''} />
        <span className="panel-label">O que vem depois</span>
        <ol className="o-que-vem-depois">
          <li>Salvar o rascunho com os campos do tipo</li>
          <li>Definir os responsáveis, na aba Pessoas da ficha</li>
          <li>Registrar as relações (implementa, expõe, depende de)</li>
          <li>Anexar as evidências que a política exige</li>
          <li>Enviar para validação: o pré-check roda antes</li>
        </ol>
      </aside>
    </div>
  );
}

type Props = {
  ativos: AtivoResumo[];
  metamodelo: Metamodelo | null;
  squads: Squad[];
  /** Blocos em que o papel concedido escreve. Vazio: sem papel de escrita, ou em modo leitura. */
  blocos: string[];
  identificado: boolean;
  modoLeitura: boolean;
  /** Os papéis concedidos: o arquiteto, que só valida, recebe outro aviso. */
  papeis?: string[];
  /** O que veio na URL (?tipo=api&criticidade=critica), como o assistente do painel aceita. */
  tipoInicial?: string;
  criticidadeInicial?: string;
  onCadastrado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

export function NovoAtivo({ ativos, metamodelo, squads, blocos, identificado, modoLeitura, papeis = [], tipoInicial, criticidadeInicial, onCadastrado, onErro }: Props) {
  const oferecidos = tiposOferecidos(metamodelo, blocos);
  const [formulario, setFormulario] = useState<Formulario>({
    tipo: tipoInicial ?? '', nome: '', descricao: '', criticidade: criticidadeInicial ?? 'media', paiId: '', idSquad: '', atributos: {},
  });
  const [rito, setRito] = useState<Rito | null>(null);
  const [salvando, setSalvando] = useState(false);
  const tipo = oferecidos.find((t) => t.chave === formulario.tipo) ?? oferecidos[0];

  useEffect(() => {
    if (!tipo) return;
    let vigente = true;
    setRito(null);
    consultarRito(tipo.chave, formulario.criticidade).then((r) => { if (vigente) setRito(r); }).catch(() => {});
    return () => { vigente = false; };
  }, [tipo?.chave, formulario.criticidade]);

  async function salvar() {
    if (!tipo) return;
    setSalvando(true);
    onErro('');
    try {
      const { id } = await cadastrarAtivo({
        tipo: tipo.chave, nome: formulario.nome, descricao: formulario.descricao, criticidade: formulario.criticidade,
        atributos: { origem: 'mesa-do-catalogo', ...formulario.atributos }, idPai: formulario.paiId || undefined, idSquad: formulario.idSquad || undefined,
      });
      await onCadastrado();
      navegar(`/ativos/${id}`);
    } catch (e) {
      // O que foi digitado continua no formulário: só a mensagem muda.
      onErro(e instanceof Error ? e.message : 'Não foi possível cadastrar o ativo.');
    } finally {
      setSalvando(false);
    }
  }

  if (!metamodelo && blocos.length > 0) return <><Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" /><p className="vazio">Carregando os tipos de ativo...</p></>;

  if (!tipo || !metamodelo) {
    // Sem bloco de escrita, o assistente manda pedir papel (test_sem_papel_o_wizard_manda_pleitear).
    return (
      <>
        <Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" />
        <div className="panel screen-panel aviso-fase">
          {modoLeitura
            ? <p>Você está em modo de leitura. Para cadastrar, troque para edição no <Link href="/perfil">seu perfil</Link>.</p>
            : !identificado
              ? <p>Diga quem você é no <Link href="/perfil">seu perfil</Link> antes de cadastrar. Ainda sem cadastro? <Link href="/acesso/solicitar">Cadastre-se e peça um papel</Link>.</p>
              : papeis.includes('arquiteto')
                ? <p>O arquiteto valida, não cadastra: quem descreve o negócio e a técnica são o negócio e o time técnico. As etapas arquiteturais esperam por você em <Link href="/validacoes?etapa=arquitetural">Validações</Link>.</p>
                : <p>O seu papel só permite consulta. Para cadastrar, <Link href="/acesso/solicitar">peça um papel de escrita</Link>; veja o que cada papel faz na <Link href="/cartilha">cartilha</Link>.</p>}
        </div>
      </>
    );
  }

  return (
    <>
      <Cabecalho rotulo="TRABALHAR" titulo="Novo ativo" texto="O ativo nasce em rascunho, com código gerado pelo tipo. Ao lado, o rito que ele vai seguir até a publicação." />
      <AssistenteDeCadastro oferecidos={oferecidos} metamodelo={metamodelo} ativos={ativos} squads={squads}
        formulario={{ ...formulario, tipo: tipo.chave }} rito={rito} salvando={salvando} onMudar={setFormulario} onSalvar={() => void salvar()} />
    </>
  );
}
