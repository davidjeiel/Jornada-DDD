// O desenho da vizinhança em anéis concêntricos por distância do centro.
// Portado de servicos.posicionar_vizinhanca: layout escrito à mão, sem
// biblioteca. O anel diz a distância de relação, e o mesmo grafo desenha igual
// toda vez, o que um layout de força não garante.
import type { Aresta, NoDaVizinhanca, Vizinhanca } from './api';

export type NoPosicionado = NoDaVizinhanca & { x: number; y: number };
export type ArestaPosicionada = Aresta & { x1: number; y1: number; x2: number; y2: number };

const arredondar = (v: number) => Math.round(v * 10) / 10;

export function posicionar(dados: Vizinhanca, largura = 900, altura = 560): { nos: NoPosicionado[]; arestas: ArestaPosicionada[] } {
  const cx = largura / 2;
  const cy = altura / 2;
  // Anéis elípticos: a tela é mais larga que alta, e um raio único deixaria metade do desenho vazia.
  const rxMax = largura / 2 - 160;
  const ryMax = altura / 2 - 50;
  const porSalto = new Map<number, NoDaVizinhanca[]>();
  for (const no of dados.nos) porSalto.set(no.salto, [...(porSalto.get(no.salto) ?? []), no]);
  const maiorSalto = Math.max(0, ...porSalto.keys());

  const posicoes = new Map<string, [number, number]>();
  for (const [salto, nos] of [...porSalto.entries()].sort(([a], [b]) => a - b)) {
    if (salto === 0) { for (const no of nos) posicoes.set(no.ativo.id, [cx, cy]); continue; }
    const fracao = salto / Math.max(maiorSalto, 1);
    // Cada anel gira um pouco, para os nós não ficarem alinhados em raios.
    const giro = (Math.PI / nos.length) * (salto % 2);
    nos.forEach((no, i) => {
      const angulo = (2 * Math.PI * i) / nos.length + giro;
      posicoes.set(no.ativo.id, [cx + rxMax * fracao * Math.cos(angulo), cy + ryMax * fracao * Math.sin(angulo)]);
    });
  }

  return {
    nos: dados.nos.map((no) => {
      const [x, y] = posicoes.get(no.ativo.id)!;
      return { ...no, x: arredondar(x), y: arredondar(y) };
    }),
    arestas: dados.arestas.filter((a) => posicoes.has(a.origem) && posicoes.has(a.destino)).map((a) => {
      const [x1, y1] = posicoes.get(a.origem)!;
      const [x2, y2] = posicoes.get(a.destino)!;
      return { ...a, x1: arredondar(x1), y1: arredondar(y1), x2: arredondar(x2), y2: arredondar(y2) };
    }),
  };
}
