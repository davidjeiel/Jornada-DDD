import { useEffect, useMemo, useState } from 'react';
import { marked } from 'marked';
import {
  Activity,
  ArrowUpRight,
  BookOpen,
  Check,
  ChevronRight,
  CircleAlert,
  Database,
  FilePlus2,
  LayoutGrid,
  Link2,
  Moon,
  Paperclip,
  Plus,
  RefreshCw,
  Search,
  ShieldCheck,
  Sun,
  X,
} from 'lucide-react';
// Fonte única: o mesmo docs/CARTILHA.md que orienta quem lê o repositório.
// Nada de copiar o texto para dentro do frontend — é exatamente o que a
// própria cartilha pede para não acontecer (ver o aviso no topo do arquivo).
import cartilhaMd from '../../docs/CARTILHA.md?raw';
import {
  anexarEvidencia,
  atribuirResponsavel,
  decidirValidacao,
  avaliarPreCheck,
  cadastrarAtivo,
  definirUsuarioAtual,
  editarAtivo,
  empresaDe,
  listarAtivos,
  listarValidacoes,
  obterAtivo,
  obterUsuarioAtual,
  relacionarAtivos,
  submeterAtivo,
  unidadeDe,
  type AtivoResumo,
  type FichaDoAtivo,
  type PreCheck,
  type Validacao,
} from './api';
import { iniciaisDe, salvarUsuario, usuariosDeTeste, type UsuarioDeTeste } from './usuarios';

type Filtro = 'todos' | 'dominio' | 'subdominio' | 'contexto' | 'capacidade' | 'sistema' | 'api' | 'processo';
type Formulario = { tipo: string; nome: string; descricao: string; criticidade: string; paiId: string; atributos: Record<string, string> };
type Edicao = { nome: string; descricao: string; criticidade: string; atributos: Record<string, string> };
type AbaFicha = 'detalhes' | 'evidencias' | 'relacoes';
type Tema = 'light' | 'dark';

// ── Cartilha: um menu fixo por seção (## do markdown), carregado sob demanda ──
type TokenMarked = ReturnType<typeof marked.lexer>[number];
type SecaoCartilha = { titulo: string; html: string };

function dividirCartilhaEmSecoes(md: string): SecaoCartilha[] {
  const tokens = marked.lexer(md);
  const grupos: { titulo: string; tokens: TokenMarked[] }[] = [{ titulo: 'Visão geral', tokens: [] }];
  for (const token of tokens) {
    if (token.type === 'heading' && token.depth === 2) {
      grupos.push({ titulo: token.text, tokens: [] });
    } else {
      grupos[grupos.length - 1].tokens.push(token);
    }
  }
  return grupos
    .filter((grupo) => grupo.tokens.length > 0)
    .map((grupo) => ({ titulo: grupo.titulo, html: marked.parser(grupo.tokens) as string }));
}

// ── Tema: sem valor salvo, quem decide é o navegador (prefers-color-scheme).
// Salvar um valor FORÇA aquele tema, ignorando o sistema — é o que o botão faz.
const CHAVE_TEMA = 'jornada-ddd:tema';

function obterTemaForcado(): Tema | null {
  try {
    const valor = localStorage.getItem(CHAVE_TEMA);
    return valor === 'light' || valor === 'dark' ? valor : null;
  } catch {
    return null;
  }
}

function salvarTemaForcado(tema: Tema): void {
  try {
    localStorage.setItem(CHAVE_TEMA, tema);
  } catch {
    // per-viewer apenas: falhar aqui não pode quebrar o toggle.
  }
}

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
const tiposDeRelacao = ['depende_de', 'implementa', 'expoe', 'consome'];

// Papel RBAC (X-Papel) → papel de OWNERSHIP mais provável para quem assume o
// ativo. É só uma sugestão de UX; o servidor decide de verdade (ADR-0007).
function papelDeOwnershipSugerido(papel: UsuarioDeTeste['papel']): string {
  if (papel === 'negocio') return 'OwnerNegocial';
  if (papel === 'arquiteto') return 'Arquiteto';
  return 'OwnerTecnico';
}

const edicaoInicial: Edicao = { nome: '', descricao: '', criticidade: 'media', atributos: {} };

