import { useEffect, useState } from 'react';
import {
  consultarMetamodelo, consultarPerfil, contarNotificacoes, definirAoFaltarIdentificacao, definirIdentidade, listarAtivos, listarPessoas, listarSquads, obterIdentidade,
  type AtivoResumo, type Metamodelo, type Perfil as DadosDoPerfil, type Pessoa, type Squad,
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
import { Descobertas } from './telas/Descobertas';
import { EmBreve, NaoEncontrada } from './telas/EmBreve';
import { GestaoDeAcessos } from './telas/GestaoDeAcessos';
import { Grafo } from './telas/Grafo';
import { Mapas } from './telas/Mapas';
import { Mesa } from './telas/Mesa';
import { Notificacoes } from './telas/Notificacoes';
import { NovoAtivo } from './telas/NovoAtivo';
import { abaDaUrl, PaginaDoAtivo } from './telas/PaginaDoAtivo';
import { podeDescontinuar } from './telas/Impacto';
import { RelacoesEmLote } from './telas/RelacoesEmLote';
import { Perfil } from './telas/Perfil';
import { Pleitos } from './telas/Pleitos';
import { Politicas } from './telas/Politicas';
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
  const [metamodelo, setMetamodelo] = useState<Metamodelo | null>(null);
  const [naoLidas, setNaoLidas] = useState(0);
  const [squads, setSquads] = useState<Squad[]>([]);
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
      const [eu, lista, times] = await Promise.all([consultarPerfil(), listarPessoas(), listarSquads().catch(() => ({ squads: [] as Squad[] }))]);
      setSquads(times.squads);
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
      const resposta = await listarAtivos();
      setAtivos(resposta.ativos);
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

  // A bolha do sino: a cada troca de tela e, parado, a cada minuto.
  function contarAvisos() {
    contarNotificacoes().then((r) => setNaoLidas(r.naoLidas)).catch(() => setNaoLidas(0));
  }
  useEffect(contarAvisos, [identidade, local.caminho]);
  useEffect(() => {
    const relogio = setInterval(contarAvisos, 60_000);
    return () => clearInterval(relogio);
  }, [identidade]);

  useEffect(() => {
    const titulo = itemDaSecao(secao)?.rotulo ?? 'Página não encontrada';
    document.title = `${titulo} · Jornada DDD`;
    setErro('');
  }, [secao]);

  function trocarIdentidade(nova: Identidade) {
    definirIdentidade(nova);
    gravarIdentidade(nova);
    if (nova.empresaId !== identidade.empresaId) setAtivos([]);
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
  const rotaDoAtivo = /^\/ativos\/([0-9a-f-]{36})(\/relacoes-lote|\/grafo)?\/?$/i.exec(local.caminho);

  let tela;
  if (rotaDoAtivo) {
    const [, idDoAtivo, subpagina] = rotaDoAtivo;
    tela = subpagina === '/grafo'
      ? <Grafo id={idDoAtivo} saltos={Math.min(Math.max(Number(local.busca.get('saltos')) || 1, 1), 3)}
          tipos={(local.busca.get('tipos') ?? '').split(',').filter(Boolean)} metamodelo={metamodelo} onErro={setErro} />
      : subpagina
      ? <RelacoesEmLote id={idDoAtivo} tipo={local.busca.get('tipo') ?? ''} ativos={ativos} metamodelo={metamodelo} onAlterado={carregar} onErro={setErro} />
      : <PaginaDoAtivo key={idDoAtivo} id={idDoAtivo} aba={abaDaUrl(local.busca.get('aba'))} ativos={ativos} pessoas={pessoas} metamodelo={metamodelo} squads={squads}
          podeEscrever={podeEscrever} podeDescontinuar={podeEscrever && podeDescontinuar(papeis)} idPessoa={perfil?.pessoa?.id ?? null}
          blocos={perfil?.blocosQueEscreve ?? []} identificado={!!perfil?.identificado} modoLeitura={!!perfil?.modoLeitura}
          onAlterado={carregar} onErro={setErro} />;
  } else if (item?.fase !== undefined) {
    tela = <EmBreve item={item} />;
  } else {
    switch (secao) {
      case 'painel':
        tela = <VisaoExecutiva key={identidade.empresaId} busca={local.busca} nomeDaEmpresa={nomeDaEmpresa} podeEscrever={podeEscrever}
          podeFotografar={podeEscrever && papeis.includes('admin')} onErro={setErro} />;
        break;
      case 'catalogo':
        tela = <Catalogo key={identidade.empresaId} busca={local.busca} idSelecionado={local.busca.get('ativo')} metamodelo={metamodelo} squads={squads}
          papeis={papeis} idPessoa={perfil?.pessoa?.id ?? null} podeEscrever={podeEscrever} onAlterado={carregar} onErro={setErro} />;
        break;
      case 'novo':
        tela = <NovoAtivo key={`${blocos.join('|')}-${metamodelo ? 'm' : ''}`} ativos={ativos} metamodelo={metamodelo} squads={squads} blocos={blocos}
          identificado={!!perfil?.identificado} modoLeitura={!!perfil?.modoLeitura}
          tipoInicial={local.busca.get('tipo') ?? undefined} criticidadeInicial={local.busca.get('criticidade') ?? undefined}
          onCadastrado={carregar} onErro={setErro} />;
        break;
      case 'validacoes':
        tela = <Validacoes aba={local.busca.get('etapa') ?? ''} selecionada={local.busca.get('validacao')} idPessoa={perfil?.pessoa?.id ?? null}
          podeDecidir={podeDecidir} onAlterado={carregar} onErro={setErro} />;
        break;
      case 'mapas':
        tela = <Mapas visao={local.busca.get('visao') === 'tecnica' ? 'tecnica' : 'negocio'} onErro={setErro} />;
        break;
      case 'mesa':
        tela = <Mesa key={identidade.pessoaId ?? ''} squads={squads} onErro={setErro} />;
        break;
      case 'politicas':
        tela = <Politicas onErro={setErro} />;
        break;
      case 'descobertas':
        tela = <Descobertas key={identidade.empresaId} ativos={ativos}
          podeImportar={podeEscrever && papeis.some((p) => p === 'admin' || p === 'curador' || p === 'techlead')}
          onAlterado={carregar} onErro={setErro} />;
        break;
      case 'notificacoes':
        tela = <Notificacoes key={identidade.pessoaId ?? ''} aba={local.busca.get('aba') === 'preferencias' ? 'preferencias' : 'caixa'}
          apenasNaoLidas={local.busca.get('naoLidas') === '1'} onAlterado={contarAvisos} onErro={setErro} />;
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
      naoLidas={naoLidas}
    >
      <Erro mensagem={erro} onFechar={() => setErro('')} />
      {tela}
    </Layout>
  );
}

export default App;
