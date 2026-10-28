import { useEffect, useRef, type FormEvent, type ReactNode } from 'react';
import { Bell, ChevronsLeft, ChevronsRight, HelpCircle, Search } from 'lucide-react';
import { CARTILHA, itensVisiveis, type Grupo, type ItemDeMenu, type Secao } from './navegacao';
import { estadoDoTema, type Tema } from './preferencias';
import { Link } from './roteador';
import { empresaDe, type UsuarioDeTeste } from './usuarios';

const GRUPOS: (Grupo | undefined)[] = [undefined, 'Trabalhar', 'Você', 'Administração'];

type Props = {
  secao: Secao;
  usuario: UsuarioDeTeste;
  tema: Tema;
  onAlternarTema: () => void;
  menuCompacto: boolean;
  onAlternarMenu: () => void;
  termo: string;
  onBuscar: (termo: string) => void;
  children: ReactNode;
};

export function Layout({ secao, usuario, tema, onAlternarTema, menuCompacto, onAlternarMenu, termo, onBuscar, children }: Props) {
  const itens = itensVisiveis(usuario.papel);
  const estadoTema = estadoDoTema(tema);
  const campoBusca = useRef<HTMLInputElement>(null);

  // "/" ou Ctrl+K (Cmd+K) levam à busca de qualquer tela, como no painel.
  useEffect(() => {
    function atalho(evento: KeyboardEvent) {
      const alvo = document.activeElement?.tagName ?? '';
      const digitando = /^(INPUT|TEXTAREA|SELECT)$/.test(alvo);
      if ((evento.key === '/' && !digitando) || (evento.key.toLowerCase() === 'k' && (evento.ctrlKey || evento.metaKey))) {
        evento.preventDefault();
        campoBusca.current?.focus();
        campoBusca.current?.select();
      }
    }
    document.addEventListener('keydown', atalho);
    return () => document.removeEventListener('keydown', atalho);
  }, []);

  function buscar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    onBuscar(new FormData(evento.currentTarget).get('termo')?.toString().trim() ?? '');
  }

  return (
    <>
      <a className="pular" href="#conteudo">Pular para o conteúdo</a>
      <div className="aplicacao">
        <nav className="menu" id="menu-lateral" aria-label="Navegação principal">
          <div className="topo-menu">
            <span className="marca"><span className="marca-longa">Jornada DDD</span><span className="marca-curta" aria-hidden="true">DDD</span></span>
            <button
              type="button"
              className="recolher"
              onClick={onAlternarMenu}
              aria-controls="menu-lateral"
              aria-expanded={!menuCompacto}
              title={menuCompacto ? 'Expandir o menu' : 'Recolher o menu'}
            >
              {menuCompacto ? <ChevronsRight size={15} aria-hidden="true" /> : <ChevronsLeft size={15} aria-hidden="true" />}
              <span className="leitor">{menuCompacto ? 'Expandir o menu lateral' : 'Recolher o menu lateral'}</span>
            </button>
          </div>
          {GRUPOS.map((grupo) => {
            const doGrupo = itens.filter((item) => item.grupo === grupo);
            if (doGrupo.length === 0) return null;
            return (
              <div className="menu-grupo" key={grupo ?? 'principal'}>
                {grupo && <div className="grupo" aria-hidden={menuCompacto}>{grupo}</div>}
                {doGrupo.map((item) => <ItemMenu key={item.secao} item={item} ativo={item.secao === secao} />)}
              </div>
            );
          })}
        </nav>

        <div className="coluna">
          <header className="topo">
            <form className="busca-global" role="search" onSubmit={buscar} autoComplete="off">
              <Search size={16} aria-hidden="true" />
              <input
                ref={campoBusca}
                type="search"
                name="termo"
                key={termo}
                defaultValue={termo}
                placeholder="Buscar ativo por nome, código ou tipo"
                aria-label="Buscar em todo o catálogo"
                aria-describedby="busca-atalho"
              />
              <kbd id="busca-atalho" title="Atalho: barra ou Ctrl+K">/</kbd>
            </form>
            <div className="identidade">
              <button type="button" className="tema" onClick={onAlternarTema} title={`${estadoTema.rotulo} — clique para alternar`} aria-live="polite">
                <span className="glifo" aria-hidden="true">{estadoTema.glifo}</span><span className="tema-rotulo">{estadoTema.rotulo}</span>
              </button>
              <Link className="sino" href="/notificacoes" aria-label="Notificações">
                <Bell size={15} aria-hidden="true" /> <span className="rotulo-topo">Notificações</span>
              </Link>
              <Link className={secao === 'perfil' ? 'quem ativo' : 'quem'} href="/perfil" title="Seu perfil: quem assina o que você fizer">
                <span className="quem-texto"><strong>{usuario.nome}</strong><small>{usuario.rotuloPapel} · {empresaDe(usuario).nome}</small></span>
              </Link>
              {/* Ao lado de quem você é: o que o seu papel permite fazer. */}
              <Link
                className={secao === 'cartilha' ? 'cartilha-atalho ativo' : 'cartilha-atalho'}
                href={CARTILHA.caminho}
                title="Cartilha: o que o seu papel faz na ferramenta"
                aria-current={secao === 'cartilha' ? 'page' : undefined}
              >
                <HelpCircle size={15} aria-hidden="true" /><span className="rotulo-topo">Cartilha</span>
              </Link>
            </div>
          </header>

          <main className="content" id="conteudo" tabIndex={-1}>
            {children}
          </main>

          <footer className="rodape">
            <p className="marca-rodape">Jornada DDD · Catálogo Corporativo</p>
            <nav aria-label="Links do rodapé">
              <Link href="/cartilha">Cartilha de uso</Link>
              <Link href="/politicas">Políticas de governança</Link>
              <Link href="/mapas">Mapas</Link>
            </nav>
          </footer>
        </div>
      </div>
    </>
  );
}

function ItemMenu({ item, ativo }: { item: ItemDeMenu; ativo: boolean }) {
  return (
    <Link
      href={item.caminho}
      className={ativo ? 'nav-item ativo' : 'nav-item'}
      aria-current={ativo ? 'page' : undefined}
      title={item.rotulo}
    >
      <span className="inicial" aria-hidden="true">{item.inicial}</span>
      <span className="rotulo">{item.rotulo}</span>
      {item.fase !== undefined && <span className="em-breve" title={`Chega na fase ${item.fase} do plano de paridade`}>fase {item.fase}</span>}
    </Link>
  );
}