function App() {
  const [usuario, setUsuario] = useState<UsuarioDeTeste>(() => obterUsuarioAtual());
  const [menuUsuarioAberto, setMenuUsuarioAberto] = useState(false);

  const [ativos, setAtivos] = useState<AtivoResumo[]>([]);
  const [selecionado, setSelecionado] = useState<AtivoResumo | null>(null);
  const [preCheck, setPreCheck] = useState<PreCheck | null>(null);
  const [ficha, setFicha] = useState<FichaDoAtivo | null>(null);
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

  const [modalFichaAberto, setModalFichaAberto] = useState(false);
  const [abaFicha, setAbaFicha] = useState<AbaFicha>('detalhes');
  const [edicao, setEdicao] = useState<Edicao>(edicaoInicial);
  const [salvandoEdicao, setSalvandoEdicao] = useState(false);
  const [novaEvidencia, setNovaEvidencia] = useState({ tipo: 'documento', titulo: '', url: '' });
  const [salvandoEvidencia, setSalvandoEvidencia] = useState(false);
  const [novaRelacao, setNovaRelacao] = useState({ destinoId: '', tipo: tiposDeRelacao[0] });
  const [salvandoRelacao, setSalvandoRelacao] = useState(false);

  const [cartilhaAberta, setCartilhaAberta] = useState(false);
  const [secaoCartilhaAtiva, setSecaoCartilhaAtiva] = useState(0);
  const secoesCartilha = useMemo(() => dividirCartilhaEmSecoes(cartilhaMd), []);

  const [temaForcado, setTemaForcado] = useState<Tema | null>(() => obterTemaForcado());
  const [temaEscuro, setTemaEscuro] = useState(() =>
    temaForcado ? temaForcado === 'dark' : window.matchMedia('(prefers-color-scheme: dark)').matches);

  // Sem tema forçado, quem manda é o navegador — e continua mandando se o
  // usuário mudar o tema do SISTEMA com a aba aberta (troca em tempo real).
  useEffect(() => {
    const raiz = document.documentElement;
    if (temaForcado) {
      raiz.setAttribute('data-theme', temaForcado);
      setTemaEscuro(temaForcado === 'dark');
      return;
    }
    raiz.removeAttribute('data-theme');
    const consulta = window.matchMedia('(prefers-color-scheme: dark)');
    setTemaEscuro(consulta.matches);
    const ouvirMudanca = (evento: MediaQueryListEvent) => setTemaEscuro(evento.matches);
    consulta.addEventListener('change', ouvirMudanca);
    return () => consulta.removeEventListener('change', ouvirMudanca);
  }, [temaForcado]);

  function alternarTema() {
    const proximo: Tema = temaEscuro ? 'light' : 'dark';
    setTemaForcado(proximo);
    salvarTemaForcado(proximo);
  }

  const podeEscrever = usuario.papel !== 'consulta';

  async function carregar(manterSelecao = true) {
    setCarregando(true);
    setErro('');
    try {
      const [resposta, fila] = await Promise.all([listarAtivos(), listarValidacoes()]);
      setAtivos(resposta.ativos);
      setValidacoes(fila.validacoes);
      if (manterSelecao && selecionado) {
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
      setFicha(visao);
      setPreCheck(avaliacao);
      setValidacoes(fila.validacoes);
      return visao;
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível carregar o detalhe.');
      return null;
    }
  }

  useEffect(() => {
    void carregar(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function trocarUsuario(novo: UsuarioDeTeste) {
    definirUsuarioAtual(novo);
    salvarUsuario(novo);
    setUsuario(novo);
    setMenuUsuarioAberto(false);
    setSelecionado(null);
    setFicha(null);
    setPreCheck(null);
    setModalFichaAberto(false);
    void carregar(false);
  }

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
        await atribuirResponsavel(selecionado.id, papelDeOwnershipSugerido(usuario.papel));
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

  function abrirFichaCompleta() {
    if (!ficha) return;
    setEdicao({ nome: ficha.nome, descricao: ficha.descricao, criticidade: ficha.criticidade, atributos: { ...ficha.atributos } });
    setAbaFicha('detalhes');
    setModalFichaAberto(true);
  }

  async function salvarEdicaoFicha(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!ficha) return;
    setSalvandoEdicao(true);
    setErro('');
    try {
      await editarAtivo(ficha.id, edicao);
      await carregar();
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível salvar as alterações.');
    } finally {
      setSalvandoEdicao(false);
    }
  }

  async function salvarEvidencia(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!ficha || !novaEvidencia.titulo.trim()) return;
    setSalvandoEvidencia(true);
    setErro('');
    try {
      await anexarEvidencia(ficha.id, { tipo: novaEvidencia.tipo, titulo: novaEvidencia.titulo, url: novaEvidencia.url || undefined });
      setNovaEvidencia({ tipo: 'documento', titulo: '', url: '' });
      await carregar();
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível anexar a evidência.');
    } finally {
      setSalvandoEvidencia(false);
    }
  }

  async function salvarRelacao(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!ficha || !novaRelacao.destinoId) return;
    setSalvandoRelacao(true);
    setErro('');
    try {
      await relacionarAtivos(ficha.id, novaRelacao.destinoId, novaRelacao.tipo);
      setNovaRelacao({ destinoId: '', tipo: tiposDeRelacao[0] });
      await carregar();
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível criar a relação.');
    } finally {
      setSalvandoRelacao(false);
    }
  }

  const empresaAtual = empresaDe(usuario);
  const unidadeAtual = unidadeDe(usuario);
  const outrosAtivosParaRelacionar = ativos.filter((a) => a.id !== ficha?.id);

  return (
    <main className={`shell screen-${tela}`}>
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">J</span><span>Jornada <b>DDD</b></span>
          <button
            className="theme-toggle"
            onClick={alternarTema}
            aria-label={temaEscuro ? 'Usar tema claro' : 'Usar tema escuro'}
            title={temaEscuro ? 'Usar tema claro' : 'Usar tema escuro'}
          >
            {temaEscuro ? <Sun size={15} /> : <Moon size={15} />}
          </button>
        </div>

        <button className="tenant" onClick={() => setMenuUsuarioAberto((v) => !v)}>
          <span className="tenant-dot" /> {empresaAtual.nome} · {unidadeAtual.nome} <ChevronRight size={15} />
        </button>

        <nav className="nav" aria-label="Navegação principal">
          <button className={tela === 'visao' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('visao')}><LayoutGrid size={18} /> Visão geral</button>
          <button className={tela === 'catalogo' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('catalogo')}><Database size={18} /> Catálogo <span className="nav-count">{ativos.length}</span></button>
          <button className={tela === 'governanca' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('governanca')}><ShieldCheck size={18} /> Governança <span className="nav-count">{validacoes.filter((v) => v.status === 'aberta').length}</span></button>
          <button className={tela === 'indicadores' ? 'nav-item active' : 'nav-item'} onClick={() => setTela('indicadores')}><Activity size={18} /> Indicadores</button>
        </nav>

        {menuUsuarioAberto && (
          <div className="switch-backdrop" onClick={() => setMenuUsuarioAberto(false)}>
            <div className="switch-panel" role="menu" onClick={(e) => e.stopPropagation()}>
              {(['admin', 'curador', 'arquiteto', 'techlead', 'negocio', 'consulta'] as const).map((papel) => (
                <div key={papel}>
                  <div className="switch-group">{usuariosDeTeste.find((u) => u.papel === papel)?.rotuloPapel ?? papel}</div>
                  {usuariosDeTeste.filter((u) => u.papel === papel).map((candidato) => (
                    <button
                      key={candidato.id}
                      className={candidato.id === usuario.id ? 'switch-user active' : 'switch-user'}
                      role="menuitemradio"
                      aria-checked={candidato.id === usuario.id}
                      onClick={() => trocarUsuario(candidato)}
                    >
                      <span className="avatar">{iniciaisDe(candidato.nome)}</span>
                      <span>
                        <strong>{candidato.nome}</strong>
                        <small>{empresaDe(candidato).nome} · {unidadeDe(candidato).nome}</small>
                      </span>
                      {candidato.id === usuario.id && <Check size={14} className="switch-check" />}
                    </button>
                  ))}
                </div>
              ))}
            </div>
          </div>
        )}

        <button className="sidebar-footer" onClick={() => setMenuUsuarioAberto((v) => !v)}>
          <span className="avatar">{iniciaisDe(usuario.nome)}</span>
          <div><strong>{usuario.nome}</strong><small>{usuario.rotuloPapel}</small></div>
          <ChevronRight size={15} />
        </button>
      </aside>

      <section className="content">
        <header className="topbar"><div><span className="eyebrow">DATA PLANE / CATÁLOGO</span><h1>{tela === 'visao' ? 'Visão geral' : tela === 'catalogo' ? 'Catálogo' : tela === 'governanca' ? 'Governança' : 'Indicadores'}</h1></div><div className="topbar-actions"><button className="outline-button" onClick={() => setCartilhaAberta(true)}><BookOpen size={16} /> Cartilha</button><button className="outline-button" onClick={() => void carregar()}><RefreshCw size={16} /> Atualizar</button></div></header>
        <div className="intro"><div><p className="kicker">{empresaAtual.nome.toUpperCase()} · LOGADO COMO {usuario.nome.toUpperCase()}</p><h2>{tela === 'visao' ? 'O catálogo está em movimento.' : tela === 'catalogo' ? 'A estrutura do negócio, visível.' : tela === 'governanca' ? 'Decisões com rastreabilidade.' : 'Sinais para orientar a próxima decisão.'}</h2><p className="muted">{tela === 'catalogo' ? 'Explore os quatro níveis da arquitetura DDD e seus ativos governados.' : tela === 'governanca' ? 'Acompanhe validações abertas, responsáveis e decisões pendentes.' : tela === 'indicadores' ? 'Uma leitura operacional da qualidade e do ciclo de vida do catálogo.' : 'Acompanhe a saúde dos ativos e mantenha cada decisão rastreável.'}</p></div>{tela !== 'governanca' && podeEscrever && <button className="primary-button" onClick={() => setModalAberto(true)}><FilePlus2 size={17} /> Cadastrar ativo</button>}</div>

        {erro && <div className="error-banner"><CircleAlert size={18} /> {erro}<button onClick={() => setErro('')} aria-label="Fechar erro"><X size={16} /></button></div>}

        {tela === 'catalogo' && <CatalogoScreen ativos={ativos} onSelecionar={(ativo) => void selecionar(ativo)} onCadastrar={() => setModalAberto(true)} podeEscrever={podeEscrever} />}
        {tela === 'governanca' && <GovernancaScreen validacoes={validacoes} onDecidir={decidir} acao={acao} podeDecidir={podeEscrever} />}
        {tela === 'indicadores' && <IndicadoresScreen ativos={ativos} validacoes={validacoes} />}

        <div className="metrics"><Metric label="Ativos no catálogo" value={ativos.length} accent="ink" note="total registrado" /><Metric label="Publicados" value={aprovados} accent="mint" note="prontos para consumo" /><Metric label="Em avaliação" value={pendentes} accent="amber" note="pedem atenção" /><Metric label="Cobertura de dados" value={ativos.length ? '78%' : '—'} accent="coral" note="completude média" /></div>

        <div className="workspace-grid">
          <section className="panel catalog-panel"><div className="panel-heading"><div><span className="panel-label">INVENTÁRIO</span><h3>Ativos recentes</h3></div><span className="result-count">{ativosFiltrados.length} resultados</span></div><div className="toolbar"><label className="search"><Search size={17} /><input placeholder="Buscar por nome, código ou tipo" value={busca} onChange={(event) => setBusca(event.target.value)} /></label><div className="filters">{(['todos', 'dominio', 'subdominio', 'contexto', 'capacidade', 'sistema', 'api', 'processo'] as Filtro[]).map((opcao) => <button key={opcao} className={filtro === opcao ? 'filter active' : 'filter'} onClick={() => setFiltro(opcao)}>{opcao === 'todos' ? 'Todos' : opcao}</button>)}</div></div><div className="asset-list">{carregando ? <div className="empty-state">Carregando catálogo...</div> : ativosFiltrados.length === 0 ? <div className="empty-state">Nenhum ativo encontrado.</div> : ativosFiltrados.map((ativo) => <button className={selecionado?.id === ativo.id ? 'asset-row selected' : 'asset-row'} key={ativo.id} onClick={() => void selecionar(ativo)}><span className={`asset-icon type-${ativo.tipo.toLowerCase()}`}>{ativo.tipo.slice(0, 1).toUpperCase()}</span><span className="asset-copy"><strong>{ativo.nome}</strong><small>{ativo.codigo} <i>•</i> {ativo.tipo}</small></span><Status status={ativo.status} /><span className={`critical critical-${ativo.criticidade.toLowerCase()}`}>{ativo.criticidade}</span><ArrowUpRight size={17} className="row-arrow" /></button>)}</div></section>
          <aside className="panel detail-panel">{selecionado ? <><div className="detail-heading"><div><span className="panel-label">ATIVO SELECIONADO</span><h3>{selecionado.nome}</h3><span className="code">{selecionado.codigo}</span></div><span className="detail-type">{selecionado.tipo}</span></div><div className="score-block"><div><span className="panel-label">PRE-CHECK</span><strong>{preCheck?.score ?? '—'}<small>/100</small></strong></div><div className={preCheck?.aprovado ? 'score-ring good' : 'score-ring'}>{preCheck?.score ?? '—'}</div></div><div className="dimension-list">{Object.entries(preCheck?.dimensoes ?? {}).map(([nome, valor]) => <div className="dimension" key={nome}><span>{nome}</span><div className="bar"><i style={{ width: `${valor}%` }} /></div><b>{valor}</b></div>)}</div><div className="detail-section"><span className="panel-label">CAMINHO DE PUBLICAÇÃO</span>{preCheck?.caminho?.slice(0, 3).map((passo) => <div className="step" key={passo.passo}><span className={passo.concluido ? 'step-mark done' : 'step-mark'}>{passo.concluido && <Check size={13} />}</span><span>{passo.rotulo}</span><small>{passo.concluido ? 'concluído' : 'pendente'}</small></div>)}</div>{preCheck && !preCheck.aprovado && <div className="notice"><CircleAlert size={17} /><div><strong>Há pontos para revisar</strong><p>{preCheck.bloqueios[0]?.mensagem ?? 'Complete os dados antes de submeter.'}</p></div></div>}{podeEscrever && <div className="detail-actions"><button className="outline-button" disabled={acao !== null} onClick={() => void executarAcao('owner')}>{acao === 'owner' ? 'Assumindo...' : 'Assumir como owner'}</button><button className="primary-button" disabled={acao !== null || !preCheck?.aprovado} onClick={() => void executarAcao('submeter')}>{acao === 'submeter' ? 'Enviando...' : 'Submeter'}</button></div>}<button className="text-button" onClick={abrirFichaCompleta}>Abrir ficha completa <ArrowUpRight size={15} /></button></> : <div className="empty-detail"><span className="detail-placeholder"><LayoutGrid size={22} /></span><h3>Selecione um ativo</h3><p>O pre-check e o caminho de publicação aparecem aqui.</p></div>}</aside>
        </div>
      </section>

      {modalAberto && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && setModalAberto(false)}><form className="modal" onSubmit={salvar}><div className="modal-header"><div><span className="panel-label">ESTRUTURA DDD</span><h3>Cadastrar item</h3></div><button type="button" className="icon-button" onClick={() => setModalAberto(false)} aria-label="Fechar"><X size={19} /></button></div><label>Tipo<select value={formulario.tipo} onChange={(event) => alterarTipo(event.target.value)}>{tiposEstrutura.map((tipo) => <option key={tipo.chave} value={tipo.chave}>{tipo.rotulo}</option>)}</select></label>{tipoFormulario?.pai && <label>{tiposEstrutura.find((tipo) => tipo.chave === tipoFormulario.pai)?.rotulo}<select required value={formulario.paiId} onChange={(event) => setFormulario({ ...formulario, paiId: event.target.value })}><option value="">Selecione o item pai</option>{paisDisponiveis.map((pai) => <option key={pai.id} value={pai.id}>{pai.codigo} · {pai.nome}</option>)}</select>{paisDisponiveis.length === 0 && <small className="field-hint">Cadastre primeiro um {tipoFormulario.pai} para continuar.</small>}</label>}<label>Nome<input required value={formulario.nome} onChange={(event) => setFormulario({ ...formulario, nome: event.target.value })} placeholder={tipoFormulario?.rotulo ?? 'Nome do ativo'} /></label><div className="form-grid"><label>Criticidade<select value={formulario.criticidade} onChange={(event) => setFormulario({ ...formulario, criticidade: event.target.value })}><option>baixa</option><option>media</option><option>alta</option><option>critica</option></select></label><label>Estado inicial<input value="Rascunho" disabled /></label></div>{camposFormulario.map((campo) => <label key={campo.nome}>{campo.rotulo}{campo.opcoes ? <select required={campo.obrigatorio} value={formulario.atributos[campo.nome] ?? ''} onChange={(event) => setFormulario({ ...formulario, atributos: { ...formulario.atributos, [campo.nome]: event.target.value } })}><option value="">Selecione</option>{campo.opcoes.map((opcao) => <option key={opcao}>{opcao}</option>)}</select> : <textarea required={campo.obrigatorio} rows={3} value={formulario.atributos[campo.nome] ?? ''} onChange={(event) => setFormulario({ ...formulario, atributos: { ...formulario.atributos, [campo.nome]: event.target.value } })} />}</label>)}<label>Descrição<textarea required rows={3} value={formulario.descricao} onChange={(event) => setFormulario({ ...formulario, descricao: event.target.value })} placeholder="Qual o papel deste item na empresa?" /></label><div className="modal-actions"><button type="button" className="outline-button" onClick={() => setModalAberto(false)}>Cancelar</button><button className="primary-button" disabled={salvando || (!!tipoFormulario?.pai && !formulario.paiId) || paisDisponiveis.length === 0 && !!tipoFormulario?.pai}>{salvando ? 'Cadastrando...' : 'Cadastrar item'}</button></div></form></div>}

      {modalFichaAberto && ficha && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && setModalFichaAberto(false)}>
          <div className="modal modal-wide">
            <div className="modal-header">
              <div><span className="panel-label">{ficha.codigo} · {ficha.tipoItem}</span><h3>{ficha.nome}</h3></div>
              <button type="button" className="icon-button" onClick={() => setModalFichaAberto(false)} aria-label="Fechar"><X size={19} /></button>
            </div>

            <div className="ficha-tabs">
              <button className={abaFicha === 'detalhes' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAbaFicha('detalhes')}>Detalhes</button>
              <button className={abaFicha === 'evidencias' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAbaFicha('evidencias')}>Evidências <span className="nav-count">{ficha.evidencias.length}</span></button>
              <button className={abaFicha === 'relacoes' ? 'ficha-tab active' : 'ficha-tab'} onClick={() => setAbaFicha('relacoes')}>Relações <span className="nav-count">{ficha.relacoes.length}</span></button>
            </div>

            <div className="ficha-body">
              {abaFicha === 'detalhes' && (podeEscrever ? (
                <form onSubmit={salvarEdicaoFicha}>
                  <label>Nome<input required value={edicao.nome} onChange={(e) => setEdicao({ ...edicao, nome: e.target.value })} /></label>
                  <div className="form-grid">
                    <label>Criticidade<select value={edicao.criticidade} onChange={(e) => setEdicao({ ...edicao, criticidade: e.target.value })}><option>baixa</option><option>media</option><option>alta</option><option>critica</option></select></label>
                    <label>Status atual<input value={ficha.status} disabled /></label>
                  </div>
                  <label>Descrição<textarea rows={3} required value={edicao.descricao} onChange={(e) => setEdicao({ ...edicao, descricao: e.target.value })} /></label>
                  {Object.keys(edicao.atributos).length > 0 && (
                    <label>Atributos
                      {Object.entries(edicao.atributos).map(([chave, valor]) => (
                        <div className="form-grid" key={chave} style={{ marginTop: 6 }}>
                          <input value={chave} disabled />
                          <input value={valor} onChange={(e) => setEdicao({ ...edicao, atributos: { ...edicao.atributos, [chave]: e.target.value } })} />
                        </div>
                      ))}
                    </label>
                  )}
                  <div className="detail-section">
                    <span className="panel-label">RESPONSÁVEIS</span>
                    {ficha.responsaveis.length === 0
                      ? <p className="muted" style={{ marginTop: 8 }}>Nenhum responsável atribuído ainda.</p>
                      : <div className="ficha-list" style={{ marginTop: 10 }}>{ficha.responsaveis.map((r, i) => (
                          <div className="ficha-row" key={`${r.idPessoa}-${i}`}>
                            <div><strong>{r.papel}</strong><small>desde {r.inicioVigencia}{r.fimVigencia ? ` até ${r.fimVigencia}` : ''}</small></div>
                            <span className={r.vigente ? 'status status-good' : 'status'}><i /> {r.vigente ? 'vigente' : 'encerrado'}</span>
                          </div>
                        ))}</div>}
                  </div>
                  <div className="modal-actions"><button className="primary-button" disabled={salvandoEdicao}>{salvandoEdicao ? 'Salvando...' : 'Salvar alterações'}</button></div>
                </form>
              ) : <p className="readonly-note">Seu papel (consulta) só permite leitura. Nome: {ficha.nome} · Criticidade: {ficha.criticidade} · {ficha.descricao}</p>)}

              {abaFicha === 'evidencias' && (
                <>
                  {ficha.evidencias.length === 0
                    ? <div className="ficha-empty">Nenhuma evidência anexada.</div>
                    : <div className="ficha-list">{ficha.evidencias.map((ev) => (
                        <div className="ficha-row" key={ev.id}>
                          <Paperclip size={15} />
                          <div><strong>{ev.titulo}</strong>{ev.url && <small><a href={ev.url} target="_blank" rel="noreferrer">{ev.url}</a></small>}</div>
                          <span className="tag">{ev.tipo}</span>
                        </div>
                      ))}</div>}
                  {podeEscrever && (
                    <form onSubmit={salvarEvidencia} className="ficha-form">
                      <label>Título<input required value={novaEvidencia.titulo} onChange={(e) => setNovaEvidencia({ ...novaEvidencia, titulo: e.target.value })} placeholder="Ex.: ADR-0012" /></label>
                      <label>Tipo<select value={novaEvidencia.tipo} onChange={(e) => setNovaEvidencia({ ...novaEvidencia, tipo: e.target.value })}><option value="documento">Documento</option><option value="adr">ADR</option><option value="openapi">OpenAPI</option><option value="repositorio">Repositório</option><option value="link">Link</option></select></label>
                      <button className="primary-button" disabled={salvandoEvidencia}><Plus size={15} /></button>
                      <label style={{ gridColumn: '1 / -1' }}>URL (opcional)<input value={novaEvidencia.url} onChange={(e) => setNovaEvidencia({ ...novaEvidencia, url: e.target.value })} placeholder="https://..." /></label>
                    </form>
                  )}
                </>
              )}

              {abaFicha === 'relacoes' && (
                <>
                  {ficha.relacoes.length === 0
                    ? <div className="ficha-empty">Nenhuma relação registrada.</div>
                    : <div className="ficha-list">{ficha.relacoes.map((rel, i) => (
                        <div className="ficha-row" key={`${rel.ativoId}-${i}`}>
                          <Link2 size={15} />
                          <div><strong>{rel.direcao === 'saida' ? `${ficha.codigo} → ${rel.codigo}` : `${rel.codigo} → ${ficha.codigo}`}</strong><small>{rel.nome}</small></div>
                          <span className="tag">{rel.tipo.replace('_', ' ')}</span>
                        </div>
                      ))}</div>}
                  {podeEscrever && (
                    <form onSubmit={salvarRelacao} className="ficha-form">
                      <label>Ativo de destino<select required value={novaRelacao.destinoId} onChange={(e) => setNovaRelacao({ ...novaRelacao, destinoId: e.target.value })}><option value="">Selecione</option>{outrosAtivosParaRelacionar.map((a) => <option key={a.id} value={a.id}>{a.codigo} · {a.nome}</option>)}</select></label>
                      <label>Tipo de relação<select value={novaRelacao.tipo} onChange={(e) => setNovaRelacao({ ...novaRelacao, tipo: e.target.value })}>{tiposDeRelacao.map((t) => <option key={t} value={t}>{t.replace('_', ' ')}</option>)}</select></label>
                      <button className="primary-button" disabled={salvandoRelacao}><Plus size={15} /></button>
                    </form>
                  )}
                </>
              )}
            </div>
          </div>
        </div>
      )}

      {cartilhaAberta && (
        <div className="modal-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && setCartilhaAberta(false)}>
          <div className="modal modal-wide">
            <div className="modal-header">
              <div><span className="panel-label">GUIA DE USO POR PAPEL</span><h3>Cartilha</h3></div>
              <button type="button" className="icon-button" onClick={() => setCartilhaAberta(false)} aria-label="Fechar"><X size={19} /></button>
            </div>
            <div className="cartilha-shell">
              <nav className="cartilha-nav" aria-label="Seções da cartilha">
                {secoesCartilha.map((secao, indice) => (
                  <button
                    key={secao.titulo}
                    className={indice === secaoCartilhaAtiva ? 'cartilha-nav-item active' : 'cartilha-nav-item'}
                    onClick={() => setSecaoCartilhaAtiva(indice)}
                  >
                    {secao.titulo}
                  </button>
                ))}
              </nav>
              <div className="cartilha-pane">
                <div className="cartilha-content" dangerouslySetInnerHTML={{ __html: secoesCartilha[secaoCartilhaAtiva]?.html ?? '' }} />
              </div>
            </div>
          </div>
        </div>
      )}
    </main>
  );
}

