import { useState, type FormEvent } from 'react';
import { solicitarAcesso } from '../api';
import { Cabecalho } from '../componentes';
import { PAPEIS, ROTULO_DO_PAPEL } from '../papeis';
import { Link } from '../roteador';

type Campos = { matricula: string; nome: string; email: string; unidade: string; papelPleiteado: string; justificativa: string };

const vazio: Campos = { matricula: '', nome: '', email: '', unidade: '', papelPleiteado: 'negocio', justificativa: '' };

type Props = {
  /** O cadastro identifica a pessoa na hora: consulta liberada, escrita só quando o papel sair. */
  onCadastrado: (idPessoa: string) => void;
  onErro: (mensagem: string) => void;
};

// Cadastro e pleito de papel: a única tela de escrita aberta a quem não tem
// papel, e tem de ser, porque é justamente quem ainda não foi autorizado que
// precisa dela. O que ela grava não autoriza nada (painel-ddd, /acesso/solicitar).
export function SolicitarAcesso({ onCadastrado, onErro }: Props) {
  const [campos, setCampos] = useState<Campos>(vazio);
  const [enviando, setEnviando] = useState(false);

  function campo<K extends keyof Campos>(nome: K) {
    return {
      name: nome,
      value: campos[nome],
      onChange: (e: { target: { value: string } }) => setCampos({ ...campos, [nome]: e.target.value }),
    };
  }

  async function enviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setEnviando(true);
    onErro('');
    try {
      const { idPessoa } = await solicitarAcesso(campos);
      onCadastrado(idPessoa);
    } catch (e) {
      // O que foi digitado continua no formulário: só a mensagem muda.
      onErro(e instanceof Error ? e.message : 'Não foi possível registrar o pleito.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <>
      <Cabecalho rotulo="ACESSO" titulo="Solicitar acesso"
        texto="Cadastre-se e diga de que papel precisa. Até um curador ou administrador decidir, você consulta o catálogo inteiro." />
      <form className="panel screen-panel formulario-pagina" onSubmit={enviar}>
        <div className="form-grid">
          <label>Matrícula<input required {...campo('matricula')} placeholder="C123456" autoComplete="username" />
            <small className="ajuda">Uma letra seguida de seis números.</small></label>
          <label>Unidade<input required {...campo('unidade')} placeholder="0427" inputMode="numeric" />
            <small className="ajuda">O código da sua unidade, com quatro números.</small></label>
        </div>
        <label>Nome completo<input required {...campo('nome')} autoComplete="name" /></label>
        <label>E-mail<input required type="email" {...campo('email')} autoComplete="email" /></label>
        <label>Papel pleiteado
          <select required {...campo('papelPleiteado')}>
            {PAPEIS.map((p) => <option key={p} value={p}>{ROTULO_DO_PAPEL[p]}</option>)}
          </select>
          <small className="ajuda">Na dúvida, veja na <Link href="/cartilha">cartilha</Link> o que cada papel faz. Quem decide pode conceder outro papel, explicando a troca.</small>
        </label>
        <label>Justificativa<textarea rows={3} {...campo('justificativa')} placeholder="Para que você precisa deste papel?" /></label>
        <div className="modal-actions">
          <Link className="outline-button" href="/perfil">Cancelar</Link>
          <button className="primary-button" disabled={enviando}>{enviando ? 'Enviando...' : 'Cadastrar e pedir o papel'}</button>
        </div>
      </form>
    </>
  );
}
