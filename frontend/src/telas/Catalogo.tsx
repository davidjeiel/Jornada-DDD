import { useEffect, useMemo, useState } from 'react';
import { ArrowUpRight, Check, CircleAlert, FilePlus2, LayoutGrid, X } from 'lucide-react';
import {
  abrirRevisao, atribuirResponsavel, avaliarPreCheck, buscarNoCatalogo, submeterAtivo,
  type AtivoResumo, type Metamodelo, type PaginaDoCatalogo, type PreCheck, type Squad,
} from '../api';
import { Cabecalho, Criticidade, Status } from '../componentes';
import { ORDENACOES, chipsDosFiltros, filtrosEfetivos, urlDoCatalogo, type Chip } from '../filtrosDoCatalogo';
import { Link, navegar } from '../roteador';

const STATUS = ['rascunho', 'em_validacao', 'publicado', 'em_revisao', 'descontinuado', 'arquivado'];

// Papel concedido → papel de OWNERSHIP mais provável para quem assume o
// ativo. É só uma sugestão de UX; o servidor decide de verdade (ADR-0007).
function papelDeOwnershipSugerido(papeis: string[]): string {
  if (papeis.includes('negocio')) return 'OwnerNegocial';
  if (papeis.includes('arquiteto')) return 'Arquiteto';
  return 'OwnerTecnico';
}

type PropsDosFiltros = { filtros: URLSearchParams; metamodelo: Metamodelo | null; squads: Squad[]; onMudar: (chave: string, valor: string | null) => void };

/** Os controles do recorte. Cada um troca só o próprio filtro e volta à primeira página. */
export function FormularioDeFiltros({ filtros, metamodelo, squads, onMudar }: PropsDosFiltros) {
  const valor = (chave: string) => filtros.get(chave) ?? '';
  return (
    <form className="formulario-pagina filtros-do-catalogo" role="search" aria-label="Filtrar o catálogo" onSubmit={(e) => e.preventDefault()}>
      <label>Tipo
        <select name="tipo" value={valor('tipo')} onChange={(e) => onMudar('tipo', e.target.value || null)}>
          <option value="">todos</option>
          {(metamodelo?.tipos ?? []).map((t) => <option key={t.chave} value={t.chave}>{t.rotulo}</option>)}
        </select>
      </label>
      <label>Status
        <select name="status" value={valor('status')} onChange={(e) => onMudar('status', e.target.value || null)}>
          <option value="">todos</option>
          {STATUS.map((s) => <option key={s} value={s}>{s.replace('_', ' ')}</option>)}
        </select>
      </label>
      <label>Criticidade
        <select name="criticidade" value={valor('criticidade')} onChange={(e) => onMudar('criticidade', e.target.value || null)}>
          <option value="">todas</option><option value="baixa">baixa</option><option value="media">média</option><option value="alta">alta</option><option value="critica">crítica</option>
        </select>
      </label>
      <label>Squad
        <select name="squad" value={valor('squad')} onChange={(e) => onMudar('squad', e.target.value || null)}>
          <option value="">todas</option>
          {squads.map((s) => <option key={s.id} value={s.id}>{s.sigla}</option>)}
        </select>
      </label>
      <label>Origem
        <select name="origem" value={valor('origem')} onChange={(e) => onMudar('origem', e.target.value || null)}>
          <option value="">qualquer</option><option value="manual">cadastro manual</option><option value="automatica">descoberta automática</option>
        </select>
      </label>
      <label>Ordenar por
        <select name="ordenar" value={valor('ordenar') || 'tipo'} onChange={(e) => onMudar('ordenar', e.target.value === 'tipo' ? null : e.target.value)}>
          {ORDENACOES.map((o) => <option key={o.chave} value={o.chave}>{o.rotulo}</option>)}
        </select>
      </label>
      <label className="caixa-de-marcar"><input type="checkbox" name="desc" checked={valor('desc') === 'true'} onChange={(e) => onMudar('desc', e.target.checked ? 'true' : null)} /> ordem inversa</label>
      <label className="caixa-de-marcar"><input type="checkbox" name="semOwner" checked={valor('semOwner') === 'true'} onChange={(e) => onMudar('semOwner', e.target.checked ? 'true' : null)} /> só sem responsável</label>
    </form>
  );
}

