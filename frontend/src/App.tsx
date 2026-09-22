import { useEffect, useMemo, useState } from 'react';
import {
  Activity,
  ArrowUpRight,
  Check,
  ChevronRight,
  CircleAlert,
  Database,
  FilePlus2,
  LayoutGrid,
  RefreshCw,
  Search,
  ShieldCheck,
  X,
} from 'lucide-react';
import {
  atribuirResponsavel,
  decidirValidacao,
  avaliarPreCheck,
  cadastrarAtivo,
  listarAtivos,
  listarValidacoes,
  obterAtivo,
  submeterAtivo,
  type AtivoResumo,
  type PreCheck,
  type Validacao,
} from './api';

type Filtro = 'todos' | 'dominio' | 'subdominio' | 'contexto' | 'capacidade' | 'sistema' | 'api' | 'processo';
type Formulario = { tipo: string; nome: string; descricao: string; criticidade: string; paiId: string; atributos: Record<string, string> };

const formularioInicial: Formulario = { tipo: 'dominio', nome: '', descricao: '', criticidade: 'media', paiId: '', atributos: {} };
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

function App() {
  const [ativos, setAtivos] = useState<AtivoResumo[]>([]);
  const [selecionado, setSelecionado] = useState<AtivoResumo | null>(null);
  const [preCheck, setPreCheck] = useState<PreCheck | null>(null);
  const [detalhe, setDetalhe] = useState<Record<string, unknown> | null>(null);
  const [busca, setBusca] = useState('');
  const [filtro, setFiltro] = useState<Filtro>('todos');
  const [formulario, setFormulario] = useState<Formulario>(formularioInicial);
  const [modalAberto, setModalAberto] = useState(false);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');
  const [salvando, setSalvando] = useState(false);
  const [acao, setAcao] = useState<'owner' | 'submeter' | null>(null);
  const [validacoes, setValidacoes] = useState<Validacao[]>([]);
  const [tela, setTela] = useState<'visao' | 'catalogo' | 'governanca' | 'indicadores'>('visao');

  async function carregar() {
    setCarregando(true);
    setErro('');
    try {
      const [resposta, fila] = await Promise.all([listarAtivos(), listarValidacoes()]);
      setAtivos(resposta.ativos);
      setValidacoes(fila.validacoes);
      if (selecionado) {
        const atualizado = resposta.ativos.find((ativo) => ativo.id === selecionado.id);
        if (atualizado) await selecionar(atualizado);
      }
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível carregar o catálogo.');
    } finally {
      setCarregando(false);
    }
  }

  async function selecionar(ativo: AtivoResumo) {
    setSelecionado(ativo);
    setErro('');
    try {
      const [visao, avaliacao, fila] = await Promise.all([obterAtivo(ativo.id), avaliarPreCheck(ativo.id), listarValidacoes(ativo.id)]);
      setDetalhe(visao);
      setPreCheck(avaliacao);
      setValidacoes(fila.validacoes);
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível carregar o detalhe.');
    }
  }

  useEffect(() => {
    void carregar();
  }, []);

  const ativosFiltrados = useMemo(() => ativos.filter((ativo) => {
    const correspondeBusca = `${ativo.nome} ${ativo.codigo} ${ativo.tipo}`.toLowerCase().includes(busca.toLowerCase());
    const correspondeFiltro = filtro === 'todos' || ativo.tipo.toLowerCase() === filtro;
    return correspondeBusca && correspondeFiltro;
  }), [ativos, busca, filtro]);

  const aprovados = ativos.filter((ativo) => ativo.status.toLowerCase().includes('public')).length;
  const pendentes = ativos.length - aprovados;
  const tipoFormulario = tiposEstrutura.find((tipo) => tipo.chave === formulario.tipo);
  const paisDisponiveis = ativos.filter((ativo) => ativo.tipo.toLowerCase() === tipoFormulario?.pai);
  const camposFormulario = camposEstrutura[formulario.tipo] ?? [];

  function alterarTipo(tipo: string) {
    setFormulario({ ...formulario, tipo, paiId: '', atributos: {} });
  }

  async function salvar(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSalvando(true);
    setErro('');
    try {
      await cadastrarAtivo({ tipo: formulario.tipo, nome: formulario.nome, descricao: formulario.descricao, criticidade: formulario.criticidade, atributos: { origem: 'mesa-do-catalogo', ...formulario.atributos }, idPai: formulario.paiId || undefined });
      setFormulario(formularioInicial);
      setModalAberto(false);
      await carregar();
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível cadastrar o ativo.');
    } finally {
      setSalvando(false);
    }
  }

  async function executarAcao(tipo: 'owner' | 'submeter') {
    if (!selecionado) return;
    setAcao(tipo);
    setErro('');
    try {
      if (tipo === 'owner') {
        await atribuirResponsavel(selecionado.id);
      } else {
        await submeterAtivo(selecionado.id, 'Enviado pela mesa do catálogo');
      }
      await carregar();
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível concluir a ação.');
    } finally {
      setAcao(null);
    }
  }

  async function decidir(id: string, aprovada: boolean) {
    setAcao('submeter');
    setErro('');
    try {
      await decidirValidacao(id, aprovada, aprovada ? 'Aprovado pela revisão técnica' : 'Precisa de ajustes antes da publicação');
      if (selecionado) await selecionar(selecionado);
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível registrar a decisão.');
    } finally {
      setAcao(null);
    }
  }

  return (
    <main className={`shell screen-${tela}`}>
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark">J</span><span>Jornada <b>DDD</b></span></div>
        <div className="tenant"><span className="tenant-dot" /> Empresa Horizonte <ChevronRight size={15} /></div>
        <nav className="nav" aria-label="Navegação principal">
          <button className={tela === 'visao' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('visao')}><LayoutGrid size={18} /> Visão geral</button>
          <button className={tela === 'catalogo' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('catalogo')}><Database size={18} /> Catálogo <span className="nav-count">{ativos.length}</span></button>
          <button className={tela === 'governanca' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('governanca')}><ShieldCheck size={18} /> Governança <span className="nav-count">{validacoes.filter((v) => v.status === 'aberta').length}</span></button>
          <button className={tela === 'indicadores' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('indicadores')}><Activity size={18} /> Indicadores</button>
        </nav>
        <div className="sidebar-footer"><span className="avatar">MC</span><div><strong>Marina Costa</strong><small>Curadora</small></div><ChevronRight size={15} /></div>
      </aside>

      <section className="content">
        <header className="topbar"><div><span className="eyebrow">DATA PLANE / CATÁLOGO</span><h1>{tela === 'visao' ? 'Visão geral' : tela === 'catalogo' ? 'Catálogo' : tela === 'governanca' ? 'Governança' : 'Indicadores'}</h1></div><button className="outline-button" onClick={() => void carregar()}><RefreshCw size={16} /> Atualizar</button></header>
        <div className="intro"><div><p className="kicker">QUARTA-FEIRA, 22 DE SETEMBRO DE 2026</p><h2>{tela === 'visao' ? 'O catálogo está em movimento.' : tela === 'catalogo' ? 'A estrutura do negócio, visível.' : tela === 'governanca' ? 'Decisões com rastreabilidade.' : 'Sinais para orientar a próxima decisão.'}</h2><p className="muted">{tela === 'catalogo' ? 'Explore os quatro níveis da arquitetura DDD e seus ativos governados.' : tela === 'governanca' ? 'Acompanhe validações abertas, responsáveis e decisões pendentes.' : tela === 'indicadores' ? 'Uma leitura operacional da qualidade e do ciclo de vida do catálogo.' : 'Acompanhe a saúde dos ativos e mantenha cada decisão rastreável.'}</p></div>{tela !== 'governanca' && <button className="primary-button" onClick={() => setModalAberto(true)}><FilePlus2 size={17} /> Cadastrar ativo</button>}</div>

        {erro && <div className="error-banner"><CircleAlert size={18} /> {erro}<button onClick={() => setErro('')} aria-label="Fechar erro"><X size={16} /></button></div>}

        {tela === 'catalogo' && <CatalogoScreen ativos={ativos} onSelecionar={(ativo) => void selecionar(ativo)} onCadastrar={() => setModalAberto(true)} />}
        {tela === 'governanca' && <GovernancaScreen validacoes={validacoes} onDecidir={decidir} acao={acao} />}
        {tela === 'indicadores' && <IndicadoresScreen ativos={ativos} validacoes={validacoes} />}

        <div className="metrics"><Metric label="Ativos no catálogo" value={ativos.length} accent="ink" note="total registrado" /><Metric label="Publicados" value={aprovados} accent="mint" note="prontos para consumo" /><Metric label="Em avaliação" value={pendentes} accent="amber" note="pedem atenção" /><Metric label="Cobertura de dados" value={ativos.length ? '78%' : '—'} accent="coral" note="completude média" /></div>

        <div className="workspace-grid">
          <section className="panel catalog-panel"><div className="panel-heading"><div><span className="panel-label">INVENTÁRIO</span><h3>Ativos recentes</h3></div><span className="result-count">{ativosFiltrados.length} resultados</span></div><div className="toolbar"><label className="search"><Search size={17} /><input placeholder="Buscar por nome, código ou tipo" value={busca} onChange={(event) => setBusca(event.target.value)} /></label><div className="filters">{(['todos', 'dominio', 'subdominio', 'contexto', 'capacidade', 'sistema', 'api', 'processo'] as Filtro[]).map((opcao) => <button key={opcao} className={filtro === opcao ? 'filter active' : 'filter'} onClick={() => setFiltro(opcao)}>{opcao === 'todos' ? 'Todos' : opcao}</button>)}</div></div><div className="asset-list">{carregando ? <div className="empty-state">Carregando catálogo...</div> : ativosFiltrados.length === 0 ? <div className="empty-state">Nenhum ativo encontrado.</div> : ativosFiltrados.map((ativo) => <button className={selecionado?.id === ativo.id ? 'asset-row selected' : 'asset-row'} key={ativo.id} onClick={() => void selecionar(ativo)}><span className={`asset-icon type-${ativo.tipo.toLowerCase()}`}>{ativo.tipo.slice(0, 1).toUpperCase()}</span><span className="asset-copy"><strong>{ativo.nome}</strong><small>{ativo.codigo} <i>•</i> {ativo.tipo}</small></span><Status status={ativo.status} /><span className={`critical critical-${ativo.criticidade.toLowerCase()}`}>{ativo.criticidade}</span><ArrowUpRight size={17} className="row-arrow" /></button>)}</div></section>
          <aside className="panel detail-panel">{selecionado ? <><div className="detail-heading"><div><span className="panel-label">ATIVO SELECIONADO</span><h3>{selecionado.nome}</h3><span className="code">{selecionado.codigo}</span></div><span className="detail-type">{selecionado.tipo}</span></div><div className="score-block"><div><span className="panel-label">PRE-CHECK</span><strong>{preCheck?.score ?? '—'}<small>/100</small></strong></div><div className={preCheck?.aprovado ? 'score-ring good' : 'score-ring'}>{preCheck?.score ?? '—'}</div></div><div className="dimension-list">{Object.entries(preCheck?.dimensoes ?? {}).map(([nome, valor]) => <div className="dimension" key={nome}><span>{nome}</span><div className="bar"><i style={{ width: `${valor}%` }} /></div><b>{valor}</b></div>)}</div><div className="detail-section"><span className="panel-label">CAMINHO DE PUBLICAÇÃO</span>{preCheck?.caminho?.slice(0, 3).map((passo) => <div className="step" key={passo.passo}><span className={passo.concluido ? 'step-mark done' : 'step-mark'}>{passo.concluido && <Check size={13} />}</span><span>{passo.rotulo}</span><small>{passo.concluido ? 'concluído' : 'pendente'}</small></div>)}</div>{preCheck && !preCheck.aprovado && <div className="notice"><CircleAlert size={17} /><div><strong>Há pontos para revisar</strong><p>{preCheck.bloqueios[0]?.mensagem ?? 'Complete os dados antes de submeter.'}</p></div></div>}<div className="detail-actions"><button className="outline-button" disabled={acao !== null} onClick={() => void executarAcao('owner')}>{acao === 'owner' ? 'Assumindo...' : 'Assumir como owner'}</button><button className="primary-button" disabled={acao !== null || !preCheck?.aprovado} onClick={() => void executarAcao('submeter')}>{acao === 'submeter' ? 'Enviando...' : 'Submeter'}</button></div><button className="text-button">Abrir ficha completa <ArrowUpRight size={15} /></button></> : <div className="empty-detail"><span className="detail-placeholder"><LayoutGrid size={22} /></span><h3>Selecione um ativo</h3><p>O pre-check e o caminho de publicação aparecem aqui.</p></div>}</aside>
        </div>
        {detalhe && <span className="sr-only">Detalhe carregado: {JSON.stringify(detalhe)}</span>}
      </section>

      {modalAberto && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && setModalAberto(false)}><form className="modal" onSubmit={salvar}><div className="modal-header"><div><span className="panel-label">ESTRUTURA DDD</span><h3>Cadastrar item</h3></div><button type="button" className="icon-button" onClick={() => setModalAberto(false)} aria-label="Fechar"><X size={19} /></button></div><label>Tipo<select value={formulario.tipo} onChange={(event) => alterarTipo(event.target.value)}>{tiposEstrutura.map((tipo) => <option key={tipo.chave} value={tipo.chave}>{tipo.rotulo}</option>)}</select></label>{tipoFormulario?.pai && <label>{tiposEstrutura.find((tipo) => tipo.chave === tipoFormulario.pai)?.rotulo}<select required value={formulario.paiId} onChange={(event) => setFormulario({ ...formulario, paiId: event.target.value })}><option value="">Selecione o item pai</option>{paisDisponiveis.map((pai) => <option key={pai.id} value={pai.id}>{pai.codigo} · {pai.nome}</option>)}</select>{paisDisponiveis.length === 0 && <small className="field-hint">Cadastre primeiro um {tipoFormulario.pai} para continuar.</small>}</label>}<label>Nome<input required value={formulario.nome} onChange={(event) => setFormulario({ ...formulario, nome: event.target.value })} placeholder={tipoFormulario?.rotulo ?? 'Nome do ativo'} /></label><div className="form-grid"><label>Criticidade<select value={formulario.criticidade} onChange={(event) => setFormulario({ ...formulario, criticidade: event.target.value })}><option>baixa</option><option>media</option><option>alta</option><option>critica</option></select></label><label>Estado inicial<input value="Rascunho" disabled /></label></div>{camposFormulario.map((campo) => <label key={campo.nome}>{campo.rotulo}{campo.opcoes ? <select required={campo.obrigatorio} value={formulario.atributos[campo.nome] ?? ''} onChange={(event) => setFormulario({ ...formulario, atributos: { ...formulario.atributos, [campo.nome]: event.target.value } })}><option value="">Selecione</option>{campo.opcoes.map((opcao) => <option key={opcao}>{opcao}</option>)}</select> : <textarea required={campo.obrigatorio} rows={3} value={formulario.atributos[campo.nome] ?? ''} onChange={(event) => setFormulario({ ...formulario, atributos: { ...formulario.atributos, [campo.nome]: event.target.value } })} />}</label>)}<label>Descrição<textarea required rows={3} value={formulario.descricao} onChange={(event) => setFormulario({ ...formulario, descricao: event.target.value })} placeholder="Qual o papel deste item na empresa?" /></label><div className="modal-actions"><button type="button" className="outline-button" onClick={() => setModalAberto(false)}>Cancelar</button><button className="primary-button" disabled={salvando || (!!tipoFormulario?.pai && !formulario.paiId) || paisDisponiveis.length === 0 && !!tipoFormulario?.pai}>{salvando ? 'Cadastrando...' : 'Cadastrar item'}</button></div></form></div>}
    </main>
  );
}

