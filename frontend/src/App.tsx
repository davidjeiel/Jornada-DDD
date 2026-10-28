import { useEffect, useState } from 'react';
import { definirUsuarioAtual, empresaDe, listarAtivos, listarValidacoes, obterUsuarioAtual, type AtivoResumo, type Validacao } from './api';
import { Erro } from './componentes';
import { Layout } from './Layout';
import { itemDaSecao, secaoDoCaminho } from './navegacao';
import {
  armazenamentoDoNavegador, gravarMenuCompacto, gravarTema, lerMenuCompacto, lerTema, proximoTema, type Tema,
} from './preferencias';
import { navegar, useLocal } from './roteador';
import { Cartilha } from './telas/Cartilha';
import { Catalogo } from './telas/Catalogo';
import { EmBreve, NaoEncontrada } from './telas/EmBreve';
import { NovoAtivo } from './telas/NovoAtivo';
import { Perfil } from './telas/Perfil';
import { Validacoes } from './telas/Validacoes';
import { VisaoExecutiva } from './telas/VisaoExecutiva';
import { salvarUsuario, type UsuarioDeTeste } from './usuarios';

const armazenamento = armazenamentoDoNavegador();

function App() {
  const local = useLocal();
  const secao = secaoDoCaminho(local.caminho);

  const [usuario, setUsuario] = useState<UsuarioDeTeste>(() => obterUsuarioAtual());
  const [ativos, setAtivos] = useState<AtivoResumo[]>([]);
  const [validacoes, setValidacoes] = useState<Validacao[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');

  // Tema e menu: o script do index.html já aplicou os dois antes da primeira
  // pintura; aqui o React só assume o controle dali em diante.
  const [tema, setTema] = useState<Tema>(() => lerTema(armazenamento));
  const [menuCompacto, setMenuCompacto] = useState(() => lerMenuCompacto(armazenamento));

  useEffect(() => {
    const raiz = document.documentElement;
    if (tema === 'sistema') raiz.removeAttribute('data-theme');
    else raiz.setAttribute('data-theme', tema);
  }, [tema]);

  useEffect(() => {
    const raiz = document.documentElement;
    if (menuCompacto) raiz.setAttribute('data-menu', 'compacto');
    else raiz.removeAttribute('data-menu');
  }, [menuCompacto]);

  function alternarTema() {
    const proximo = proximoTema(tema);
    gravarTema(armazenamento, proximo);
    setTema(proximo);
  }

  function alternarMenu() {
    gravarMenuCompacto(armazenamento, !menuCompacto);
    setMenuCompacto(!menuCompacto);
  }

  async function carregar() {
    setCarregando(true);
    try {
      const [resposta, fila] = await Promise.all([listarAtivos(), listarValidacoes()]);
      setAtivos(resposta.ativos);
      setValidacoes(fila.validacoes);
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível carregar o catálogo.');
    } finally {
      setCarregando(false);
    }
  }

  useEffect(() => {
    void carregar();
  }, [usuario.id]);

  useEffect(() => {
    const titulo = itemDaSecao(secao)?.rotulo ?? 'Página não encontrada';
    document.title = `${titulo} · Jornada DDD`;
    setErro('');
  }, [secao]);

  function trocarUsuario(novo: UsuarioDeTeste) {
    definirUsuarioAtual(novo);
    salvarUsuario(novo);
    setAtivos([]);
    setValidacoes([]);
    setUsuario(novo);
  }

  function buscar(termo: string) {
    navegar(termo ? `/catalogo?termo=${encodeURIComponent(termo)}` : '/catalogo');
  }

  const podeEscrever = usuario.papel !== 'consulta';
  const item = itemDaSecao(secao);

  let tela;
  if (item?.fase !== undefined) {
    tela = <EmBreve item={item} />;
  } else {
    switch (secao) {
      case 'painel':
        tela = <VisaoExecutiva ativos={ativos} validacoes={validacoes} nomeDaEmpresa={empresaDe(usuario).nome} podeEscrever={podeEscrever} />;
        break;
      case 'catalogo':
        tela = <Catalogo ativos={ativos} carregando={carregando} termo={local.busca.get('termo') ?? ''} idSelecionado={local.busca.get('ativo')} usuario={usuario} podeEscrever={podeEscrever} onAlterado={carregar} onErro={setErro} />;
        break;
      case 'novo':
        tela = <NovoAtivo ativos={ativos} podeEscrever={podeEscrever} onCadastrado={carregar} onErro={setErro} />;
        break;
      case 'validacoes':
        tela = <Validacoes validacoes={validacoes} ativos={ativos} podeDecidir={podeEscrever} onAlterado={carregar} onErro={setErro} />;
        break;
      case 'perfil':
        tela = <Perfil usuario={usuario} onTrocar={trocarUsuario} />;
        break;
      case 'cartilha':
        tela = <Cartilha />;
        break;
      default:
        tela = <NaoEncontrada />;
    }
  }

  return (
    <Layout
      secao={secao}
      usuario={usuario}
      tema={tema}
      onAlternarTema={alternarTema}
      menuCompacto={menuCompacto}
      onAlternarMenu={alternarMenu}
      termo={secao === 'catalogo' ? local.busca.get('termo') ?? '' : ''}
      onBuscar={buscar}
    >
      <Erro mensagem={erro} onFechar={() => setErro('')} />
      {tela}
    </Layout>
  );
}

export default App;
