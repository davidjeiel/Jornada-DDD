# ADR-0001 — Adotar arquitetura hexagonal com DDD tático, com guardas automatizadas

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0002, ADR-0006

## Contexto

O código atual mistura três responsabilidades na mesma função. `governanca.pre_check`
é o exemplo mais claro: ela abre consultas SQL, aplica as regras de política e monta
a lista de códigos de bloqueio. A assinatura denuncia o acoplamento — quase toda
função de domínio do projeto começa com `con` (`sqlite3.Connection`):

```python
def pre_check(con, id_item: int) -> dict: ...
def pode(con, id_pessoa, acao, id_item=None) -> bool: ...
def avaliar(con, id_item: int) -> dict: ...
```

Três consequências práticas hoje:

1. **Não dá para testar regra sem banco.** `tests/test_governanca.py` precisa de
   `create_app`, `seed` e `tmp_path` para verificar uma regra que, em essência, é
   uma função pura de `(ativo, política, score) → bloqueios`.
2. **Trocar SQLite por PostgreSQL toca o domínio inteiro**, porque o domínio fala
   dialeto de banco diretamente.
3. **`servicos.py` tem 1.105 linhas e `web.py` 1.152** — não porque o domínio seja
   complexo, mas porque cada arquivo acumula orquestração, SQL e formatação.

Ao mesmo tempo, o projeto já demonstra maturidade conceitual: `acesso.pode()` como
ponto único de autorização, a segregação de função em `pode_decidir`, o outbox em
`notificacoes.py`, o par `plano_*`/`importar_*` em `integracoes.py` (simular antes
de gravar — isto é, essencialmente, uma porta com duas implementações). O modelo
mental correto já está lá; falta a estrutura que o sustente.

## Decisão

Adotar **Ports & Adapters (hexagonal)** com DDD tático, em quatro anéis e uma
única regra de dependência: **as setas apontam para dentro**.

### Os anéis

**1. `Catalogo.Dominio`** — puro. Nenhuma referência a banco, HTTP, fila, nuvem ou
framework de DI. `<PackageReference>` permitido: nenhum fora da BCL.

Contém agregados (`Ativo`, `RevisaoDeAtivo`, `Validacao`, `Empresa`), objetos de
valor (`CodigoDeAtivo`, `Criticidade`, `Escopo`, `ScoreDeQualidade`), eventos de
domínio (`AtivoSubmetido`, `RevisaoPublicada`, `ValidacaoDecidida`) e — o ganho
maior deste ADR — as **políticas como objetos puros**:

```csharp
public sealed class PoliticaDePublicacao
{
    public ResultadoPreCheck Avaliar(
        Ativo ativo,
        PoliticaDeGovernanca politica,
        ScoreDeQualidade score,
        IReadOnlyList<Relacao> relacoes,
        IReadOnlyList<Evidencia> evidencias)
    {
        var bloqueios = new List<CodigoDeBloqueio>();
        if (score.Total < politica.ScoreMinimo)      bloqueios.Add(CodigoDeBloqueio.ScoreAbaixoDoMinimo);
        if (evidencias.Count < politica.EvidenciaMinima) bloqueios.Add(CodigoDeBloqueio.EvidenciaInsuficiente);
        if (!ativo.TemOwnerVigente)                  bloqueios.Add(CodigoDeBloqueio.SemOwner);
        // ... as mesmas regras de governanca.pre_check, sem tocar em banco
        return new ResultadoPreCheck(bloqueios.Count == 0, bloqueios);
    }
}
```

O mesmo vale para `PoliticaDeAutorizacao` (de `acesso.pode`), `CalculadoraDeScore`
(de `qualidade.py`) e `MaquinaDeCicloDeVida` (de `governanca.TRANSICOES`).

**2. `Catalogo.Aplicacao`** — casos de uso e **definição das portas**. As
interfaces vivem aqui, não nos adaptadores; é isso que inverte a dependência.

```csharp
namespace Catalogo.Aplicacao.Portas;

public interface IRepositorioDeAtivos
{
    Task<Ativo?> ObterAsync(IdDeAtivo id, CancellationToken ct);
    Task<IReadOnlyList<Ativo>> ObterTrilhaAsync(IdDeAtivo id, CancellationToken ct);
    Task AdicionarAsync(Ativo ativo, CancellationToken ct);
}

public interface IUnidadeDeTrabalho { Task<int> ConfirmarAsync(CancellationToken ct); }
public interface IPublicadorDeEventos { Task PublicarAsync(IEnumerable<EventoDeDominio> e, CancellationToken ct); }
public interface IIndiceDeBusca { Task<PaginaDeResultados> BuscarAsync(CriterioDeBusca c, CancellationToken ct); }
public interface IRelogio { DateTimeOffset Agora { get; } }
```

`IRelogio` não é preciosismo: hoje `datetime('now')` está espalhado pelo SQL, o que
torna impossível testar SLA e vigência de forma determinística — exatamente o que
`governanca.sla_restante` e `vigiar-sla` precisam.

**3. Adaptadores** — implementam as portas. Um projeto por tecnologia, para que a
dependência apareça no `.csproj` e não escape:
`Catalogo.Persistencia.Postgres`, `Catalogo.Mensageria.ServiceBus`,
`Catalogo.Busca.AzureSearch`, `Catalogo.Identidade.Entra`,
`Catalogo.Descoberta.OpenApi`, `Catalogo.Descoberta.Git`.

**4. Hosts** — adaptadores de entrada (`Catalogo.Api`, `Catalogo.Workers`) e o
*composition root*, o único ponto do sistema que conhece domínio e infraestrutura
ao mesmo tempo.

