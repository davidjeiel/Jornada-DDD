import { useEffect, useState } from 'react';
import {
  consultarMetamodelo, consultarPerfil, definirAoFaltarIdentificacao, definirIdentidade, listarAtivos, listarPessoas, listarValidacoes, obterIdentidade,
  type AtivoResumo, type Metamodelo, type Perfil as DadosDoPerfil, type Pessoa, type Validacao,
} from './api';
import { Erro } from './componentes';
import { gravarIdentidade, type Identidade } from './identidade';
import { Layout } from './Layout';
import { itemDaSecao, secaoDoCaminho } from './navegacao';
import {
  armazenamentoDoNavegador, gravarMenuCompacto, gravarTema, lerMenuCompacto, lerTema, proximoTema, type Tema,
} from './preferencias';
import { navegar, useLocal } from './roteador';
import { Cartilha } from './telas/Cartilha';
import { Catalogo } from './telas/Catalogo';
import { EmBreve, NaoEncontrada } from './telas/EmBreve';
import { GestaoDeAcessos } from './telas/GestaoDeAcessos';
import { NovoAtivo } from './telas/NovoAtivo';
import { abaDaUrl, PaginaDoAtivo } from './telas/PaginaDoAtivo';
import { podeDescontinuar } from './telas/Impacto';
import { RelacoesEmLote } from './telas/RelacoesEmLote';
import { Perfil } from './telas/Perfil';
import { Pleitos } from './telas/Pleitos';
import { SolicitarAcesso } from './telas/SolicitarAcesso';
import { Squads } from './telas/Squads';
import { Validacoes } from './telas/Validacoes';
import { VisaoExecutiva } from './telas/VisaoExecutiva';
import { empresaDe, registrarPessoas } from './usuarios';

const armazenamento = armazenamentoDoNavegador();

