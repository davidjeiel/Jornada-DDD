import { useEffect, useMemo, useState } from 'react';
import { ArrowUpRight, Check, CircleAlert, FilePlus2, LayoutGrid, Search, X } from 'lucide-react';
import {
  abrirRevisao, atribuirResponsavel, avaliarPreCheck, submeterAtivo,
  type AtivoResumo, type PreCheck,
} from '../api';
import { Cabecalho, Criticidade, Status } from '../componentes';
import { Link, navegar } from '../roteador';

const FILTROS = ['todos', 'dominio', 'subdominio', 'contexto', 'capacidade', 'sistema', 'aplicacao', 'api', 'evento'] as const;
type Filtro = (typeof FILTROS)[number];

const NIVEIS = [
  { tipo: 'dominio', nota: 'fronteiras de negócio' },
  { tipo: 'subdominio', nota: 'áreas de capacidade' },
  { tipo: 'contexto', nota: 'modelos delimitados' },
  { tipo: 'capacidade', nota: 'resultados esperados' },
];

// Papel concedido → papel de OWNERSHIP mais provável para quem assume o
// ativo. É só uma sugestão de UX; o servidor decide de verdade (ADR-0007).
function papelDeOwnershipSugerido(papeis: string[]): string {
  if (papeis.includes('negocio')) return 'OwnerNegocial';
  if (papeis.includes('arquiteto')) return 'Arquiteto';
  return 'OwnerTecnico';
}