export function ChipsDosFiltros({ chips }: { chips: Chip[] }) {
  if (chips.length === 0) return null;
  return (
    <nav className="chips" aria-label="Filtros ativos">
      {chips.map((c) => (
        <span className="chip" key={c.chave}>{c.rotulo}: {c.valor}
          <Link href={c.semEle} aria-label={`Remover o filtro ${c.rotulo}`}><X size={13} /></Link>
        </span>
      ))}
      <Link className="text-button" href="/catalogo?limpar=1">Limpar filtros</Link>
    </nav>
  );
}

type PropsDaLista = { pagina: PaginaDoCatalogo; filtros: URLSearchParams; idSelecionado: string | null };

/** A página do recorte, com o total real e a navegação entre páginas (test_catalogo_mostra_total_e_paginacao). */
export function ListaDoCatalogo({ pagina, filtros, idSelecionado }: PropsDaLista) {
  const comAtivo = (id: string) => {
    const busca = new URLSearchParams(filtros);
    busca.set('pagina', String(pagina.pagina));
    busca.set('ativo', id);
    return `/catalogo?${busca.toString()}`;
  };
  return (
    <>
      <p className="resultado-do-recorte">
        <strong>{pagina.total}</strong> ativo(s){pagina.total > 0 && <> · Mostrando {pagina.primeiro}–{pagina.ultimo}</>}
        {pagina.truncado && <span className="discreto"> · o recorte passou do limite de leitura; refine os filtros</span>}
      </p>
      <div className="asset-list">
        {pagina.itens.length === 0 ? <div className="empty-state">Nenhum ativo neste recorte.</div>
          : pagina.itens.map(({ ativo, origem, temResponsavel }) => (
            <Link className={idSelecionado === ativo.id ? 'asset-row selected' : 'asset-row'} key={ativo.id} href={comAtivo(ativo.id)}
              aria-current={idSelecionado === ativo.id ? 'true' : undefined}>
              <span className={`asset-icon type-${ativo.tipo.toLowerCase()}`} aria-hidden="true">{ativo.tipo.slice(0, 1).toUpperCase()}</span>
              <span className="asset-copy">
                <strong>{ativo.nome}</strong>
                <small>{ativo.codigo} <i>•</i> {ativo.tipo}{origem === 'automatica' && ' · descoberta automática'}{!temResponsavel && ' · sem responsável'}</small>
              </span>
              <Status status={ativo.status} />
              <Criticidade valor={ativo.criticidade} />
              <ArrowUpRight size={17} className="row-arrow" aria-hidden="true" />
            </Link>
          ))}
      </div>
      {pagina.paginas > 1 && (
        <nav className="paginacao" aria-label="Páginas do catálogo">
          {pagina.pagina > 1 ? <Link className="outline-button" href={urlDoCatalogo(filtros, { pagina: String(pagina.pagina - 1) })}>Anterior</Link> : <span />}
          <span>Página {pagina.pagina} de {pagina.paginas}</span>
          {pagina.pagina < pagina.paginas ? <Link className="outline-button" href={urlDoCatalogo(filtros, { pagina: String(pagina.pagina + 1) })}>Próxima</Link> : <span />}
        </nav>
      )}
    </>
  );
}

type Props = {
  busca: URLSearchParams;
  idSelecionado: string | null;
  metamodelo: Metamodelo | null;
  squads: Squad[];
  /** Chaves dos papéis concedidos que valem agora (GET /v1/perfil). */
  papeis: string[];
  idPessoa: string | null;
  podeEscrever: boolean;
  onAlterado: () => Promise<void>;
  onErro: (mensagem: string) => void;
};

// Os filtros guardados são conveniência deste navegador: sem armazenamento, só não voltam.
const armazenamento = (() => {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
})();

