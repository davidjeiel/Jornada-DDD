import { useEffect, useState, type FormEvent } from 'react';
import { alterarSquad, cadastrarSquad, inativarSquad, listarSquads, type DadosDoSquad, type Squad } from '../api';
import { Cabecalho } from '../componentes';

type PropsDoFormulario = {
  editando: Squad | null;
  ocupado: boolean;
  onSalvar: (dados: DadosDoSquad) => void;
  onCancelar: () => void;
};

/** Título, sigla e descrição, os três obrigatórios; a sigla vai em maiúsculas, até 20 letras. */
export function FormularioDeSquad({ editando, ocupado, onSalvar, onCancelar }: PropsDoFormulario) {
  function salvar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    const d = new FormData(evento.currentTarget);
    onSalvar({ titulo: d.get('titulo')?.toString() ?? '', sigla: d.get('sigla')?.toString() ?? '', descricao: d.get('descricao')?.toString() ?? '' });
  }

  return (
    <form className="formulario-pagina" onSubmit={salvar} key={editando?.id ?? 'novo'}>
      <div className="form-grid">
        <label>Título<input name="titulo" required defaultValue={editando?.titulo} /></label>
        <label>Sigla<input name="sigla" required maxLength={20} defaultValue={editando?.sigla} />
          <small className="ajuda">Única na empresa, até 20 letras.</small></label>
      </div>
      <label>Descrição<textarea name="descricao" required rows={2} defaultValue={editando?.descricao} /></label>
      <div className="modal-actions">
        {editando && <button type="button" className="outline-button" onClick={onCancelar}>Cancelar</button>}
        <button className="primary-button" disabled={ocupado}>{editando ? 'Salvar squad' : 'Cadastrar squad'}</button>
      </div>
    </form>
  );
}

type PropsDaLista = { squads: Squad[]; ocupado: boolean; onEditar: (squad: Squad) => void; onInativar: (squad: Squad) => void };

export function ListaDeSquads({ squads, ocupado, onEditar, onInativar }: PropsDaLista) {
  return (
    <div className="rolagem">
      <table className="tabela">
        <thead><tr><th scope="col">Sigla</th><th scope="col">Título</th><th scope="col">Descrição</th><th scope="col">Situação</th><th scope="col"><span className="leitor">Ações</span></th></tr></thead>
        <tbody>
          {squads.length === 0 && <tr><td colSpan={5} className="vazio">Nenhum squad cadastrado.</td></tr>}
          {squads.map((s) => (
            <tr key={s.id}>
              <td className="mono">{s.sigla}</td>
              <td>{s.titulo}</td>
              <td className="discreto">{s.descricao}</td>
              <td><span className={`status ${s.ativa ? 'status-good' : 'status-arquivado'}`}>{s.ativa ? 'ativo' : 'inativo'}</span></td>
              <td className="sem-quebra">
                <button type="button" className="text-button" disabled={ocupado} onClick={() => onEditar(s)}>Editar</button>
                {s.ativa && <button type="button" className="text-button" disabled={ocupado} onClick={() => onInativar(s)}>Inativar</button>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

type Props = { onErro: (mensagem: string) => void };

// Os squads do painel (/squads), para quem concede acesso. Inativar não apaga:
// os vínculos existentes ficam.
export function Squads({ onErro }: Props) {
  const [squads, setSquads] = useState<Squad[]>([]);
  const [busca, setBusca] = useState('');
  const [editando, setEditando] = useState<Squad | null>(null);
  const [ocupado, setOcupado] = useState(false);

  async function carregar(termo = busca) {
    try {
      setSquads((await listarSquads(termo)).squads);
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível carregar os squads.');
    }
  }

  useEffect(() => {
    void carregar('');
  }, []);

  async function agir(acao: () => Promise<unknown>) {
    setOcupado(true);
    onErro('');
    try {
      await acao();
      setEditando(null);
      await carregar();
    } catch (e) {
      onErro(e instanceof Error ? e.message : 'Não foi possível concluir.');
    } finally {
      setOcupado(false);
    }
  }

  return (
    <>
      <Cabecalho rotulo="ADMINISTRAÇÃO" titulo="Squads" texto="Os times da empresa. A pessoa é ligada ao seu squad na gestão de acessos." />
      <section className="panel screen-panel" aria-labelledby="form-squad">
        <span className="panel-label" id="form-squad">{editando ? `Editando ${editando.sigla}` : 'Novo squad'}</span>
        <FormularioDeSquad editando={editando} ocupado={ocupado} onCancelar={() => setEditando(null)}
          onSalvar={(dados) => void agir(() => editando ? alterarSquad(editando.id, dados) : cadastrarSquad(dados))} />
      </section>
      <section className="panel screen-panel">
        <form className="barra-de-busca" role="search" onSubmit={(e) => { e.preventDefault(); void carregar(busca); }}>
          <input name="busca" type="search" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Título, sigla ou descrição" aria-label="Buscar squads" />
          <button className="outline-button">Buscar</button>
        </form>
        <ListaDeSquads squads={squads} ocupado={ocupado} onEditar={setEditando} onInativar={(s) => void agir(() => inativarSquad(s.id))} />
      </section>
    </>
  );
}
