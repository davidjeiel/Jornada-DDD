import { useEffect, useState, type FormEvent } from 'react';
import {
  cadastrarPessoa, concederPapel, consultarVinculos, corrigirCadastro, excluirCadastro, inativarEmLote, listarCadastros, listarSquads, revogarPapel,
  type AtivoResumo, type Cadastro, type PapelNoEscopo, type Squad,
} from '../api';
import { Cabecalho } from '../componentes';
import { PAPEIS, ROTULO_DO_PAPEL } from '../papeis';

export const POR_PAGINA = 20;

type PropsDaTabela = {
  cadastros: Cadastro[];
  pagina: number;
  idDoAdmin: string | null;
  selecionados: string[];
  aberto: string | null;
  onSelecionar: (id: string, marcado: boolean) => void;
  onAbrir: (id: string) => void;
  onPagina: (pagina: number) => void;
};

/** A lista paginada, ativos primeiro (POR_PAGINA_GESTAO do painel). O próprio admin não entra na seleção. */
export function TabelaDeCadastros({ cadastros, pagina, idDoAdmin, selecionados, aberto, onSelecionar, onAbrir, onPagina }: PropsDaTabela) {
  const paginas = Math.max(1, Math.ceil(cadastros.length / POR_PAGINA));
  const atual = Math.min(Math.max(1, pagina), paginas);
  const visiveis = cadastros.slice((atual - 1) * POR_PAGINA, atual * POR_PAGINA);

  return (
    <>
      <div className="rolagem">
        <table className="tabela">
          <thead>
            <tr><th scope="col"><span className="leitor">Selecionar</span></th><th scope="col">Nome</th><th scope="col">Matrícula</th><th scope="col">Unidade</th><th scope="col">Squad</th><th scope="col">Papéis</th><th scope="col">Situação</th><th scope="col"><span className="leitor">Ações</span></th></tr>
          </thead>
          <tbody>
            {visiveis.length === 0 && <tr><td colSpan={8} className="vazio">Nenhum cadastro encontrado.</td></tr>}
            {visiveis.map(({ pessoa, squad, papeis }) => (
              <tr key={pessoa.id} className={pessoa.id === aberto ? 'selecionada' : undefined}>
                <td>
                  {pessoa.id !== idDoAdmin && pessoa.ativa && (
                    <input type="checkbox" aria-label={`Selecionar ${pessoa.nome}`} checked={selecionados.includes(pessoa.id)}
                      onChange={(e) => onSelecionar(pessoa.id, e.target.checked)} />
                  )}
                </td>
                <td>{pessoa.nome}{pessoa.id === idDoAdmin && <span className="discreto"> (você)</span>}</td>
                <td className="mono">{pessoa.matricula}</td>
                <td className="mono">{pessoa.unidade}</td>
                <td>{squad?.sigla ?? '—'}</td>
                <td>{papeis.map((p) => `${p.rotulo}${p.escopoTipo === 'empresa' ? '' : ` (${p.escopoRotulo})`}`).join(', ') || <span className="discreto">sem papel</span>}</td>
                <td><span className={`status ${pessoa.ativa ? 'status-good' : 'status-arquivado'}`}>{pessoa.ativa ? 'ativo' : 'inativo'}</span></td>
                <td><button type="button" className="text-button" onClick={() => onAbrir(pessoa.id)}>Detalhes</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {paginas > 1 && (
        <nav className="paginacao" aria-label="Páginas">
          <button type="button" className="outline-button" disabled={atual <= 1} onClick={() => onPagina(atual - 1)}>Anterior</button>
          <span>Página {atual} de {paginas} · {cadastros.length} cadastros</span>
          <button type="button" className="outline-button" disabled={atual >= paginas} onClick={() => onPagina(atual + 1)}>Próxima</button>
        </nav>
      )}
    </>
  );
}

type PropsDoPainel = {
  cadastro: Cadastro;
  squads: Squad[];
  dominios: AtivoResumo[];
  vinculos: string[] | null;
  ehVoce: boolean;
  ocupado: boolean;
  onCorrigir: (dados: { nome: string; email: string; unidade: string; idSquad: string | null; ativa: boolean }) => void;
  onConceder: (papel: PapelNoEscopo) => void;
  onRevogar: (papel: PapelNoEscopo) => void;
  onExcluir: () => void;
};

/** O detalhe de um cadastro: correção, papéis (conceder e revogar) e exclusão, quando não há vínculo. */
export function PainelDoCadastro({ cadastro, squads, dominios, vinculos, ehVoce, ocupado, onCorrigir, onConceder, onRevogar, onExcluir }: PropsDoPainel) {
  const { pessoa, papeis } = cadastro;

  function corrigir(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    onCorrigir({
      nome: d.get('nome')?.toString() ?? '', email: d.get('email')?.toString() ?? '', unidade: d.get('unidade')?.toString() ?? '',
      idSquad: d.get('idSquad')?.toString() || null, ativa: d.get('ativa') === 'on',
    });
  }

  function conceder(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    const escopoTipo = d.get('escopoTipo')?.toString() ?? 'empresa';
    onConceder({
      papel: d.get('papel')?.toString() ?? '', escopoTipo,
      escopoId: escopoTipo === 'dominio' ? d.get('escopoId')?.toString() || null : escopoTipo === 'squad' ? d.get('idSquadDoEscopo')?.toString() || null : null,
    });
  }

  return (
    <section className="panel screen-panel" aria-labelledby="detalhe-titulo">
      <div className="panel-heading">
        <div><span className="panel-label">CADASTRO <span className="mono">{pessoa.matricula}</span></span><h2 id="detalhe-titulo">{pessoa.nome}</h2></div>
      </div>

      <form className="formulario-pagina" onSubmit={corrigir} key={`${pessoa.id}-${pessoa.ativa}`}>
        <div className="form-grid">
          <label>Nome completo<input name="nome" required defaultValue={pessoa.nome} /></label>
          <label>E-mail<input name="email" type="email" required defaultValue={pessoa.email} /></label>
          <label>Unidade<input name="unidade" required defaultValue={pessoa.unidade} /></label>
          <label>Squad
            <select name="idSquad" defaultValue={pessoa.idSquad ?? ''}>
              <option value="">sem squad</option>
              {squads.filter((s) => s.ativa || s.id === pessoa.idSquad).map((s) => <option key={s.id} value={s.id}>{s.sigla} · {s.titulo}</option>)}
            </select>
          </label>
        </div>
        <label className="caixa-de-marcar">
          <input name="ativa" type="checkbox" defaultChecked={pessoa.ativa} disabled={ehVoce} /> Cadastro ativo
          <small className="ajuda">Inativar encerra todos os papéis. Reativar não devolve nenhum.{ehVoce && ' Você não pode inativar o próprio cadastro.'}</small>
        </label>
        <div className="modal-actions"><button className="primary-button" disabled={ocupado}>Salvar cadastro</button></div>
      </form>

      <h3>Papéis vigentes</h3>
      {papeis.length === 0 ? <p className="vazio">Nenhum papel vigente.</p> : (
        <ul className="lista-de-papeis">
          {papeis.map((p) => (
            <li key={`${p.papel}-${p.escopoTipo}-${p.escopoId ?? ''}`}>
              <span><strong>{p.rotulo}</strong> · {p.escopoRotulo}</span>
              {!ehVoce && (
                <button type="button" className="text-button" disabled={ocupado}
                  onClick={() => onRevogar({ papel: p.papel, escopoTipo: p.escopoTipo, escopoId: p.escopoId })}>Revogar</button>
              )}
            </li>
          ))}
        </ul>
      )}

      {!ehVoce && pessoa.ativa && (
        <form className="decisao-pleito" onSubmit={conceder}>
          <label>Conceder papel
            <select name="papel" defaultValue="negocio">{PAPEIS.map((p) => <option key={p} value={p}>{ROTULO_DO_PAPEL[p]}</option>)}</select>
          </label>
          <label>Vale em
            <select name="escopoTipo" defaultValue="empresa"><option value="empresa">todo o catálogo</option><option value="dominio">um domínio</option><option value="squad">os ativos de um squad</option></select>
          </label>
          <label>Domínio
            <select name="escopoId" defaultValue=""><option value="">—</option>{dominios.map((d) => <option key={d.id} value={d.id}>{d.codigo} · {d.nome}</option>)}</select>
          </label>
          <label>Squad do escopo
            <select name="idSquadDoEscopo" defaultValue=""><option value="">—</option>{squads.filter((s) => s.ativa).map((s) => <option key={s.id} value={s.id}>{s.sigla} · {s.titulo}</option>)}</select>
          </label>
          <div className="modal-actions"><button className="outline-button" disabled={ocupado}>Conceder</button></div>
        </form>
      )}

      <h3>Excluir o cadastro</h3>
      {vinculos === null ? <p className="vazio">Verificando vínculos...</p>
        : vinculos.length > 0
          ? <p className="discreto">Não pode ser excluído: {vinculos.join(', ')}. Inative o cadastro em vez de excluir.</p>
          : ehVoce
            ? <p className="discreto">Você não pode excluir o próprio cadastro.</p>
            : (
              <p>Sem vínculo nenhum com o histórico.{' '}
                <button type="button" className="danger-button" disabled={ocupado} onClick={onExcluir}>Excluir cadastro</button>
              </p>
            )}
    </section>
  );
}

export type NovoCadastro = { matricula: string; nome: string; email: string; unidade: string; idSquad: string | null };

/**
 * Cadastro direto, sem pleito: para pôr como responsável quem não pediu acesso.
 * A pessoa nasce ativa e sem papel; o papel, se precisar, concede-se depois.
 */
export function FormularioDeCadastroDireto({ squads, ocupado, onCadastrar }: { squads: Squad[]; ocupado: boolean; onCadastrar: (dados: NovoCadastro) => void }) {
  function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    const texto = (k: string) => d.get(k)?.toString().trim() ?? '';
    onCadastrar({ matricula: texto('matricula'), nome: texto('nome'), email: texto('email'), unidade: texto('unidade'), idSquad: texto('idSquad') || null });
  }
  return (
    <details className="panel screen-panel cadastro-direto">
      <summary>Cadastrar pessoa</summary>
      <p className="discreto">Para quem vai responder por ativos sem ter pedido acesso. A pessoa entra ativa e sem papel: consulta o catálogo e já pode ser escolhida como responsável na ficha. Se precisar de papel, conceda abaixo, no cadastro dela.</p>
      <form className="formulario-pagina" onSubmit={enviar} aria-label="Cadastrar pessoa">
        <div className="form-grid">
          <label>Matrícula<input name="matricula" required placeholder="C123456" autoComplete="off" /></label>
          <label>Unidade<input name="unidade" required placeholder="0427" inputMode="numeric" /></label>
        </div>
        <label>Nome completo<input name="nome" required /></label>
        <label>E-mail<input name="email" type="email" required /></label>
        <label>Squad
          <select name="idSquad" defaultValue="">
            <option value="">sem squad</option>
            {squads.filter((s) => s.ativa).map((s) => <option key={s.id} value={s.id}>{s.sigla} · {s.titulo}</option>)}
          </select>
        </label>
        <div className="modal-actions"><button className="primary-button" disabled={ocupado}>Cadastrar</button></div>
      </form>
    </details>
  );
}

type Props = {
  ativos: AtivoResumo[];
  idDoAdmin: string | null;
  /** Avisa o App: a lista de pessoas (os responsáveis possíveis) mudou. */
  onPessoasAlteradas?: () => void;
  onErro: (mensagem: string) => void;
};

// A gestão de acessos do painel (/gestao-acessos), só para o admin: a API recusa
// para qualquer outro papel. Salvar o cadastro não mexe nos papéis: cada papel
// se concede e se revoga (ADR-0018, consequências).
export function GestaoDeAcessos({ ativos, idDoAdmin, onPessoasAlteradas, onErro }: Props) {
  const [cadastros, setCadastros] = useState<Cadastro[]>([]);
  const [squads, setSquads] = useState<Squad[]>([]);
  const [busca, setBusca] = useState('');
  const [pagina, setPagina] = useState(1);
  const [selecionados, setSelecionados] = useState<string[]>([]);
  const [aberto, setAberto] = useState<string | null>(null);
  const [vinculos, setVinculos] = useState<string[] | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const dominios = ativos.filter((a) => a.tipo.toLowerCase() === 'dominio');
  const cadastro = cadastros.find((c) => c.pessoa.id === aberto) ?? null;

  async function carregar(termo = busca) {
    try {
      const [lista, times] = await Promise.all([listarCadastros(termo), listarSquads()]);
      setCadastros(lista.cadastros);
      setSquads(times.squads);
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível carregar os cadastros.');
    }
  }

  useEffect(() => {
    void carregar('');
  }, []);

  useEffect(() => {
    setVinculos(null);
    if (aberto) consultarVinculos(aberto).then((v) => setVinculos(v.vinculos)).catch(() => setVinculos([]));
  }, [aberto, cadastros]);

  async function agir(acao: () => Promise<unknown>) {
    setOcupado(true);
    onErro('');
    try {
      await acao();
      await carregar();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível concluir.');
    } finally {
      setOcupado(false);
    }
  }

  function buscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setPagina(1);
    void carregar(busca);
  }

  return (
    <>
      <Cabecalho rotulo="ADMINISTRAÇÃO" titulo="Gestão de acessos"
        texto="Todos os cadastros da empresa, inclusive os inativos. Cadastre quem vai responder por ativos, corrija dados, conceda ou revogue papéis, inative quem saiu." />
      <FormularioDeCadastroDireto key={cadastros.length} squads={squads} ocupado={ocupado}
        onCadastrar={(dados) => void agir(async () => { const novo = await cadastrarPessoa(dados); setAberto(novo.pessoa.id); onPessoasAlteradas?.(); })} />
      <section className="panel screen-panel">
        <form className="barra-de-busca" role="search" onSubmit={buscar}>
          <input name="busca" type="search" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Nome, matrícula, e-mail, unidade ou squad" aria-label="Buscar cadastros" />
          <button className="outline-button">Buscar</button>
          <button type="button" className="danger-button" disabled={selecionados.length === 0 || ocupado}
            onClick={() => void agir(async () => { await inativarEmLote(selecionados); setSelecionados([]); })}>
            Inativar selecionados ({selecionados.length})
          </button>
        </form>
        <TabelaDeCadastros cadastros={cadastros} pagina={pagina} idDoAdmin={idDoAdmin} selecionados={selecionados} aberto={aberto}
          onSelecionar={(id, marcado) => setSelecionados((atual) => marcado ? [...atual, id] : atual.filter((x) => x !== id))}
          onAbrir={setAberto} onPagina={setPagina} />
      </section>
      {cadastro && (
        <PainelDoCadastro cadastro={cadastro} squads={squads} dominios={dominios} vinculos={vinculos}
          ehVoce={cadastro.pessoa.id === idDoAdmin} ocupado={ocupado}
          onCorrigir={(dados) => void agir(() => corrigirCadastro(cadastro.pessoa.id, dados))}
          onConceder={(papel) => void agir(() => concederPapel(cadastro.pessoa.id, papel))}
          onRevogar={(papel) => void agir(() => revogarPapel(cadastro.pessoa.id, papel))}
          onExcluir={() => void agir(async () => { await excluirCadastro(cadastro.pessoa.id); setAberto(null); })} />
      )}
    </>
  );
}