function CatalogoScreen({ ativos, onSelecionar, onCadastrar }: { ativos: AtivoResumo[]; onSelecionar: (ativo: AtivoResumo) => void; onCadastrar: () => void }) {
  const niveis = ['dominio', 'subdominio', 'contexto', 'capacidade'];
  return <section className="screen-grid catalog-screen"><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">ESTRUTURA DDD</span><h3>Mapa do catálogo</h3></div><button className="primary-button" onClick={onCadastrar}><FilePlus2 size={16} /> Novo item</button></div><div className="hierarchy-grid">{niveis.map((nivel) => <div className="hierarchy-column" key={nivel}><span className="panel-label">{nivel}</span><strong>{ativos.filter((ativo) => ativo.tipo.toLowerCase() === nivel).length}</strong><small>{nivel === 'dominio' ? 'fronteiras de negócio' : nivel === 'subdominio' ? 'áreas de capacidade' : nivel === 'contexto' ? 'modelos delimitados' : 'resultados esperados'}</small></div>)}</div></div><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">INVENTÁRIO</span><h3>Todos os ativos</h3></div><span className="result-count">{ativos.length} itens</span></div><div className="compact-list">{ativos.length === 0 ? <div className="empty-state">Nenhum ativo cadastrado.</div> : ativos.map((ativo) => <button className="compact-row" key={ativo.id} onClick={() => onSelecionar(ativo)}><span className="asset-icon">{ativo.tipo.slice(0, 1).toUpperCase()}</span><span><strong>{ativo.nome}</strong><small>{ativo.codigo} · {ativo.tipo}</small></span><Status status={ativo.status} /><ArrowUpRight size={16} /></button>)}</div></div></section>;
}