### A guarda automatizada

A regra de dependência **não é combinado de equipe, é teste que quebra o build**:

```csharp
[Fact]
public void Dominio_nao_conhece_infraestrutura()
{
    var resultado = Types.InAssembly(typeof(Ativo).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Npgsql", "Azure.", "System.Net.Http",
            "Microsoft.AspNetCore", "Microsoft.Extensions.DependencyInjection")
        .GetResult();

    Assert.True(resultado.IsSuccessful,
        "Domínio contaminado: " + string.Join(", ", resultado.FailingTypeNames ?? []));
}

[Fact]
public void Aplicacao_nao_conhece_adaptadores() { /* idem, para Catalogo.Aplicacao */ }

[Fact]
public void Contextos_nao_se_referenciam_diretamente()
{
    // Governança não chama Descoberta; conversa via evento de domínio ou porta.
}
```

Sem este teste, o hexágono dura três sprints. Com ele, dura o projeto inteiro.

### Convenções de clean code que valem para todo o `src/`

- **Português no domínio, inglês na infraestrutura.** A linguagem ubíqua do
  catálogo é em português (`Ativo`, `Criticidade`, `Validacao`) — traduzir para
  inglês cria um dicionário mental entre o código e as pessoas que usam o produto.
  Infraestrutura segue a convenção da plataforma. O projeto já faz isso.
- **Construtor privado + método de fábrica nomeado** nos agregados, para que não
  exista instância inválida (`Ativo.Rascunhar(...)`, não `new Ativo()`).
- **Objeto de valor no lugar de primitivo** para tudo que tem regra: `Matricula`
  (hoje é a regex `^[A-Za-z][0-9]{6}$` solta em `acesso.py`), `Unidade` (`^[0-9]{4}$`),
  `CodigoDeAtivo`, `Criticidade`.
- **Exceção de domínio tipada**, nunca `Exception` genérica. `RegraDeNegocio` do
  `servicos.py` vira uma hierarquia, e o adaptador HTTP a traduz em
  [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) — hoje o
  `api.py` devolve `{"erro": "..."}` e 422 para tudo.
- **`sealed` por padrão**, `record` para objetos de valor e eventos, imutabilidade
  como default.
- **Sem `static` com estado**, sem singleton escondido: tudo entra por construtor.

## Consequências

### Positivas

- A suíte de regras de governança — hoje a mais cara do repositório — passa a
  rodar em memória, em milissegundos, com tabela de casos.
- Trocar PostgreSQL, Service Bus ou provedor de identidade é reimplementar uma
  interface, não caçar chamadas espalhadas.
- A extração de um contexto para serviço próprio (ADR-0006) vira troca de
  adaptador: o caso de uso não muda.
- A revisão de código ganha um critério objetivo em vez de gosto pessoal: *esta
  classe está no anel certo?*

### Negativas

- **Mais projetos e mais arquivos.** Uma operação simples de CRUD que hoje é uma
  função em `servicos.py` passa a tocar 3–4 arquivos. É um custo real,
  especialmente nos contextos de suporte.
- **Curva de aprendizado.** Inversão de dependência, composition root e agregados
  não são intuitivos para quem vem de Flask + SQL cru.
- **Risco de cerimônia vazia:** repositório que só delega para o ORM, DTO que só
  copia campo. Mitigação abaixo.

### Neutras / a monitorar

- **Não usar hexágono completo em contexto genérico.** Notificações e Indicadores
  não precisam de agregado rico; um caso de uso chamando um adaptador basta. O
  rigor arquitetural é proporcional à densidade de regra de negócio. Isto é
  decisão consciente, não descuido — e deve ser explicitado no README de cada
  módulo.
- **CQRS parcial, não total.** Consultas de leitura pesada (`visao_360`,
  `grafo_catalogo`, `indicadores`) podem ir direto de um adaptador de consulta
  para SQL/Dapper, sem passar por agregado. Forçar `visao_360` a materializar
  agregados seria perda de desempenho sem ganho de correção.

## Alternativas consideradas

**Arquitetura em camadas tradicional (Controller → Service → Repository).**
Recusada: é o que já existe em `servicos.py`, e ela não impede que o serviço
conheça o banco. A inversão de dependência é justamente o que falta.

**Vertical Slice Architecture (uma pasta por feature, handler acessa dados direto).**
Tentadora pela simplicidade e boa para CRUD. Recusada para os contextos centrais
porque o valor aqui está em regras que **atravessam** features: `pre_check` é usada
na publicação, no checklist da tela, no score e na API. Fatiar verticalmente
espalharia a mesma regra por cinco fatias. Adotada, porém, dentro da camada de
aplicação: a organização por `Comandos/` e `Consultas/` por contexto é vertical.

**Clean Architecture "por livro" (Entities/UseCases/Interface Adapters/Frameworks).**
Praticamente o mesmo desenho, com mais nomenclatura. Hexagonal foi escolhida por
ser explícita sobre *portas*, que é o conceito que o time vai usar todo dia.

**Manter o desenho atual e só trocar SQLite por PostgreSQL.** Resolve escala de
banco e nada mais: continua sem multi-tenancy segura, sem testabilidade de regra,
sem caminho para extrair serviço. Adia o problema com juros.

## Revisitar quando

- Um teste de arquitetura precisar de exceção pela terceira vez no mesmo trimestre
  — sinal de que a fronteira está desenhada no lugar errado.
- A razão entre linhas de "encanamento" e linhas de regra de negócio passar de 3:1
  em um contexto central.
