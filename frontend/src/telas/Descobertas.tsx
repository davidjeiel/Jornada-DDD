import { useEffect, useState } from 'react';
import {
  consultarDescobertas, importarDescobertas, previaDeImportacao, triarDescobertas,
  type AtivoResumo, type Descobertas as DadosDasDescobertas, type ItemDoCatalogo, type PedidoDeDescoberta, type PlanoDeImportacao,
} from '../api';
import { Cabecalho } from '../componentes';
import { Link } from '../roteador';
import { nomeDaPessoa } from '../usuarios';

const ROTULO_DA_ACAO: Record<string, string> = { criar: 'criar', manter: 'já existe', atualizar: 'atualizar' };
const quando = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

/** A prévia (descobertas.html do painel): o que será criado e o que já existe, antes de gravar. */
export function PreviaDaImportacao({ plano, ocupado, onImportar }: { plano: PlanoDeImportacao; ocupado: boolean; onImportar: () => void }) {
  return (
    <div className="previa">
      <div className="panel-heading">
        <div>
          <span className="panel-label">PRÉVIA · NADA FOI GRAVADO</span>
          <h3>{plano.titulo}{plano.versao && <span className="discreto"> · versão {plano.versao}</span>}</h3>
          <p className="discreto">{plano.criar} a criar · {plano.manter} já existe(m)</p>
        </div>
        <button type="button" className="primary-button" disabled={ocupado || plano.criar + plano.acoes.filter((a) => a.acao === 'atualizar').length === 0} onClick={onImportar}>
          Importar {plano.criar} ativo(s)
        </button>
      </div>
      <div className="rolagem">
        <table className="tabela">
          <thead><tr><th scope="col">O que acontece</th><th scope="col">Tipo</th><th scope="col">Nome</th><th scope="col">Detalhe</th></tr></thead>
          <tbody>
            {plano.acoes.map((a) => (
              <tr key={`${a.tipo}-${a.nome}`} className={a.acao === 'criar' ? '' : 'discreto'}>
                <td><span className={`acao-do-plano acao-${a.acao}`}>{ROTULO_DA_ACAO[a.acao] ?? a.acao}</span></td>
                <td>{a.tipo}</td><td className="mono">{a.nome}</td><td>{a.detalhe}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

type PropsDaImportacao = {
  dados: DadosDasDescobertas;
  aplicacoes: AtivoResumo[];
  pedido: PedidoDeDescoberta;
  plano: PlanoDeImportacao | null;
  erro: string;
  ocupado: boolean;
  onMudar: (pedido: PedidoDeDescoberta) => void;
  onPrevia: () => void;
  onImportar: () => void;
};

/** O formulário: a fonte, a aplicação que recebe o que for descoberto e o JSON. A importação só aparece depois da prévia. */
export function FormularioDeImportacao({ dados, aplicacoes, pedido, plano, erro, ocupado, onMudar, onPrevia, onImportar }: PropsDaImportacao) {
  const fonte = dados.fontes.find((f) => f.chave === pedido.fonte) ?? dados.fontes[0];
  return (
    <section className="panel screen-panel" aria-labelledby="importar-titulo">
      <div className="panel-heading"><div><span className="panel-label">IMPORTAÇÃO</span><h2 id="importar-titulo">Importar</h2></div></div>
      <form className="formulario-pagina" onSubmit={(e) => { e.preventDefault(); onPrevia(); }}>
        <fieldset className="fontes-de-descoberta">
          <legend>Fonte</legend>
          {dados.fontes.map((f) => (
            <label key={f.chave} className="caixa-de-marcar">
              <input type="radio" name="fonte" value={f.chave} checked={pedido.fonte === f.chave} onChange={() => onMudar({ ...pedido, fonte: f.chave })} /> {f.rotulo}
            </label>
          ))}
        </fieldset>
        <label>Aplicação que recebe o que for descoberto
          <select value={pedido.idPai ?? ''} onChange={(e) => onMudar({ ...pedido, idPai: e.target.value || null })}>
            <option value="">Nenhuma (entra sem pai)</option>
            {aplicacoes.map((a) => <option key={a.id} value={a.id}>{a.codigo} · {a.nome}</option>)}
          </select>
        </label>
        <label>Conteúdo
          <textarea name="conteudo" rows={10} className="mono" value={pedido.conteudo} spellCheck={false}
            onChange={(e) => onMudar({ ...pedido, conteudo: e.target.value })} placeholder={fonte?.ajuda} />
        </label>
        {fonte && <p className="ajuda">{fonte.ajuda}</p>}
        {erro && <p className="alerta-integridade" role="alert">{erro}</p>}
        <div className="modal-actions">
          <button className="outline-button" disabled={ocupado || !pedido.conteudo.trim()}>Ver a prévia</button>
        </div>
      </form>
      {plano && <PreviaDaImportacao plano={plano} ocupado={ocupado} onImportar={onImportar} />}
    </section>
  );
}

type PropsDaBandeja = {
  itens: ItemDoCatalogo[];
  selecionados: string[];
  ocupado: boolean;
  onSelecionar: (ids: string[]) => void;
  onTriar: (acao: 'aceitar' | 'descartar') => void;
};

/** A bandeja: o que a máquina trouxe e ninguém olhou. Aceitar tira daqui; descartar arquiva. */
export function BandejaDeDescobertas({ itens, selecionados, ocupado, onSelecionar, onTriar }: PropsDaBandeja) {
  const todos = itens.length > 0 && selecionados.length === itens.length;
  return (
    <section className="panel screen-panel" aria-labelledby="bandeja-titulo">
      <div className="panel-heading">
        <div><span className="panel-label">TRIAGEM</span><h2 id="bandeja-titulo">Bandeja</h2></div>
        <span className="result-count">{itens.length} por triar</span>
      </div>
      {itens.length === 0
        ? <p className="vazio">Nada na bandeja. O que for importado aparece aqui até alguém aceitar ou descartar.</p>
        : (
          <>
            <div className="rolagem">
              <table className="tabela">
                <thead>
                  <tr>
                    <th scope="col"><input type="checkbox" aria-label="Selecionar todos" checked={todos}
                      onChange={(e) => onSelecionar(e.target.checked ? itens.map((i) => i.ativo.id) : [])} /></th>
                    <th scope="col">Código</th><th scope="col">Nome</th><th scope="col">Tipo</th><th scope="col">Contexto/pai</th>
                    <th scope="col">Trazido por</th><th scope="col">Importado em</th>
                  </tr>
                </thead>
                <tbody>
                  {itens.map(({ ativo, atualizadoEm, pai, criadoPor }) => (
                    <tr key={ativo.id}>
                      <td><input type="checkbox" aria-label={`Selecionar ${ativo.nome}`} checked={selecionados.includes(ativo.id)}
                        onChange={(e) => onSelecionar(e.target.checked ? [...selecionados, ativo.id] : selecionados.filter((s) => s !== ativo.id))} /></td>
                      <td className="mono">{ativo.codigo}</td>
                      <td><Link href={`/ativos/${ativo.id}`}>{ativo.nome}</Link></td>
                      <td>{ativo.tipo}</td>
                      <td>{pai ? <Link href={`/ativos/${pai.id}`}>{pai.nome}</Link> : '—'}</td>
                      <td>{criadoPor ? nomeDaPessoa(criadoPor) : '—'}</td>
                      <td className="discreto">{quando.format(new Date(atualizadoEm))}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="modal-actions">
              <button type="button" className="outline-button" disabled={ocupado || selecionados.length === 0} onClick={() => onTriar('descartar')}>
                Descartar {selecionados.length || ''} (arquiva)
              </button>
              <button type="button" className="primary-button" disabled={ocupado || selecionados.length === 0} onClick={() => onTriar('aceitar')}>
                Aceitar {selecionados.length || ''}
              </button>
            </div>
            <p className="ajuda">Aceitar não publica nada: o ativo segue em rascunho e passa a ser cadastro de uma pessoa. Descartar arquiva, guardando a trilha.</p>
          </>
        )}
    </section>
  );
}

type Props = { ativos: AtivoResumo[]; podeImportar: boolean; onAlterado: () => Promise<void>; onErro: (mensagem: string) => void };

// As descobertas do painel (/descobertas): importar de um contrato OpenAPI ou
// de um inventário Git, com prévia antes de gravar, e triar o que chegou.
export function Descobertas({ ativos, podeImportar, onAlterado, onErro }: Props) {
  const [dados, setDados] = useState<DadosDasDescobertas | null>(null);
  const [pedido, setPedido] = useState<PedidoDeDescoberta>({ fonte: 'openapi', conteudo: '', idPai: null });
  const [plano, setPlano] = useState<PlanoDeImportacao | null>(null);
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');
  const [selecionados, setSelecionados] = useState<string[]>([]);
  const [ocupado, setOcupado] = useState(false);
  const [versao, setVersao] = useState(0);

  useEffect(() => {
    consultarDescobertas().then((d) => { setDados(d); setSelecionados([]); })
      .catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar as descobertas.'));
  }, [versao]);

  async function agir(acao: () => Promise<void>) {
    setOcupado(true);
    setErro('');
    setAviso('');
    try { await acao(); }
    catch (e) { setErro(e instanceof Error ? e.message : 'Não foi possível concluir.'); }
    finally { setOcupado(false); }
  }

  const aplicacoes = ativos.filter((a) => a.tipo === 'aplicacao' && a.status !== 'arquivado');
  return (
    <>
      <Cabecalho rotulo="TRABALHAR" titulo="Descobertas"
        texto="Traga APIs, endpoints e repositórios de um contrato OpenAPI ou de um inventário Git. Tudo entra em rascunho, para uma pessoa dizer o que é." />
      {aviso && <p className="aviso-de-sucesso" role="status">{aviso}</p>}
      {!dados ? <p className="vazio">Carregando...</p> : (
        <div className="descobertas">
          {podeImportar
            ? <FormularioDeImportacao dados={dados} aplicacoes={aplicacoes} pedido={pedido} plano={plano} erro={erro} ocupado={ocupado}
                onMudar={(p) => { setPedido(p); setPlano(null); }}
                onPrevia={() => void agir(async () => setPlano(await previaDeImportacao(pedido)))}
                onImportar={() => void agir(async () => {
                  const r = await importarDescobertas(pedido);
                  setPlano(null);
                  setPedido({ ...pedido, conteudo: '' });
                  setAviso(`${r.criados} ativo(s) importado(s)${r.atualizados ? `, ${r.atualizados} atualizado(s)` : ''}. Estão na bandeja para triagem.`);
                  await onAlterado();
                  setVersao((v) => v + 1);
                })} />
            : <p className="aviso-fase">Importar e triar descobertas é de tech lead, curador ou administrador. Você vê a bandeja.</p>}
          <BandejaDeDescobertas itens={dados.bandeja} selecionados={selecionados} ocupado={ocupado || !podeImportar}
            onSelecionar={setSelecionados}
            onTriar={(acao) => void agir(async () => {
              const r = await triarDescobertas(selecionados, acao);
              setAviso(`${r.tratados} ${acao === 'aceitar' ? 'aceito(s)' : 'descartado(s)'}${r.ignorados ? `; ${r.ignorados} ignorado(s)` : ''}.`);
              await onAlterado();
              setVersao((v) => v + 1);
            })} />
          {erro && !podeImportar && <p className="alerta-integridade" role="alert">{erro}</p>}
        </div>
      )}
    </>
  );
}
