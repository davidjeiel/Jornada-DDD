# Jornada DDD

Catálogo corporativo governado, como **plataforma multiempresa**. Evolução do
[`painel-ddd`](https://github.com/davidjeiel/painel-ddd) (Flask + SQLite) para
.NET 10 sobre Azure, com arquitetura hexagonal e isolamento entre empresas
imposto pelo banco.

> **Primeira versão.** Estrutura hexagonal completa, domínio central portado com
> testes verdes, guardas de arquitetura e uma fatia vertical funcionando ponta a
> ponta. Ainda não há persistência real ligada — ver [Limitações](#limitações-desta-primeira-versão).

---

## Rodar

```bash
dotnet build Jornada-sem-nuget.slnx          # compila sem precisar de NuGet
dotnet run --project tests/Jornada.Arquitetura.Testes   # guardas do ADR-0001
dotnet run --project tests/Jornada.Dominio.Testes       # 49 testes de regra
dotnet run --project src/hosts/Jornada.Api              # API em memória
```

A API sobe sem infraestrutura nenhuma. Toda rota `/v1` exige o cabeçalho
`X-Empresa`; `GET /demo` mostra os cabeçalhos prontos.

```bash
A=11111111-1111-1111-1111-111111111111

# 1. cadastrar — repare que o corpo NÃO tem "usuario" nem "tenant_id"
ID=$(curl -s -H 'Content-Type: application/json' -H "X-Empresa: $A" -H 'X-Papel: curador' \
  -X POST http://localhost:5080/v1/ativos \
  -d '{"tipo":"sistema","nome":"Core Bancario","descricao":"Sistema central",
       "atributos":{"plataforma":"mainframe"}}' | jq -r .id)

# 2. submeter sem owner → 422, o pré-check bloqueia
curl -s -X POST http://localhost:5080/v1/ativos/$ID/submissoes -H "X-Empresa: $A" -d '{}'

# 3. definir o owner técnico
curl -s -X POST http://localhost:5080/v1/ativos/$ID/responsaveis \
  -H 'Content-Type: application/json' -H "X-Empresa: $A" \
  -d '{"idPessoa":"44444444-4444-4444-4444-444444444444","papel":"OwnerTecnico"}'

# 4. agora passa → 201, score 97, etapa técnica aberta
curl -s -X POST http://localhost:5080/v1/ativos/$ID/submissoes \
  -H 'Content-Type: application/json' -H "X-Empresa: $A" -d '{"motivo":"primeira versao"}'
```

Ambiente local completo (PostgreSQL + PgBouncer + Service Bus + Keycloak sobre
Podman): [`docs/AMBIENTE-LOCAL.md`](docs/AMBIENTE-LOCAL.md).

---

## Estrutura

```
src/
├── dominio/Jornada.Dominio/          ← ZERO dependência externa. Nem uma.
│   ├── Catalogo/                       Ativo, metamodelo, ciclo de vida
│   ├── Governanca/                     PoliticaDePublicacao (era pre_check)
│   ├── Qualidade/                      CalculadoraDeQualidade (era qualidade.py)
│   ├── Acesso/                         PoliticaDeAutorizacao (era acesso.pode)
│   └── Tenancy/                        Empresa, Unidade, ContextoDeTenant
├── aplicacao/Jornada.Aplicacao/      ← casos de uso + DEFINIÇÃO das portas
├── adaptadores/
│   ├── Jornada.Adaptadores.Memoria/    implementa as portas em memória
│   └── Jornada.Persistencia.Postgres/  EF Core + RLS + outbox
└── hosts/Jornada.Api/                ← Minimal API: traduz HTTP e nada mais

tests/
├── Jornada.Dominio.Testes/           49 casos portados do pytest
└── Jornada.Arquitetura.Testes/       a regra de dependência como build gate

docs/     13 ADRs + ARQUITETURA.md + AMBIENTE-LOCAL.md
local/    ambiente Podman (compose, RLS, Keycloak, Service Bus)
infra/    esqueleto Terraform
```

As decisões e o porquê de cada uma estão em [`docs/adr/`](docs/adr/README.md).
Comece por [`docs/ARQUITETURA.md`](docs/ARQUITETURA.md).

> **Nota de nomenclatura:** os ADRs escrevem `Catalogo.*` (Catalogo.Dominio,
> Catalogo.Api…). O código usa `Jornada.*`, para bater com o nome do
> repositório. Mesma estrutura, prefixo diferente.

---

## O que esta versão já prova

**1. A regra de negócio ficou testável sem banco.**

No `painel-ddd`, `governanca.pre_check(con, id_item)` mistura abrir consultas
SQL, aplicar regra e formatar bloqueios — e por isso `tests/test_governanca.py`
precisa de `create_app`, `seed` e `tmp_path`. Aqui a mesma regra é uma função
pura, e a suíte inteira roda em **~100 ms**:

```
49 passou, 0 falhou  em 104 ms
```

Todo o I/O que estava dentro da regra foi para
`Jornada.Aplicacao/Catalogo/Consultas/AvaliarPreCheck.cs`, que monta o contexto
pelas portas e chama a política.

**2. O hexágono é verificado, não combinado.**

`tests/Jornada.Arquitetura.Testes` inspeciona os assemblies compilados e falha o
build se alguém contaminar o núcleo:

```
OK   o DOMÍNIO não conhece infraestrutura
OK   o DOMÍNIO não conhece a aplicação (as setas apontam para dentro)
OK   toda porta é interface pública e mora em Jornada.Aplicacao.Portas
OK   nenhuma porta expõe tipo de infraestrutura na assinatura
OK   agregados não têm construtor público (só fábrica nomeada)
OK   os serviços de decisão são classes estáticas (sem estado, sem injeção)
OK   não há estado estático mutável no domínio
OK   não há DateTime.Now / UtcNow solto no domínio (use IRelogio)
```

As guardas foram testadas nos dois sentidos: introduzimos violações de propósito
e confirmamos que o build quebra.

**3. Empresa é fronteira, não filtro.**

`IdDeEmpresa` é um tipo nominal: passar um `IdDeAtivo` onde se espera uma empresa
não compila. Nenhum caso de uso recebe `tenant_id` de quem chama — ele vem do
token, pelo middleware, pela porta. E na API:

```
Empresa B lendo ativo da Empresa A  →  404
ativos visíveis para A: 1   |   para B: 0
```

**4. O papel autoriza a ação, mas não o objeto.**

`BLOCOS_POR_PAPEL` foi preservado: quem responde pelo negócio não cadastra uma
API, e quem responde pela técnica não redesenha a hierarquia de domínios.

```
negócio cadastrando API  →  403 SEM_PERMISSAO
```

---

## Limitações desta primeira versão

Ditas com clareza porque afetam o que você pode rodar hoje.

### 1. O adaptador Postgres não foi compilado

O ambiente onde esta versão foi gerada não tinha acesso ao **nuget.org**. Isso
não impediu o núcleo — `Jornada.Dominio` e `Jornada.Aplicacao` são, por decisão
de arquitetura, projetos **sem nenhum pacote externo** ([ADR-0001](docs/adr/0001-arquitetura-hexagonal-ddd.md)),
e a API usa só o framework compartilhado do ASP.NET Core.

Mas `Jornada.Persistencia.Postgres` precisa de EF Core e Npgsql. O código está
escrito e revisado; **não foi compilado**. Na sua máquina:

```bash
dotnet restore Jornada.slnx && dotnet build Jornada.slnx
```

O SQL dele, por outro lado, **foi validado** contra um PostgreSQL 16 real — e a
validação achou um bug de ordenação que já está corrigido (ver
`src/adaptadores/Jornada.Persistencia.Postgres/Esquema.sql`).

### 2. Os testes não usam xUnit

Pelo mesmo motivo. Em vez de entregar testes que ninguém conseguiu executar, eles
rodam sobre um harness próprio de ~120 linhas, sem dependência — e **rodam, verdes,
hoje**. O ativo são os casos, não o executor ([ADR-0012](docs/adr/0012-estrategia-de-migracao.md)).

**Migrar é mecânico**, e vale fazer assim que houver NuGet:

| harness atual | xUnit |
|---|---|
| `[Teste("descrição")]` | `[Fact(DisplayName = "descrição")]` |
| `[Suite("nome")]` | (nada — a classe basta) |
| `Verificar.Igual(a, b)` | `Assert.Equal(a, b)` |
| `Verificar.Verdadeiro(x)` | `Assert.True(x)` |
| `Verificar.Contem(c, i)` | `Assert.Contains(i, c)` |
| `Verificar.Lanca<T>(() => …)` | `Assert.Throws<T>(() => …)` |
| `<OutputType>Exe` | remover; adicionar `Microsoft.NET.Test.Sdk` + `xunit` |

As versões dos pacotes já estão fixadas em `Directory.Packages.props`.

### 3. O que ainda não existe

Fatia vertical ≠ produto. Falta, em ordem de roteiro ([ADR-0012](docs/adr/0012-estrategia-de-migracao.md)):

- migrations do EF Core e a carga inicial;
- fluxo de validação em etapas (abrir, assumir, decidir) — o `submeter` já
  devolve as etapas que a política exige, mas elas ainda não viram registro;
- workers de outbox, SLA e snapshot de indicadores;
- control plane (empresas, unidades, planos) — [ADR-0006](docs/adr/0006-topologia-de-servicos.md);
- identidade real (Entra External ID) — hoje o contexto vem de cabeçalhos HTTP;
- descoberta automática (OpenAPI/Git), notificações, relações e grafo;
- front React.

---

## Ligar o Postgres

O hexágono existe justamente para isso ser barato. Em `src/hosts/Jornada.Api/Program.cs`,
troque **estas linhas** — nenhum caso de uso muda:

```csharp
// de:
builder.Services.AddSingleton<ArmazemEmMemoria>();
builder.Services.AddScoped<RepositorioDeAtivosEmMemoria>();
builder.Services.AddScoped<IRepositorioDeAtivos>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());

// para:
builder.Services.AddDbContext<ContextoDeDados>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Catalogo")));
builder.Services.AddScoped<IRepositorioDeAtivos, RepositorioDeAtivosPostgres>();
builder.Services.AddScoped<IUnidadeDeTrabalho, TransacaoComTenant>();
```

A connection string aponta para a **porta 6432** (PgBouncer), não 5432. Apontar
para 5432 funciona e, em silêncio, deixa de exercitar o transaction pooling —
que é onde bug de contexto de tenant se esconde ([ADR-0003](docs/adr/0003-modelo-multi-tenancy.md)).

Depois das migrations, aplique `Esquema.sql`: ele traz RLS, políticas e a função
`plataforma.exigir_rls()`. **As migrations do EF não criam RLS** — sem esse
arquivo, o ADR-0003 está documentado, não implementado.

---

## Convenções

- **Português no domínio, inglês na infraestrutura.** A linguagem ubíqua do
  catálogo é em português; traduzir criaria um dicionário mental entre o código
  e quem usa o produto. O `painel-ddd` já fazia isso.
- **`TreatWarningsAsErrors`** ligado em todo o `src/`. Num domínio cheio de
  invariante, aviso de referência nula é bug esperando acontecer.
- **Construtor privado + fábrica nomeada** nos agregados: `Ativo.Rascunhar(...)`,
  nunca `new Ativo()`. Não existe instância inválida.
- **Objeto de valor no lugar de primitivo** para tudo que tem regra: `Matricula`,
  `CodigoDeUnidade`, `CodigoDeAtivo`, `IdDeEmpresa`.
- **O tempo entra por `IRelogio`**, nunca por `DateTime.UtcNow` — há guarda de
  arquitetura para isso.

## Licença

MIT, como o `painel-ddd`.