export function Catalogo({ busca, idSelecionado, metamodelo, squads, papeis, idPessoa, podeEscrever, onAlterado, onErro }: Props) {
  const chaveDaBusca = busca.toString();
  const filtros = useMemo(() => filtrosEfetivos(busca, armazenamento), [chaveDaBusca]);
  const [pagina, setPagina] = useState<PaginaDoCatalogo | null>(null);
  const [preCheck, setPreCheck] = useState<PreCheck | null>(null);
  const [acao, setAcao] = useState<'owner' | 'submeter' | 'revisao' | null>(null);
  const [versao, setVersao] = useState(0);
  const rotuloDoTipo = (chave: string) => metamodelo?.tipos.find((t) => t.chave === chave)?.rotulo ?? chave;
  const chips = chipsDosFiltros(filtros, squads, rotuloDoTipo);
  const selecionado: AtivoResumo | null = pagina?.itens.find((i) => i.ativo.id === idSelecionado)?.ativo ?? null;

  useEffect(() => {
    let vigente = true;
    buscarNoCatalogo(filtros).then((p) => { if (vigente) setPagina(p); })
      .catch((e) => { if (vigente) onErro(e instanceof Error ? e.message : 'Não foi possível carregar o catálogo.'); });
    return () => { vigente = false; };
  }, [filtros.toString(), versao]);

  // A seleção mora na URL (?ativo=): link direto e o botão voltar funcionam.
  useEffect(() => {
    setPreCheck(null);
    if (idSelecionado) avaliarPreCheck(idSelecionado).then(setPreCheck).catch(() => setPreCheck(null));
  }, [idSelecionado, versao]);

  async function executarAcao(tipo: 'owner' | 'submeter' | 'revisao') {
    if (!selecionado) return;
    setAcao(tipo);
    onErro('');
    try {
      if (tipo === 'owner' && idPessoa) await atribuirResponsavel(selecionado.id, idPessoa, papelDeOwnershipSugerido(papeis));
      else if (tipo === 'revisao') await abrirRevisao(selecionado.id);
      else await submeterAtivo(selecionado.id, 'Enviado pelo catálogo');
      await onAlterado();
      setVersao((v) => v + 1);
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
        texto="A estrutura do negócio e os ativos técnicos que a sustentam. O recorte que você escolher volta na próxima visita."
        acoes={podeEscrever && <Link className="primary-button" href="/novo"><FilePlus2 size={17} aria-hidden="true" /> Novo ativo</Link>}
      />

      <section className="panel screen-panel">
        <FormularioDeFiltros filtros={filtros} metamodelo={metamodelo} squads={squads} onMudar={(chave, valor) => navegar(urlDoCatalogo(filtros, { [chave]: valor }))} />
        <ChipsDosFiltros chips={chips} />
      </section>

      <div className="workspace-grid">
        <section className="panel catalog-panel" aria-labelledby="lista-titulo">
          <div className="panel-heading"><div><span className="panel-label">ATIVOS</span><h2 id="lista-titulo">O recorte</h2></div></div>
          {pagina ? <ListaDoCatalogo pagina={pagina} filtros={filtros} idSelecionado={idSelecionado} /> : <div className="empty-state">Carregando catálogo...</div>}
        </section>

        <aside className="panel detail-panel" aria-label="Ativo selecionado">
          {selecionado ? (
            <>
              <div className="detail-heading">
                <div><span className="panel-label">ATIVO SELECIONADO</span><h3>{selecionado.nome}</h3><span className="code">{selecionado.codigo}</span></div>
                <span className="detail-type">{rotuloDoTipo(selecionado.tipo)}</span>
              </div>
              <div className="score-block">
                <div><span className="panel-label">PRE-CHECK</span><strong>{preCheck?.score ?? '—'}<small>/100</small></strong></div>
                <div className={preCheck?.aprovado ? 'score-ring good' : 'score-ring'}>{preCheck?.score ?? '—'}</div>
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
              {podeEscrever && (selecionado.status === 'rascunho' || selecionado.status === 'em_revisao') && (
                <div className="detail-actions">
                  <button className="outline-button" disabled={acao !== null} onClick={() => void executarAcao('owner')}>{acao === 'owner' ? 'Assumindo...' : 'Assumir como owner'}</button>
                  <button className="primary-button" disabled={acao !== null || !preCheck?.aprovado} onClick={() => void executarAcao('submeter')}>{acao === 'submeter' ? 'Enviando...' : 'Submeter'}</button>
                </div>
              )}
              <Link className="text-button" href={`/ativos/${selecionado.id}`}>Abrir a ficha do ativo <ArrowUpRight size={15} aria-hidden="true" /></Link>
            </>
          ) : (
            <div className="empty-detail"><span className="detail-placeholder"><LayoutGrid size={22} aria-hidden="true" /></span><h3>Selecione um ativo</h3><p>O pré-check e o caminho de publicação aparecem aqui.</p></div>
          )}
        </aside>
      </div>
    </>
  );
}