function GovernancaScreen({ validacoes, onDecidir, acao }: { validacoes: Validacao[]; onDecidir: (id: string, aprovada: boolean) => void; acao: 'owner' | 'submeter' | null }) {
  const abertas = validacoes.filter((validacao) => validacao.status === 'aberta');
  return <section className="screen-grid governance-screen"><div className="panel screen-panel governance-summary"><span className="panel-label">FILA DE GOVERNANÇA</span><strong>{abertas.length}</strong><p>validações aguardando decisão</p><div className="governance-bars"><span style={{ width: `${validacoes.length ? (abertas.length / validacoes.length) * 100 : 0}%` }} /></div><small>{validacoes.length - abertas.length} concluídas</small></div><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">REVISÕES</span><h3>Decisões pendentes</h3></div><span className="result-count">{validacoes.length} total</span></div><div className="compact-list">{validacoes.length === 0 ? <div className="empty-state">Submeta um ativo para abrir uma validação.</div> : validacoes.map((validacao) => <div className="validation-row" key={validacao.id}><span className={validacao.status === 'aberta' ? 'step-mark' : 'step-mark done'}>{validacao.status === 'aberta' ? '!' : <Check size={13} />}</span><span><strong>{validacao.etapa}</strong><small>revisão {validacao.revisao} · {validacao.status}</small></span>{validacao.status === 'aberta' ? <span className="validation-buttons"><button className="text-button" disabled={acao !== null} onClick={() => onDecidir(validacao.id, false)}>Ajustes</button><button className="text-button" disabled={acao !== null} onClick={() => onDecidir(validacao.id, true)}>Aprovar</button></span> : <span className="status status-good"><i /> concluída</span>}</div>)}</div></div></section>;
}