function CatalogoScreen({ ativos, onSelecionar, onCadastrar, podeEscrever }: { ativos: AtivoResumo[]; onSelecionar: (ativo: AtivoResumo) => void; onCadastrar: () => void; podeEscrever: boolean }) {
  const niveis = ['dominio', 'subdominio', 'contexto', 'capacidade'];
  return <section className="screen-grid catalog-screen"><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">ESTRUTURA DDD</span><h3>Mapa do catálogo</h3></div>{podeEscrever && <button className="primary-button" onClick={onCadastrar}><FilePlus2 size={16} /> Novo item</button>}</div><div className="hierarchy-grid">{niveis.map((nivel) => <div className="hierarchy-column" key={nivel}><span className="panel-label">{nivel}</span><strong>{ativos.filter((ativo) => ativo.tipo.toLowerCase() === nivel).length}</strong><small>{nivel === 'dominio' ? 'fronteiras de negócio' : nivel === 'subdominio' ? 'áreas de capacidade' : nivel === 'contexto' ? 'modelos delimitados' : 'resultados esperados'}</small></div>)}</div></div><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">INVENTÁRIO</span><h3>Todos os ativos</h3></div><span className="result-count">{ativos.length} itens</span></div><div className="compact-list">{ativos.length === 0 ? <div className="empty-state">Nenhum ativo cadastrado.</div> : ativos.map((ativo) => <button className="compact-row" key={ativo.id} onClick={() => onSelecionar(ativo)}><span className="asset-icon">{ativo.tipo.slice(0, 1).toUpperCase()}</span><span><strong>{ativo.nome}</strong><small>{ativo.codigo} · {ativo.tipo}</small></span><Status status={ativo.status} /><ArrowUpRight size={16} /></button>)}</div></div></section>;
}

