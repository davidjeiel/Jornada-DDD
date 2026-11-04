import { useEffect, useState, type FormEvent } from 'react';
import { obterAtivo, relacionarEmLote, type AtivoResumo, type FichaDoAtivo, type Metamodelo, type NovaRelacao } from '../api';
import { Cabecalho } from '../componentes';
import { Link, navegar } from '../roteador';
import { caminhoDaAba, candidatosAoDestino } from './PaginaDoAtivo';

/** Os destinos que já têm esta relação a partir da origem: aparecem marcados e travados. */
export function jaRelacionados(ficha: FichaDoAtivo, tipo: string): Set<string> {
  return new Set(ficha.relacoes.filter((r) => r.direcao === 'saida' && r.tipo === tipo).map((r) => r.ativoId));
}

type PropsDaSelecao = {
  ficha: FichaDoAtivo;
  tipo: string;
  ativos: AtivoResumo[];
  metamodelo: Metamodelo | null;
  ocupado: boolean;
  onTipo: (tipo: string) => void;
  onEnviar: (destinos: string[], relacao: NovaRelacao) => void;
};

/**
 * A seleção do lote, como /ativo/&lt;id&gt;/relacoes-lote do painel: os candidatos
 * dos tipos sugeridos, com os que já estão relacionados marcados e travados
 * (test_tela_de_lote_marca_o_que_ja_existe).
 */
export function SelecaoDoLote({ ficha, tipo, ativos, metamodelo, ocupado, onTipo, onEnviar }: PropsDaSelecao) {
  const existentes = jaRelacionados(ficha, tipo);
  const candidatos = candidatosAoDestino(ativos, ficha.id, tipo, metamodelo);

  function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    onEnviar(d.getAll('destino').map(String), {
      tipo, criticidade: d.get('criticidade')?.toString() ?? 'media', mecanismo: d.get('mecanismo')?.toString() || undefined,
    });
  }

  return (
    <form className="panel screen-panel formulario-pagina" onSubmit={enviar} key={tipo}>
      <div className="form-grid">
        <label>Tipo de relação
          <select name="tipo_relacao" value={tipo} onChange={(e) => onTipo(e.target.value)}>
            {(metamodelo?.relacoes ?? []).map((r) => <option key={r.chave} value={r.chave}>{r.rotulo}</option>)}
          </select>
        </label>
        <label>Mecanismo
          <select name="mecanismo" defaultValue=""><option value="">não informado</option>{(metamodelo?.mecanismos ?? []).map((m) => <option key={m.chave} value={m.chave}>{m.rotulo}</option>)}</select>
        </label>
        <label>Criticidade da dependência
          <select name="criticidade" defaultValue="media"><option value="baixa">baixa</option><option value="media">média</option><option value="alta">alta</option><option value="critica">crítica</option></select>
        </label>
      </div>
      <fieldset className="lista-do-lote">
        <legend>Destinos ({candidatos.length})</legend>
        {candidatos.length === 0 && <p className="vazio">Nenhum ativo dos tipos sugeridos para esta relação.</p>}
        {candidatos.map((a) => {
          const existe = existentes.has(a.id);
          return (
            <label key={a.id} className="caixa-de-marcar">
              {existe
                ? <input type="checkbox" checked disabled readOnly />
                : <input type="checkbox" name="destino" value={a.id} />}
              <span className="mono">{a.codigo}</span> {a.nome} <span className="discreto">· {a.tipo}</span>
              {existe && <span className="discreto"> (já relacionado)</span>}
            </label>
          );
        })}
      </fieldset>
      <div className="modal-actions">
        <Link className="outline-button" href={caminhoDaAba(ficha.id, 'relacoes')}>Cancelar</Link>
        <button className="primary-button" disabled={ocupado}>Registrar as relações marcadas</button>
      </div>
    </form>
  );
}

type Props = { id: string; tipo: string; ativos: AtivoResumo[]; metamodelo: Metamodelo | null; onAlterado: () => Promise<void>; onErro: (mensagem: string) => void };

export function RelacoesEmLote({ id, tipo: tipoInicial, ativos, metamodelo, onAlterado, onErro }: Props) {
  const [ficha, setFicha] = useState<FichaDoAtivo | null>(null);
  const [tipo, setTipo] = useState(tipoInicial || 'depende_de');
  const [ocupado, setOcupado] = useState(false);

  useEffect(() => {
    obterAtivo(id).then(setFicha).catch((e) => onErro(e instanceof Error ? e.message : 'Não foi possível carregar o ativo.'));
  }, [id]);

  async function enviar(destinos: string[], relacao: NovaRelacao) {
    if (destinos.length === 0) { onErro('Marque ao menos um destino.'); return; }
    setOcupado(true);
    onErro('');
    try {
      const r = await relacionarEmLote(id, destinos, relacao);
      await onAlterado();
      navegar(caminhoDaAba(id, 'relacoes'));
      if (r.erros.length > 0) onErro(`${r.criadas} criada(s), ${r.ignoradas} ignorada(s); recusadas: ${r.erros.map((e) => e.mensagem).join(' ')}`);
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível registrar as relações.');
    } finally {
      setOcupado(false);
    }
  }

  return (
    <>
      <Cabecalho rotulo={ficha ? `${ficha.codigo} · RELAÇÕES EM LOTE` : 'RELAÇÕES EM LOTE'} titulo={ficha ? ficha.nome : 'Relacionar em lote'}
        texto="Marque os destinos e registre todas as relações de uma vez. As que já existem aparecem travadas." />
      {ficha
        ? <SelecaoDoLote ficha={ficha} tipo={tipo} ativos={ativos} metamodelo={metamodelo} ocupado={ocupado} onTipo={setTipo} onEnviar={(d, r) => void enviar(d, r)} />
        : <p className="vazio">Carregando o ativo...</p>}
    </>
  );
}