function IndicadoresScreen({ ativos, validacoes }: { ativos: AtivoResumo[]; validacoes: Validacao[] }) {
  const tipos = [...new Set(ativos.map((ativo) => ativo.tipo))];
  const publicados = ativos.filter((ativo) => ativo.status.toLowerCase().includes('public')).length;
  return <section className="screen-grid indicator-screen"><div className="indicator-cards"><Metric label="Ativos totais" value={ativos.length} accent="ink" note="na empresa atual" /><Metric label="Taxa publicados" value={ativos.length ? `${Math.round((publicados / ativos.length) * 100)}%` : '0%'} accent="mint" note={`${publicados} publicados`} /><Metric label="Validações" value={validacoes.length} accent="amber" note={`${validacoes.filter((v) => v.status === 'aberta').length} abertas`} /></div><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">DISTRIBUIÇÃO</span><h3>Composição do catálogo</h3></div><span className="result-count">{tipos.length} tipos</span></div><div className="indicator-list">{tipos.map((tipo) => { const total = ativos.filter((ativo) => ativo.tipo === tipo).length; return <div className="indicator-line" key={tipo}><span>{tipo}</span><div className="bar"><i style={{ width: `${ativos.length ? (total / ativos.length) * 100 : 0}%` }} /></div><b>{total}</b></div>; })}</div></div></section>;
}

function Metric({ label, value, note, accent }: { label: string; value: string | number; note: string; accent: string }) { return <div className={`metric metric-${accent}`}><span>{label}</span><strong>{value}</strong><small>{note}</small></div>; }
function Status({ status }: { status: string }) { const publicado = status.toLowerCase().includes('public'); return <span className={publicado ? 'status status-good' : 'status'}><i />{status}</span>; }

export default App;