type Props = {
  ativos: AtivoResumo[];
  carregando: boolean;
  termo: string;
  idSelecionado: string | null;
  /** Chaves dos papéis concedidos que valem agora (GET /v1/perfil). */
  papeis: string[];
  idPessoa: string | null;
  podeEscrever: boolean;
  onAlterado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

export function Catalogo({ ativos, carregando, termo, idSelecionado, papeis, idPessoa, podeEscrever, onAlterado, onErro }: Props) {
  const [filtro, setFiltro] = useState<Filtro>('todos');
  const [preCheck, setPreCheck] = useState<PreCheck | null>(null);
  const [acao, setAcao] = useState<'owner' | 'submeter' | 'revisao' | null>(null);

  const selecionado = ativos.find((ativo) => ativo.id === idSelecionado) ?? null;

  async function carregarDetalhe(id: string) {
    try {
      setPreCheck(await avaliarPreCheck(id));
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível carregar o detalhe.');
    }
  }

  // A seleção mora na URL (?ativo=): link direto e o botão voltar funcionam.
  useEffect(() => {
    setPreCheck(null);
    if (idSelecionado) void carregarDetalhe(idSelecionado);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idSelecionado]);

  function urlCom(mudancas: { termo?: string; ativo?: string | null }) {
    const busca = new URLSearchParams();
    const novoTermo = mudancas.termo ?? termo;
    const novoAtivo = mudancas.ativo === undefined ? idSelecionado : mudancas.ativo;
    if (novoTermo) busca.set('termo', novoTermo);
    if (novoAtivo) busca.set('ativo', novoAtivo);
    const texto = busca.toString();
    return `/catalogo${texto ? `?${texto}` : ''}`;
  }

  const filtrados = useMemo(() => ativos.filter((ativo) => {
    const correspondeBusca = `${ativo.nome} ${ativo.codigo} ${ativo.tipo}`.toLowerCase().includes(termo.toLowerCase());
    const correspondeFiltro = filtro === 'todos' || ativo.tipo.toLowerCase() === filtro;
    return correspondeBusca && correspondeFiltro;
  }), [ativos, termo, filtro]);

  async function recarregarTudo() {
    await onAlterado();
    if (idSelecionado) await carregarDetalhe(idSelecionado);
  }

  async function executarAcao(tipo: 'owner' | 'submeter' | 'revisao') {
    if (!selecionado) return;
    setAcao(tipo);
    onErro('');
    try {
      if (tipo === 'owner' && idPessoa) await atribuirResponsavel(selecionado.id, idPessoa, papelDeOwnershipSugerido(papeis));
      else if (tipo === 'revisao') await abrirRevisao(selecionado.id);
      else await submeterAtivo(selecionado.id, 'Enviado pelo catálogo');
      await recarregarTudo();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível concluir a ação.');
    } finally {
      setAcao(null);
    }
  }

  return (
    <>
      <Cabecalho
        rotulo="INVENTÁRIO"
        titulo="Catálogo de ativos"
        texto="A estrutura do negócio e os ativos técnicos que a sustentam, com o caminho de cada um até a publicação."
        acoes={podeEscrever && <Link className="primary-button" href="/novo"><FilePlus2 size={17} aria-hidden="true" /> Novo ativo</Link>}
      />

      <section className="panel screen-panel" aria-labelledby="mapa-titulo">
        <div className="panel-heading"><div><span className="panel-label">ESTRUTURA DDD</span><h2 id="mapa-titulo">Os quatro níveis</h2></div></div>
        <div className="hierarchy-grid">
          {NIVEIS.map((nivel) => (
            <button type="button" className={filtro === nivel.tipo ? 'hierarchy-column active' : 'hierarchy-column'} key={nivel.tipo} onClick={() => setFiltro(filtro === nivel.tipo ? 'todos' : nivel.tipo as Filtro)}>
              <span className="panel-label">{nivel.tipo}</span>
              <strong>{ativos.filter((ativo) => ativo.tipo.toLowerCase() === nivel.tipo).length}</strong>
              <small>{nivel.nota}</small>
            </button>
          ))}
        </div>
      </section>

      <div className="workspace-grid">
        <section className="panel catalog-panel" aria-labelledby="lista-titulo">
          <div className="panel-heading">
            <div><span className="panel-label">ATIVOS</span><h2 id="lista-titulo">Todos os ativos</h2></div>
            <span className="result-count">{filtrados.length} de {ativos.length}</span>
          </div>
          <div className="toolbar">
            {termo
              ? <span className="chip"><Search size={13} aria-hidden="true" /> “{termo}” <Link href={urlCom({ termo: '' })} aria-label={`Remover a busca por ${termo}`}><X size={13} /></Link></span>
              : <span className="muted toolbar-dica">Use a busca no topo (atalho <kbd>/</kbd>) para filtrar por nome, código ou tipo.</span>}
            <div className="filters" role="group" aria-label="Filtrar por tipo">
              {FILTROS.map((opcao) => <button type="button" key={opcao} aria-pressed={filtro === opcao} className={filtro === opcao ? 'filter active' : 'filter'} onClick={() => setFiltro(opcao)}>{opcao === 'todos' ? 'Todos' : opcao}</button>)}
            </div>
          </div>
          <div className="asset-list">
            {carregando ? <div className="empty-state">Carregando catálogo...</div>
              : filtrados.length === 0 ? <div className="empty-state">Nenhum ativo encontrado.</div>
                : filtrados.map((ativo) => (
                  <Link className={selecionado?.id === ativo.id ? 'asset-row selected' : 'asset-row'} key={ativo.id} href={urlCom({ ativo: ativo.id })} aria-current={selecionado?.id === ativo.id ? 'true' : undefined}>
                    <span className={`asset-icon type-${ativo.tipo.toLowerCase()}`} aria-hidden="true">{ativo.tipo.slice(0, 1).toUpperCase()}</span>
                    <span className="asset-copy"><strong>{ativo.nome}</strong><small>{ativo.codigo} <i>•</i> {ativo.tipo}</small></span>
                    <Status status={ativo.status} />
                    <Criticidade valor={ativo.criticidade} />
                    <ArrowUpRight size={17} className="row-arrow" aria-hidden="true" />
                  </Link>
                ))}
          </div>
        </section>

        <aside className="panel detail-panel" aria-label="Ativo selecionado">
          {selecionado ? (
            <>
              <div className="detail-heading">
                <div><span className="panel-label">ATIVO SELECIONADO</span><h3>{selecionado.nome}</h3><span className="code">{selecionado.codigo}</span></div>
                <span className="detail-type">{selecionado.tipo}</span>
              </div>
              <div className="score-block">
                <div><span className="panel-label">PRE-CHECK</span><strong>{preCheck?.score ?? '—'}<small>/100</small></strong></div>
                <div className={preCheck?.aprovado ? 'score-ring good' : 'score-ring'}>{preCheck?.score ?? '—'}</div>
              </div>
              <div className="dimension-list">
                {Object.entries(preCheck?.dimensoes ?? {}).map(([nome, valor]) => (
                  <div className="dimension" key={nome}><span>{nome}</span><div className="bar"><i style={{ width: `${valor}%` }} /></div><b>{valor}</b></div>
                ))}
              </div>
              <div className="detail-section">
                <span className="panel-label">CAMINHO DE PUBLICAÇÃO</span>
                {preCheck?.caminho?.map((passo) => (
                  <div className="step" key={passo.passo}>
                    <span className={passo.concluido ? 'step-mark done' : 'step-mark'}>{passo.concluido && <Check size={13} aria-hidden="true" />}</span>
                    <span>{passo.rotulo}</span><small>{passo.concluido ? 'concluído' : 'pendente'}</small>
                  </div>
                ))}
              </div>
              {preCheck && !preCheck.aprovado && (
                <div className="notice"><CircleAlert size={17} aria-hidden="true" /><div><strong>Há pontos para revisar</strong><p>{preCheck.bloqueios[0]?.mensagem ?? 'Complete os dados antes de submeter.'}</p></div></div>
              )}
              {podeEscrever && selecionado.status === 'publicado' && (
                <div className="detail-actions"><button className="primary-button" disabled={acao !== null} onClick={() => void executarAcao('revisao')}>{acao === 'revisao' ? 'Abrindo...' : 'Abrir revisão'}</button></div>
              )}
              {podeEscrever && selecionado.status !== 'publicado' && (
                <div className="detail-actions">
                  <button className="outline-button" disabled={acao !== null} onClick={() => void executarAcao('owner')}>{acao === 'owner' ? 'Assumindo...' : 'Assumir como owner'}</button>
                  <button className="primary-button" disabled={acao !== null || !preCheck?.aprovado} onClick={() => void executarAcao('submeter')}>{acao === 'submeter' ? 'Enviando...' : 'Submeter'}</button>
                </div>
              )}
              <Link className="text-button" href={`/ativos/${selecionado.id}`}>Abrir a ficha do ativo <ArrowUpRight size={15} aria-hidden="true" /></Link>
            </>
          ) : (
            <div className="empty-detail"><span className="detail-placeholder"><LayoutGrid size={22} aria-hidden="true" /></span><h3>Selecione um ativo</h3><p>O pre-check e o caminho de publicação aparecem aqui.</p></div>
          )}
        </aside>
      </div>

    </>
  );
}

/** Para quem precisa abrir um ativo no catálogo a partir de outra tela. */
export function abrirNoCatalogo(id: string) {
  navegar(`/catalogo?ativo=${id}`);
}
