// Roteador mínimo sobre a History API: o app tem uma dúzia de rotas planas e
// não justifica uma dependência. O nginx já devolve o index.html para qualquer
// caminho (try_files), então link direto e F5 funcionam.
import { useEffect, useState, type AnchorHTMLAttributes, type MouseEvent } from 'react';

const EVENTO = 'jornada:navegou';

export type Local = { caminho: string; busca: URLSearchParams };

function localAtual(): Local {
  return { caminho: window.location.pathname, busca: new URLSearchParams(window.location.search) };
}

export function navegar(destino: string): void {
  window.history.pushState(null, '', destino);
  window.dispatchEvent(new Event(EVENTO));
  window.scrollTo({ top: 0 });
}

export function useLocal(): Local {
  const [local, setLocal] = useState<Local>(localAtual);
  useEffect(() => {
    const atualizar = () => setLocal(localAtual());
    window.addEventListener('popstate', atualizar);
    window.addEventListener(EVENTO, atualizar);
    return () => {
      window.removeEventListener('popstate', atualizar);
      window.removeEventListener(EVENTO, atualizar);
    };
  }, []);
  return local;
}

/** Um <a> de verdade (abre em nova aba, leitor de tela o anuncia como link) que navega sem recarregar. */
export function Link({ href, onClick, ...resto }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string }) {
  function aoClicar(evento: MouseEvent<HTMLAnchorElement>) {
    onClick?.(evento);
    if (evento.defaultPrevented || evento.button !== 0 || evento.metaKey || evento.ctrlKey || evento.shiftKey || evento.altKey) return;
    evento.preventDefault();
    navegar(href);
  }
  return <a {...resto} href={href} onClick={aoClicar} />;
}