function App() {
  const local = useLocal();
  const secao = secaoDoCaminho(local.caminho);

  const [identidade, setIdentidade] = useState<Identidade>(() => obterIdentidade());
  const [perfil, setPerfil] = useState<DadosDoPerfil | null>(null);
  const [pessoas, setPessoas] = useState<Pessoa[]>([]);
  const [ativos, setAtivos] = useState<AtivoResumo[]>([]);
  const [validacoes, setValidacoes] = useState<Validacao[]>([]);
  const [metamodelo, setMetamodelo] = useState<Metamodelo | null>(null);
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

  useEffect(() => {
    definirAoFaltarIdentificacao(() => navegar('/perfil'));
  }, []);

  function alternarTema() {
    const proximo = proximoTema(tema);
    gravarTema(armazenamento, proximo);
    setTema(proximo);
  }

  function alternarMenu() {
    gravarMenuCompacto(armazenamento, !menuCompacto);
    setMenuCompacto(!menuCompacto);
  }

  async function carregarPerfil() {
    try {
      const [eu, lista] = await Promise.all([consultarPerfil(), listarPessoas()]);
      registrarPessoas(lista.pessoas);
      setPessoas(lista.pessoas);
      setPerfil(eu);
    } catch (e) {
      setErro(e instanceof Error ? e.message : 'Não foi possível saber quem é você.');
    }
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
    void carregarPerfil();
    void carregar();
    consultarMetamodelo().then(setMetamodelo).catch(() => setMetamodelo(null));
  }, [identidade]);

  useEffect(() => {
    const titulo = itemDaSecao(secao)?.rotulo ?? 'Página não encontrada';
    document.title = `${titulo} · Jornada DDD`;
    setErro('');
  }, [secao]);

  function trocarIdentidade(nova: Identidade) {
    definirIdentidade(nova);
    gravarIdentidade(nova);
    if (nova.empresaId !== identidade.empresaId) {
      setAtivos([]);
      setValidacoes([]);
    }
    setPerfil(null);
    setIdentidade(nova);
  }

  function buscar(termo: string) {
    navegar(termo ? `/catalogo?termo=${encodeURIComponent(termo)}` : '/catalogo');
  }

  // Quem escreve é quem o servidor diz: blocos do papel concedido, fora do modo leitura.
  const blocos = perfil?.blocosQueEscreve ?? [];
  const podeEscrever = blocos.length > 0;
  const podeDecidir = !!perfil?.identificado && !perfil.modoLeitura;
  const papeis = perfil?.papeis.map((p) => p.papel) ?? [];
  const nomeDaEmpresa = empresaDe(identidade.empresaId).nome;
  const item = itemDaSecao(secao);

  // /ativos/{id} e /ativos/{id}/relacoes-lote: dentro de um ativo, o menu continua no catálogo.
  const rotaDoAtivo = /^\/ativos\/([0-9a-f-]{36})(\/relacoes-lote)?\/?$/i.exec(local.caminho);

  let tela;
  if (rotaDoAtivo) {
    const [, idDoAtivo, lote] = rotaDoAtivo;
    tela = lote
      ? <RelacoesEmLote id={idDoAtivo} tipo={local.busca.get('tipo') ?? ''} ativos={ativos} metamodelo={metamodelo} onAlterado={carregar} onErro={setErro} />
      : <PaginaDoAtivo key={idDoAtivo} id={idDoAtivo} aba={abaDaUrl(local.busca.get('aba'))} ativos={ativos} pessoas={pessoas} metamodelo={metamodelo}
          podeEscrever={podeEscrever} podeDescontinuar={podeEscrever && podeDescontinuar(papeis)} idPessoa={perfil?.pessoa?.id ?? null}
          onAlterado={carregar} onErro={setErro} />;
  } else if (item?.fase !== undefined) {
    tela = <EmBreve item={item} />;
  } else {
    switch (secao) {
      case 'painel':
        tela = <VisaoExecutiva ativos={ativos} validacoes={validacoes} nomeDaEmpresa={nomeDaEmpresa} podeEscrever={podeEscrever} />;
        break;
      case 'catalogo':
        tela = <Catalogo ativos={ativos} carregando={carregando} termo={local.busca.get('termo') ?? ''} idSelecionado={local.busca.get('ativo')} papeis={papeis} idPessoa={perfil?.pessoa?.id ?? null} podeEscrever={podeEscrever} onAlterado={carregar} onErro={setErro} />;
        break;
      case 'novo':
        tela = <NovoAtivo key={blocos.join('|')} ativos={ativos} blocos={blocos} identificado={!!perfil?.identificado} modoLeitura={!!perfil?.modoLeitura} onCadastrado={carregar} onErro={setErro} />;
        break;
      case 'validacoes':
        tela = <Validacoes validacoes={validacoes} ativos={ativos} podeDecidir={podeDecidir} onAlterado={carregar} onErro={setErro} />;
        break;
      case 'perfil':
        tela = (
          <Perfil
            perfil={perfil}
            identidade={identidade}
            pessoas={pessoas}
            onIdentificar={(pessoaId) => trocarIdentidade({ ...identidade, pessoaId })}
            onTrocarEmpresa={(empresaId) => trocarIdentidade({ empresaId, pessoaId: null, modoLeitura: identidade.modoLeitura })}
            onModo={(modoLeitura) => trocarIdentidade({ ...identidade, modoLeitura })}
          />
        );
        break;
      case 'solicitar-acesso':
        tela = (
          <SolicitarAcesso
            onCadastrado={(pessoaId) => {
              // Quem acabou de se cadastrar já passa a ser quem disse que é.
              trocarIdentidade({ ...identidade, pessoaId, modoLeitura: false });
              navegar('/perfil');
            }}
            onErro={setErro}
          />
        );
        break;
      case 'pleitos':
        tela = <Pleitos ativos={ativos} onErro={setErro} onDecidido={() => void carregarPerfil()} />;
        break;
      case 'gestao-acessos':
        tela = <GestaoDeAcessos ativos={ativos} idDoAdmin={perfil?.pessoa?.id ?? null} onErro={setErro} />;
        break;
      case 'squads':
        tela = <Squads onErro={setErro} />;
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
      perfil={perfil}
      nomeDaEmpresa={nomeDaEmpresa}
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
