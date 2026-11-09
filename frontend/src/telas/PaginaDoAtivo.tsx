import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { Check, Link2, Paperclip } from 'lucide-react';
import {
  abrirRevisao, analisarImpacto, anexarEvidencia, arquivarAtivo, atribuirResponsavel, avaliarPreCheck, compararRevisoes, encerrarResponsavel,
  consultarHistorico, descontinuarAtivo, editarAtivo, obterAtivo, relacionarAtivos, submeterAtivo,
  type Ancestral, type AtivoResumo, type Comparacao, type FichaDoAtivo as Ficha, type Historico as DadosDoHistorico,
  type Impacto, type Metamodelo, type NovaRelacao, type Pessoa, type PreCheck, type Squad,
} from '../api';
import { Criticidade, Status } from '../componentes';
import { Link } from '../roteador';
import { nomeDaPessoa } from '../usuarios';
import { Historico } from './Historico';
import { CartaoDeArquivamento, CartaoDeDescontinuacao, QuemDepende, podeSerArquivado, podeSerDescontinuado } from './Impacto';

// ── as abas da visão 360° (web.ABAS_ATIVO do painel) ──────────────────────

export const ABAS = [
  { chave: 'resumo', rotulo: 'Resumo' },
  { chave: 'relacoes', rotulo: 'Relações' },
  { chave: 'pessoas', rotulo: 'Pessoas' },
  { chave: 'evidencias', rotulo: 'Evidências' },
  { chave: 'historico', rotulo: 'Histórico' },
] as const;

export type Aba = (typeof ABAS)[number]['chave'];

/** A aba pedida na URL; desconhecida ou ausente, o resumo (test_aba_desconhecida_cai_no_resumo). */
export function abaDaUrl(valor: string | null | undefined): Aba {
  return ABAS.find((a) => a.chave === valor)?.chave ?? 'resumo';
}

/** O endereço de uma aba do ativo. Toda escrita volta para a aba de onde saiu. */
export function caminhoDaAba(id: string, aba: Aba): string {
  return aba === 'resumo' ? `/ativos/${id}` : `/ativos/${id}?aba=${aba}`;
}

/** O passo do caminho até a publicação e a aba que resolve a pendência (governanca.ETAPAS_PUBLICACAO). */
const ABA_DO_PASSO: Record<string, Aba> = {
  descrever: 'resumo', responsaveis: 'pessoas', conectar: 'relacoes', evidenciar: 'evidencias', qualidade: 'resumo',
};

/** O checklist só faz sentido enquanto o ativo caminha para a publicação. */
export function mostraCaminho(status: string): boolean {
  return status === 'rascunho' || status === 'em_revisao';
}

export function editavel(status: string): boolean {
  return status === 'rascunho' || status === 'em_revisao';
}

/**
 * Por que a ficha está só para leitura, ou nulo se dá para editar. A ficha
 * diz o motivo no topo, em vez de só esconder os formulários: quem não escolheu
 * quem é, quem está em modo leitura, o papel que não edita aquele tipo, ou o
 * estado do ativo.
 */
export function motivoDeSoLeitura(o: { identificado: boolean; modoLeitura: boolean; escreveNoTipo: boolean; status: string; rotuloDoTipo: string }): string | null {
  if (!o.identificado) return 'Você está só consultando. Para editar, escolha quem é você em Seu perfil.';
  if (o.modoLeitura) return 'Você está em modo de leitura. Para editar, troque o modo em Seu perfil.';
  if (!o.escreveNoTipo) return `Seu papel não edita ativos do tipo ${o.rotuloDoTipo.toLowerCase()}. A ficha fica para consulta.`;
  switch (o.status) {
    case 'em_validacao': return 'Em validação: a ficha fica só para leitura até as etapas serem decididas. Acompanhe em Validações.';
    case 'publicado': return 'Publicado: para mudar alguma coisa, abra uma revisão (botão "Abrir revisão" ao lado).';
    case 'descontinuado': return 'Descontinuado: a ficha não se edita mais.';
    case 'arquivado': return 'Arquivado: a ficha não se edita mais.';
    default: return null;
  }
}

const PAPEIS_DE_OWNERSHIP = [
  { chave: 'OwnerNegocial', rotulo: 'Owner negocial' },
  { chave: 'OwnerTecnico', rotulo: 'Owner técnico' },
  { chave: 'Arquiteto', rotulo: 'Arquiteto' },
  { chave: 'Mantenedor', rotulo: 'Mantenedor' },
];

// ── peças ─────────────────────────────────────────────────────────────────