function GovernancaScreen({ validacoes, onDecidir, acao, podeDecidir }: { validacoes: Validacao[]; onDecidir: (id: string, aprovada: boolean) => void; acao: 'owner' | 'submeter' | null; podeDecidir: boolean }) {
  const abertas = validacoes.filter((validacao) => validacao.status === 'aberta');
  return <section className="screen-grid governance-screen"><div className="panel screen-panel governance-summary"><span className="panel-label">FILA DE GOVERNANÇA</span><strong>{abertas.length}</strong><p>validações aguardando decisão</p><div className="governance-bars"><span style={{ width: `${validacoes.length ? (abertas.length / validacoes.length) * 100 : 0}%` }} /></div><small>{validacoes.length - abertas.length} concluídas</small></div><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">REVISÕES</span><h3>Decisões pendentes</h3></div><span className="result-count">{validacoes.length} total</span></div><div className="compact-list">{validacoes.length === 0 ? <div className="empty-state">Submeta um ativo para abrir uma validação.</div> : validacoes.map((validacao) => <div className="validation-row" key={validacao.id}><span className={validacao.status === 'aberta' ? 'step-mark' : 'step-mark done'}>{validacao.status === 'aberta' ? '!' : <Check size={13} />}</span><span><strong>{validacao.etapa}</strong><small>revisão {validacao.revisao} · {validacao.status}</small></span>{validacao.status === 'aberta' ? (podeDecidir ? <span className="validation-buttons"><button className="text-button" disabled={acao !== null} onClick={() => onDecidir(validacao.id, false)}>Ajustes</button><button className="text-button" disabled={acao !== null} onClick={() => onDecidir(validacao.id, true)}>Aprovar</button></span> : <span className="status">aguardando</span>) : <span className="status status-good"><i /> concluída</span>}</div>)}</div></div></section>;
}

function IndicadoresScreen({ ativos, validacoes }: { ativos: AtivoResumo[]; validacoes: Validacao[] }) {
  const tipos = [...new Set(ativos.map((ativo) => ativo.tipo))];
  const publicados = ativos.filter((ativo) => ativo.status.toLowerCase().includes('public')).length;
  return <section className="screen-grid indicator-screen"><div className="indicator-cards"><Metric label="Ativos totais" value={ativos.length} accent="ink" note="na empresa atual" /><Metric label="Taxa publicados" value={ativos.length ? `${Math.round((publicados / ativos.length) * 100)}%` : '0%'} accent="mint" note={`${publicados} publicados`} /><Metric label="Validações" value={validacoes.length} accent="amber" note={`${validacoes.filter((v) => v.status === 'aberta').length} abertas`} /></div><div className="panel screen-panel"><div className="panel-heading"><div><span className="panel-label">DISTRIBUIÇÃO</span><h3>Composição do catálogo</h3></div><span className="result-count">{tipos.length} tipos</span></div><div className="indicator-list">{tipos.map((tipo) => { const total = ativos.filter((ativo) => ativo.tipo === tipo).length; return <div className="indicator-line" key={tipo}><span>{tipo}</span><div className="bar"><i style={{ width: `${ativos.length ? (total / ativos.length) * 100 : 0}%` }} /></div><b>{total}</b></div>; })}</div></div></section>;
}

function Metric({ label, value, note, accent }: { label: string; value: string | number; note: string; accent: string }) { return <div className={`metric metric-${accent}`}><span>{label}</span><strong>{value}</strong><small>{note}</small></div>; }
function Status({ status }: { status: string }) { const publicado = status.toLowerCase().includes('public'); return <span className={publicado ? 'status status-good' : 'status'}><i />{status}</span>; }

export default App;