export function TrilhaHierarquica({ trilha, atual }: { trilha: Ancestral[]; atual: string }) {
  if (trilha.length === 0) return null;
  return (
    <nav className="trilha" aria-label="Trilha hierárquica">
      <ol>
        {trilha.map((a) => <li key={a.id}><Link href={`/ativos/${a.id}`}>{a.nome}</Link></li>)}
        <li aria-current="page">{atual}</li>
      </ol>
    </nav>
  );
}

/** As cinco dimensões do score, cada uma com a barra (o cartão "Dimensões de qualidade" do painel). */
export const DIMENSOES = [
  { chave: 'completude', rotulo: 'Completude' },
  { chave: 'consistencia', rotulo: 'Consistência' },
  { chave: 'ownership', rotulo: 'Ownership' },
  { chave: 'evidencia', rotulo: 'Evidência' },
  { chave: 'temporalidade', rotulo: 'Temporalidade' },
] as const;

export function DimensoesDeQualidade({ preCheck }: { preCheck: PreCheck }) {
  return (
    <section className="detail-section" aria-labelledby="dimensoes-titulo">
      <span className="panel-label" id="dimensoes-titulo">Dimensões de qualidade</span>
      <ul className="lista-de-barras">
        {DIMENSOES.map((d) => {
          const valor = preCheck.dimensoes[d.chave] ?? 0;
          return (
            <li key={d.chave}>
              <div className="linha-da-barra"><span>{d.rotulo}</span><span className="discreto">{valor}%</span></div>
              <div className={valor < 70 ? 'barra baixa' : 'barra'} role="img" aria-label={`${d.rotulo}: ${valor}%`}><span style={{ width: `${valor}%` }} /></div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

/**
 * O rito da política e os alertas do pré-check (o cartão "Rito e alertas" do
 * painel). Em edição, os bloqueios já estão no caminho até a publicação e não
 * se repetem; fora dela, aparecem aqui.
 */
export function RitoEAlertas({ preCheck, emEdicao }: { preCheck: PreCheck; emEdicao: boolean }) {
  const p = preCheck.politica;
  const bloqueios = emEdicao ? [] : preCheck.bloqueios;
  return (
    <section className="detail-section" aria-labelledby="rito-da-ficha">
      <span className="panel-label" id="rito-da-ficha">{emEdicao ? 'Rito e alertas' : 'Pré-check de publicação'}</span>
      <p className="discreto">Política: {p.etapas.join(' → ')} · evidência mínima {p.evidenciaMinima} · score mínimo {p.scoreMinimo}% · prazo {p.slaHoras}h por etapa · revisar a cada {p.revisarACadaDias} dias</p>
      <ul className="lista-checagem">
        {bloqueios.map((b) => <li key={b.codigo} className="bloqueio">{b.mensagem}</li>)}
        {preCheck.alertas.map((a) => <li key={a} className="pendente">{a}</li>)}
        {preCheck.aprovado && preCheck.alertas.length === 0 && <li className="ok">Tudo pronto para publicar</li>}
        {preCheck.aprovado && preCheck.alertas.length > 0 && <li className="ok">Sem bloqueios: pode ser submetido</li>}
        {emEdicao && !preCheck.aprovado && preCheck.alertas.length === 0 && <li className="discreto">Os impedimentos estão no caminho até a publicação, acima.</li>}
      </ul>
    </section>
  );
}

/** De onde veio a evidência da relação, por extenso. */
export const ORIGEM_DA_EVIDENCIA: Record<string, string> = {
  manual: 'declarada', git: 'Git', openapi: 'OpenAPI', cmdb: 'CMDB', cicd: 'CI/CD',
};

/** Os filhos diretos na hierarquia (as linhas "filho · compõe" do mapa de relações do painel). */
export function FilhosDoAtivo({ filhos, rotuloDoTipo }: { filhos: AtivoResumo[]; rotuloDoTipo: (chave: string) => string }) {
  if (filhos.length === 0) return null;
  return (
    <section className="detail-section" aria-labelledby="filhos-titulo">
      <span className="panel-label" id="filhos-titulo">Compõem este ativo ({filhos.length})</span>
      <div className="ficha-list">
        {filhos.map((f) => (
          <div className="ficha-row" key={f.id}>
            <div><strong><Link href={`/ativos/${f.id}`}>{f.codigo}</Link> {f.nome}</strong><small>{rotuloDoTipo(f.tipo)} · filho na hierarquia</small></div>
            <Status status={f.status} />
          </div>
        ))}
      </div>
    </section>
  );
}

/** O que o tipo exige de responsáveis para publicar (a nota da aba Pessoas do painel). */
export function exigenciaDoTipo(tipo: { exigeOwnerNegocial: boolean; exigeOwnerTecnico: boolean } | undefined): string {
  if (!tipo) return '';
  const exigidos = [tipo.exigeOwnerNegocial && 'owner negocial', tipo.exigeOwnerTecnico && 'owner técnico'].filter(Boolean);
  return exigidos.length ? `Este tipo exige ${exigidos.join(' e ')}.` : 'Este tipo não tem papel obrigatório.';
}

export function CaminhoAtePublicacao({ id, preCheck }: { id: string; preCheck: PreCheck }) {
  const metas: Record<string, string> = {
    descrever: 'descrição e campos obrigatórios do tipo',
    responsaveis: 'papéis exigidos pelo tipo de ativo',
    conectar: 'hierarquia válida e sem dependência circular',
    evidenciar: `mínimo de ${preCheck.politica.evidenciaMinima} evidência(s)`,
    qualidade: `mínimo de ${preCheck.politica.scoreMinimo}% · atual ${preCheck.score}%`,
  };
  const concluidos = preCheck.caminho.filter((p) => p.concluido).length;
  return (
    <section className="detail-section caminho" aria-labelledby="caminho-titulo">
      <span className="panel-label" id="caminho-titulo">Caminho até a publicação · {concluidos} de {preCheck.caminho.length}</span>
      <ol className="passos">
        {preCheck.caminho.map((passo) => (
          <li key={passo.passo} className={passo.concluido ? 'passo feito' : 'passo'}>
            <span className={passo.concluido ? 'step-mark done' : 'step-mark'}>{passo.concluido && <Check size={13} aria-hidden="true" />}</span>
            <div>
              <Link href={caminhoDaAba(id, ABA_DO_PASSO[passo.passo] ?? 'resumo')}><strong>{passo.rotulo}</strong></Link>
              <small className="discreto"> {metas[passo.passo] ?? ''}</small>
              {passo.pendencias.length > 0 && <ul className="pendencias">{passo.pendencias.map((p) => <li key={p}>{p}</li>)}</ul>}
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}

/** Os ativos que a busca de destino oferece: os dos tipos sugeridos para a relação, sem o próprio. */
export function candidatosAoDestino(ativos: AtivoResumo[], idOrigem: string, tipoDeRelacao: string, metamodelo: Metamodelo | null, todos = false): AtivoResumo[] {
  const sugeridos = metamodelo?.relacoes.find((r) => r.chave === tipoDeRelacao)?.destinosSugeridos ?? [];
  return ativos.filter((a) => a.id !== idOrigem && (todos || sugeridos.length === 0 || sugeridos.includes(a.tipo.toLowerCase())));
}

/** "CAP-0001 · Simular" → o ativo. Aceita o código sozinho, também. */
export function destinoDigitado(texto: string, candidatos: AtivoResumo[]): AtivoResumo | undefined {
  const limpo = texto.trim().toLowerCase();
  if (!limpo) return undefined;
  return candidatos.find((a) => `${a.codigo} · ${a.nome}`.toLowerCase() === limpo || a.codigo.toLowerCase() === limpo);
}

/** A mensagem da recusa antes de chamar a API; nulo quando dá para enviar (test_relacao_sem_destino_avisa_em_vez_de_quebrar). */
export function problemaDaRelacao(texto: string, destino: AtivoResumo | undefined): string | null {
  if (!texto.trim()) return 'Escolha um ativo de destino.';
  if (!destino) return 'Nenhum ativo com esse código ou nome. Escolha um da lista.';
  return null;
}

type PropsDaRelacao = {
  id: string;
  ativos: AtivoResumo[];
  metamodelo: Metamodelo | null;
  ocupado: boolean;
  onRelacionar: (idDestino: string, relacao: NovaRelacao) => void;
  onErro: (mensagem: string) => void;
};

export function FormularioDeRelacao({ id, ativos, metamodelo, ocupado, onRelacionar, onErro }: PropsDaRelacao) {
  const [tipo, setTipo] = useState(metamodelo?.relacoes[0]?.chave ?? 'depende_de');
  const [todos, setTodos] = useState(false);
  const candidatos = candidatosAoDestino(ativos, id, tipo, metamodelo, todos);

  function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const dados = new FormData(evento.currentTarget);
    const texto = dados.get('destino')?.toString() ?? '';
    const destino = destinoDigitado(texto, candidatosAoDestino(ativos, id, tipo, metamodelo, true));
    const problema = problemaDaRelacao(texto, destino);
    if (problema) { onErro(problema); return; }
    onRelacionar(destino!.id, {
      tipo, criticidade: dados.get('criticidade')?.toString() ?? 'media', mecanismo: dados.get('mecanismo')?.toString() || undefined,
    });
    evento.currentTarget.reset();
  }

  return (
    <form className="decisao-pleito" onSubmit={enviar} aria-label="Registrar relação">
      <label>Tipo de relação
        <select name="tipo_relacao" value={tipo} onChange={(e) => setTipo(e.target.value)}>
          {(metamodelo?.relacoes ?? [{ chave: 'depende_de', rotulo: 'depende de', destinosSugeridos: [] }]).map((r) => <option key={r.chave} value={r.chave}>{r.rotulo}</option>)}
        </select>
      </label>
      <label className="largo">Ativo de destino
        <input id="destino-busca" name="destino" list="destinos-sugeridos" autoComplete="off" placeholder="Digite o código ou o nome" />
        <datalist id="destinos-sugeridos">
          {candidatos.map((a) => <option key={a.id} value={`${a.codigo} · ${a.nome}`}>{a.tipo}</option>)}
        </datalist>
        <small className="ajuda">
          {todos ? 'Mostrando todos os ativos.' : 'Sugestões pelos tipos que fazem sentido para esta relação.'}{' '}
          <button type="button" className="text-button" onClick={() => setTodos(!todos)}>{todos ? 'Só os sugeridos' : 'Ver todos'}</button>
        </small>
      </label>
      <label>Mecanismo
        <select name="mecanismo" defaultValue="">
          <option value="">não informado</option>
          {(metamodelo?.mecanismos ?? []).map((m) => <option key={m.chave} value={m.chave}>{m.rotulo}</option>)}
        </select>
      </label>
      <label>Criticidade da dependência
        <select name="criticidade" defaultValue="media"><option value="baixa">baixa</option><option value="media">média</option><option value="alta">alta</option><option value="critica">crítica</option></select>
      </label>
      <div className="modal-actions">
        <Link className="outline-button" href={`/ativos/${id}/relacoes-lote?tipo=${tipo}`}>Relacionar em lote</Link>
        <button className="primary-button" disabled={ocupado}>Registrar relação</button>
      </div>
    </form>
  );
}

// ── a visão inteira ──────────────────────────────────────────────────────

export type Acoes = {
  editar: (dados: { nome: string; descricao: string; criticidade: string; atributos: Record<string, string>; idSquad: string | null }) => void;
  submeter: (motivo: string) => void;
  abrirRevisao: () => void;
  atribuir: (idPessoa: string, papel: string) => void;
  encerrar: (idPessoa: string, papel: string) => void;
  anexar: (evidencia: { tipo: string; titulo: string; url?: string }) => void;
  relacionar: (idDestino: string, relacao: NovaRelacao) => void;
  comparar: () => void;
  descontinuar: (motivo: string, confirmacao: string) => Promise<void>;
  arquivar: () => Promise<void>;
};

type PropsDaVisao = {
  ficha: Ficha;
  preCheck: PreCheck | null;
  aba: Aba;
  ativos: AtivoResumo[];
  pessoas: Pessoa[];
  metamodelo: Metamodelo | null;
  squads: Squad[];
  historico: DadosDoHistorico | null;
  comparacao: Comparacao | null;
  impacto: Impacto | null;
  podeEscrever: boolean;
  podeDescontinuar: boolean;
  idPessoa: string | null;
  ocupado: boolean;
  acoes: Acoes;
  onErro: (mensagem: string) => void;
  /** Por que a ficha está só para leitura; nulo quando dá para editar. */
  motivoDeSoLeitura?: string | null;
};

/**
 * A visão 360° do painel (/ativo/&lt;id&gt;): todas as abas no HTML, só a escolhida
 * visível, como o painel faz (test_visao_360_abre_no_resumo_e_esconde_as_outras_abas).
 */
export function VisaoDoAtivo(props: PropsDaVisao) {
  const { ficha, preCheck, aba, podeEscrever, acoes } = props;
  const rotuloDoTipo = props.metamodelo?.tipos.find((t) => t.chave === ficha.tipoItem)?.rotulo ?? ficha.tipoItem;
  const contagem: Partial<Record<Aba, number>> = {
    relacoes: ficha.relacoes.length, pessoas: ficha.responsaveis.filter((r) => r.vigente).length, evidencias: ficha.evidencias.length,
  };

  const painel = (chave: Aba, conteudo: ReactNode) => (
    <section className="painel-aba" id={`painel-${chave}`} role="tabpanel" aria-labelledby={`aba-${chave}`} hidden={aba !== chave}>{conteudo}</section>
  );

  return (
    <>
      <TrilhaHierarquica trilha={ficha.trilha} atual={ficha.nome} />
      <header className="cabecalho-do-ativo">
        <div>
          <span className="panel-label">{ficha.codigo} · {rotuloDoTipo}</span>
          <h1>{ficha.nome}</h1>
        </div>
        <div className="etiquetas"><Status status={ficha.status} /><Criticidade valor={ficha.criticidade} /></div>
      </header>
      {props.motivoDeSoLeitura && (
        <p className="aviso-somente-leitura" role="note">
          {props.motivoDeSoLeitura}
          {props.motivoDeSoLeitura.includes('Seu perfil') && <> <Link href="/perfil">Ir para Seu perfil</Link></>}
        </p>
      )}

      <div className="ficha-tabs" role="tablist" aria-label="Seções do ativo">
        {ABAS.map((a) => (
          <Link key={a.chave} id={`aba-${a.chave}`} role="tab" aria-selected={aba === a.chave} aria-controls={`painel-${a.chave}`}
            className={aba === a.chave ? 'ficha-tab active' : 'ficha-tab'} href={caminhoDaAba(ficha.id, a.chave)}>
            {a.rotulo}{contagem[a.chave] !== undefined && <span className="nav-count">{contagem[a.chave]}</span>}
          </Link>
        ))}
      </div>

      <div className="panel screen-panel">
        {painel('resumo', <Resumo {...props} />)}
        {painel('relacoes', <Relacoes {...props} />)}
        {painel('pessoas', <Pessoas {...props} />)}
        {painel('evidencias', <Evidencias {...props} />)}
        {painel('historico', props.historico
          ? <Historico historico={props.historico} comparacao={props.comparacao} onComparar={acoes.comparar} />
          : <p className="vazio">Carregando o histórico...</p>)}
      </div>
      {!podeEscrever && !props.motivoDeSoLeitura && <p className="readonly-note">Seu papel, ou o modo de leitura, só permite consulta.</p>}
      {preCheck === null && <p className="vazio">Avaliando o pré-check...</p>}
    </>
  );
}

function Resumo({ ficha, preCheck, podeEscrever, ocupado, acoes, metamodelo, squads }: PropsDaVisao) {
  const squad = squads.find((s) => s.id === ficha.idSquad);
  const campos = metamodelo?.tipos.find((t) => t.chave === ficha.tipoItem)?.campos ?? [];
  const rotuloDoCampo = (chave: string) => campos.find((c) => c.nome === chave)?.rotulo ?? chave;
  const chaves = [...new Set([...campos.map((c) => c.nome), ...Object.keys(ficha.atributos)])].filter((c) => c !== 'origem');

  function salvar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    acoes.editar({
      nome: d.get('nome')?.toString() ?? '', descricao: d.get('descricao')?.toString() ?? '', criticidade: d.get('criticidade')?.toString() ?? 'media',
      atributos: Object.fromEntries(chaves.map((c) => [c, d.get(`attr_${c}`)?.toString() ?? ''])),
      idSquad: d.get('id_squad')?.toString() || null,
    });
  }

  return (
    <div className="resumo-do-ativo">
      <div>
        {podeEscrever && editavel(ficha.status) ? (
          <form className="formulario-pagina" onSubmit={salvar} key={`${ficha.id}-${ficha.revisaoAtual}-${ficha.nome}`}>
            <label>Nome<input name="nome" required defaultValue={ficha.nome} /></label>
            <label>Criticidade
              <select name="criticidade" defaultValue={ficha.criticidade}><option value="baixa">baixa</option><option value="media">média</option><option value="alta">alta</option><option value="critica">crítica</option></select>
            </label>
            <label>Squad
              <select name="id_squad" defaultValue={ficha.idSquad ?? ''}>
                <option value="">sem squad</option>
                {squads.filter((s) => s.ativa || s.id === ficha.idSquad).map((s) => <option key={s.id} value={s.id}>{s.sigla} · {s.titulo}</option>)}
              </select>
            </label>
            <label>Descrição<textarea name="descricao" rows={3} required defaultValue={ficha.descricao} /></label>
            {chaves.map((c) => {
              const campo = campos.find((x) => x.nome === c);
              return (
                <label key={c}>{rotuloDoCampo(c)}{campo?.obrigatorio && ' *'}
                  {campo?.opcoes.length
                    ? <select name={`attr_${c}`} defaultValue={ficha.atributos[c] ?? ''}><option value="">Selecione</option>{campo.opcoes.map((o) => <option key={o}>{o}</option>)}</select>
                    : <input name={`attr_${c}`} defaultValue={ficha.atributos[c] ?? ''} />}
                </label>
              );
            })}
            <div className="modal-actions"><button className="primary-button" disabled={ocupado}>Salvar alterações</button></div>
          </form>
        ) : (
          <dl className="dados-do-ativo">
            <div><dt>Descrição</dt><dd>{ficha.descricao || '—'}</dd></div>
            <div><dt>Squad</dt><dd>{squad ? `${squad.sigla} · ${squad.titulo}` : '—'}</dd></div>
            {chaves.map((c) => <div key={c}><dt>{rotuloDoCampo(c)}</dt><dd>{ficha.atributos[c] || '—'}</dd></div>)}
            <div><dt>Revisão</dt><dd>#{ficha.revisaoAtual}</dd></div>
          </dl>
        )}
      </div>
      <aside>
        {preCheck && (
          <div className="score-block">
            <div><span className="panel-label">PRÉ-CHECK</span><strong>{preCheck.score}<small>/100</small></strong></div>
            <div className={preCheck.aprovado ? 'score-ring good' : 'score-ring'}>{preCheck.score}</div>
          </div>
        )}
        {preCheck && <p className="proxima-revisao discreto">Próxima revisão <strong>{preCheck.proximaRevisao}</strong></p>}
        {preCheck && mostraCaminho(ficha.status) && <CaminhoAtePublicacao id={ficha.id} preCheck={preCheck} />}
        {preCheck && <RitoEAlertas preCheck={preCheck} emEdicao={editavel(ficha.status)} />}
        {podeEscrever && ficha.status === 'publicado' && (
          <div className="detail-actions"><button className="primary-button" disabled={ocupado} onClick={acoes.abrirRevisao}>Abrir revisão</button></div>
        )}
        {podeEscrever && editavel(ficha.status) && (
          <form className="formulario-pagina submissao" aria-label="Enviar para validação"
            onSubmit={(e) => { e.preventDefault(); acoes.submeter(new FormData(e.currentTarget).get('motivo')?.toString().trim() ?? ''); }}>
            <label>Motivo da revisão<textarea name="motivo" rows={2} placeholder="O que mudou e por quê. Aparece no histórico e para quem valida." /></label>
            <div className="detail-actions">
              <button className="primary-button" disabled={ocupado || !preCheck?.aprovado}>Submeter para validação</button>
            </div>
          </form>
        )}
        {preCheck && <DimensoesDeQualidade preCheck={preCheck} />}
      </aside>
    </div>
  );
}

function Relacoes({ ficha, ativos, metamodelo, podeEscrever, podeDescontinuar, impacto, ocupado, acoes, onErro }: PropsDaVisao) {
  const rotuloDaRelacao = (chave: string) => metamodelo?.relacoes.find((r) => r.chave === chave)?.rotulo ?? chave.replace('_', ' ');
  const rotuloDoMecanismo = (chave: string | null) => (chave && metamodelo?.mecanismos.find((m) => m.chave === chave)?.rotulo) || chave;
  return (
    <>
      {ficha.relacoes.length === 0
        ? <p className="vazio">Nenhuma relação registrada.</p>
        : <div className="ficha-list">{ficha.relacoes.map((rel, i) => (
            <div className="ficha-row" key={`${rel.ativoId}-${rel.tipo}-${rel.direcao}-${i}`}>
              <Link2 size={15} aria-hidden="true" />
              <div>
                <strong>{rel.direcao === 'saida' ? <>{rotuloDaRelacao(rel.tipo)} <Link href={`/ativos/${rel.ativoId}`}>{rel.codigo}</Link></> : <><Link href={`/ativos/${rel.ativoId}`}>{rel.codigo}</Link> {rotuloDaRelacao(rel.tipo)} este</>}</strong>
                <small>{rel.nome}{rel.mecanismo && ` · ${rotuloDoMecanismo(rel.mecanismo)}`} · evidência {ORIGEM_DA_EVIDENCIA[rel.origemDaEvidencia] ?? rel.origemDaEvidencia}</small>
              </div>
              <Criticidade valor={rel.criticidade} />
            </div>
          ))}</div>}
      {podeEscrever && editavel(ficha.status) && (
        <FormularioDeRelacao id={ficha.id} ativos={ativos} metamodelo={metamodelo} ocupado={ocupado} onRelacionar={acoes.relacionar} onErro={onErro} />
      )}
      <FilhosDoAtivo filhos={ficha.filhos ?? []} rotuloDoTipo={(c) => metamodelo?.tipos.find((t) => t.chave === c)?.rotulo ?? c} />
      <p><Link href={`/ativos/${ficha.id}/grafo`}>Ver o grafo de relações deste ativo</Link></p>
      {impacto && <QuemDepende impacto={impacto} />}
      {impacto && podeDescontinuar && podeSerDescontinuado(ficha.status) && (
        <CartaoDeDescontinuacao key={ficha.status} impacto={impacto} onDescontinuar={acoes.descontinuar} />
      )}
      {podeDescontinuar && podeSerArquivado(ficha.status) && <CartaoDeArquivamento status={ficha.status} onArquivar={acoes.arquivar} />}
    </>
  );
}

function Pessoas({ ficha, pessoas, podeEscrever, idPessoa, ocupado, acoes, metamodelo }: PropsDaVisao) {
  function atribuir(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    const quem = d.get('idPessoa')?.toString();
    if (quem) acoes.atribuir(quem, d.get('papel')?.toString() ?? 'OwnerTecnico');
  }
  return (
    <>
      {ficha.responsaveis.length === 0
        ? <p className="vazio">Nenhum responsável atribuído ainda.</p>
        : <div className="ficha-list">{ficha.responsaveis.map((r, i) => (
            <div className="ficha-row" key={`${r.idPessoa}-${r.papel}-${i}`}>
              <div><strong>{nomeDaPessoa(r.idPessoa)}</strong><small>{PAPEIS_DE_OWNERSHIP.find((p) => p.chave === r.papel)?.rotulo ?? r.papel} · desde {r.inicioVigencia}{r.fimVigencia ? ` até ${r.fimVigencia}` : ''}</small></div>
              <span className={r.vigente ? 'status status-good' : 'status'}>{r.vigente ? 'vigente' : 'encerrado'}</span>
              {r.vigente && podeEscrever && editavel(ficha.status) && (
                <button type="button" className="link-button" disabled={ocupado} onClick={() => acoes.encerrar(r.idPessoa, r.papel)}
                  aria-label={`Encerrar ${nomeDaPessoa(r.idPessoa)} como ${PAPEIS_DE_OWNERSHIP.find((p) => p.chave === r.papel)?.rotulo ?? r.papel}`}>Encerrar</button>
              )}
            </div>
          ))}</div>}
      <p className="discreto exigencia-do-tipo">{exigenciaDoTipo(metamodelo?.tipos.find((t) => t.chave === ficha.tipoItem))}</p>
      {podeEscrever && editavel(ficha.status) && (
        <form className="decisao-pleito" onSubmit={atribuir} aria-label="Definir responsável">
          <label>Pessoa
            <select name="idPessoa" defaultValue={idPessoa ?? ''}>
              <option value="">Selecione</option>
              {pessoas.map((p) => <option key={p.id} value={p.id}>{p.nome}{p.id === idPessoa ? ' (você)' : ''}</option>)}
            </select>
          </label>
          <label>Papel no ativo
            <select name="papel" defaultValue="OwnerTecnico">{PAPEIS_DE_OWNERSHIP.map((p) => <option key={p.chave} value={p.chave}>{p.rotulo}</option>)}</select>
          </label>
          <p className="ajuda largo">Cada papel tem uma pessoa só: definir outra encerra quem estava, e a troca fica no histórico.</p>
          <div className="modal-actions"><button className="primary-button" disabled={ocupado}>Definir responsável</button></div>
        </form>
      )}
    </>
  );
}

function Evidencias({ ficha, podeEscrever, ocupado, acoes }: PropsDaVisao) {
  function anexar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    const titulo = d.get('titulo')?.toString().trim() ?? '';
    if (!titulo) return;
    acoes.anexar({ tipo: d.get('tipo')?.toString() ?? 'documento', titulo, url: d.get('url')?.toString() || undefined });
    evento.currentTarget.reset();
  }
  return (
    <>
      {ficha.evidencias.length === 0
        ? <p className="vazio">Nenhuma evidência anexada.</p>
        : <div className="ficha-list">{ficha.evidencias.map((ev) => (
            <div className="ficha-row" key={ev.id}>
              <Paperclip size={15} aria-hidden="true" />
              <div><strong>{ev.titulo}</strong>{ev.url && <small><a href={ev.url} target="_blank" rel="noreferrer">{ev.url}</a></small>}</div>
              <span className="tag">{ev.tipo}</span>
            </div>
          ))}</div>}
      {podeEscrever && editavel(ficha.status) && (
        <form className="decisao-pleito" onSubmit={anexar} aria-label="Anexar evidência">
          <label>Título<input name="titulo" required placeholder="Ex.: ADR-0012" /></label>
          <label>Tipo
            <select name="tipo" defaultValue="documento"><option value="documento">Documento</option><option value="adr">ADR</option><option value="openapi">OpenAPI</option><option value="repositorio">Repositório</option><option value="link">Link</option></select>
          </label>
          <label className="largo">URL (opcional)<input name="url" placeholder="https://..." /></label>
          <div className="modal-actions"><button className="primary-button" disabled={ocupado}>Anexar evidência</button></div>
        </form>
      )}
    </>
  );
}

// ── a página ─────────────────────────────────────────────────────────────

type Props = {
  id: string;
  aba: Aba;
  ativos: AtivoResumo[];
  pessoas: Pessoa[];
  metamodelo: Metamodelo | null;
  squads: Squad[];
  podeEscrever: boolean;
  podeDescontinuar: boolean;
  idPessoa: string | null;
  /** Os blocos de tipos em que o papel da pessoa escreve (perfil.blocosQueEscreve). */
  blocos?: string[];
  identificado?: boolean;
  modoLeitura?: boolean;
  onAlterado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

export function PaginaDoAtivo({
  id, aba, ativos, pessoas, metamodelo, squads, podeEscrever, podeDescontinuar, idPessoa,
  blocos = [], identificado = true, modoLeitura = false, onAlterado, onErro,
}: Props) {
  const [ficha, setFicha] = useState<Ficha | null>(null);
  const [preCheck, setPreCheck] = useState<PreCheck | null>(null);
  const [impacto, setImpacto] = useState<Impacto | null>(null);
  const [historico, setHistorico] = useState<DadosDoHistorico | null>(null);
  const [comparacao, setComparacao] = useState<Comparacao | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [versao, setVersao] = useState(0);

  useEffect(() => {
    let vigente = true;
    Promise.all([obterAtivo(id), avaliarPreCheck(id), analisarImpacto(id).catch(() => null)])
      .then(([f, p, i]) => { if (vigente) { setFicha(f); setPreCheck(p); setImpacto(i); } })
      .catch((e) => { if (vigente) onErro(e instanceof Error ? e.message : 'Não foi possível carregar o ativo.'); });
    return () => { vigente = false; };
  }, [id, versao]);

  // O histórico só é buscado quando a aba abre, e de novo a cada mudança no ativo.
  useEffect(() => {
    if (aba !== 'historico') return;
    let vigente = true;
    setComparacao(null);
    consultarHistorico(id).then((h) => { if (vigente) setHistorico(h); }).catch(() => {});
    return () => { vigente = false; };
  }, [id, aba, versao]);

  async function agir(acao: () => Promise<unknown>, falha: string) {
    setOcupado(true);
    onErro('');
    try {
      await acao();
      setVersao((v) => v + 1);
      await onAlterado();
    } catch (e) {
      onErro(e instanceof Error ? e.message : falha);
    } finally {
      setOcupado(false);
    }
  }

  if (!ficha) return <p className="vazio">Carregando o ativo...</p>;

  const acoes: Acoes = {
    editar: (dados) => void agir(() => editarAtivo(id, dados), 'Não foi possível salvar as alterações.'),
    submeter: (motivo) => void agir(() => submeterAtivo(id, motivo), 'Não foi possível submeter.'),
    abrirRevisao: () => void agir(() => abrirRevisao(id), 'Não foi possível abrir a revisão.'),
    atribuir: (quem, papel) => void agir(() => atribuirResponsavel(id, quem, papel), 'Não foi possível definir o responsável.'),
    encerrar: (quem, papel) => void agir(() => encerrarResponsavel(id, quem, papel), 'Não foi possível encerrar o responsável.'),
    anexar: (ev) => void agir(() => anexarEvidencia(id, ev), 'Não foi possível anexar a evidência.'),
    relacionar: (destino, relacao) => void agir(async () => {
      const { criada } = await relacionarAtivos(id, destino, relacao);
      if (!criada) onErro('Essa relação já existia; nada mudou.');
    }, 'Não foi possível criar a relação.'),
    comparar: () => void compararRevisoes(id).then(setComparacao).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível comparar as revisões.')),
    descontinuar: (motivo, confirmacao) => agir(() => descontinuarAtivo(id, motivo, confirmacao || undefined), 'Não foi possível descontinuar o ativo.'),
    arquivar: () => agir(() => arquivarAtivo(id), 'Não foi possível arquivar o ativo.'),
  };

  // O papel escreve em blocos de tipos: quem edita sistemas não edita domínios, e vice-versa.
  const tipo = metamodelo?.tipos.find((t) => t.chave === ficha.tipoItem);
  const escreveNoTipo = podeEscrever && (!tipo || blocos.length === 0 || blocos.includes(tipo.bloco));
  const motivo = motivoDeSoLeitura({
    identificado, modoLeitura, escreveNoTipo: identificado && !modoLeitura ? escreveNoTipo : true,
    status: ficha.status, rotuloDoTipo: tipo?.rotulo ?? ficha.tipoItem,
  });

  return (
    <VisaoDoAtivo ficha={ficha} preCheck={preCheck} aba={aba} ativos={ativos} pessoas={pessoas} metamodelo={metamodelo} squads={squads}
      historico={historico} comparacao={comparacao} impacto={impacto} podeEscrever={escreveNoTipo} podeDescontinuar={podeDescontinuar}
      idPessoa={idPessoa} ocupado={ocupado} acoes={acoes} onErro={onErro} motivoDeSoLeitura={motivo} />
  );
}
