# ADR-0003 — Empresa é fronteira de isolamento; unidade é escopo de autorização

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0004, ADR-0007, ADR-0011

## Contexto

O produto precisa atender **várias empresas, cada uma com várias unidades**. Essa
frase esconde a decisão mais consequente do projeto, porque "empresa" e "unidade"
parecem simetricamente o mesmo tipo de coisa e não são.

O código atual tem um embrião: `pessoa.unidade` é um `TEXT` de quatro dígitos,
validado pela regex `^[0-9]{4}$` em `acesso.validar_cadastro`, mas não participa de
nenhuma decisão de autorização. E `atribuicao_papel` já tem a peça certa:

```sql
escopo_tipo TEXT NOT NULL DEFAULT 'global',  -- global|dominio|squad
escopo_id   INTEGER
```

com `acesso.papeis_efetivos` resolvendo escopo por item. A estrutura para escopar
autorização já existe e funciona — falta um nível de escopo e falta a noção de
empresa, que hoje simplesmente não existe.

Errar aqui custa caro nos dois sentidos:
- tratar **unidade como tenant** multiplicaria bancos, backups, migrações e faturas
  por 10–100× sem ganho de isolamento, e ainda quebraria a consulta que mais importa
  para o cliente ("quero ver o catálogo consolidado da holding");
- tratar **empresa como escopo** (só um `WHERE empresa_id = ?`) faria do isolamento
  entre clientes uma questão de disciplina de programação, o que é inaceitável.

## Decisão

### 1. Dois conceitos, papéis diferentes

| | **Empresa** (`tenant_id`) | **Unidade** (`unidade_id`) |
|---|---|---|
| O que é | Cliente contratante | Divisão interna da empresa |
| Natureza | **Fronteira de isolamento** | **Escopo de autorização** |
| Define | RLS, backup, chave de criptografia, residência de dados, plano, fatura, SLA | quem vê e quem aprova o quê |
| Atravessável | **Nunca** | Sim, mediante papel de escopo superior |
| Onde é imposta | Banco (RLS) + gateway + token | Domínio (`PoliticaDeAutorizacao`) |
| Modelo | Raiz de agregado própria | Hierarquia (árvore) dentro da empresa |

Unidade é **hierárquica**: holding → diretoria → filial. O mesmo mecanismo de
`id_pai` que o catálogo já usa em `item_catalogo` serve aqui.

O escopo de papel passa de `global | dominio | squad` para
**`empresa | unidade | dominio | squad`**, onde `empresa` substitui o antigo
`global` (que agora é reservado a operadores da plataforma, no control plane).

### 2. `tenant_id` nunca é parâmetro

Esta é a regra inegociável. Nenhum caso de uso, repositório ou consulta recebe
`tenant_id` de quem chama. O caminho é sempre:

```
JWT (claim de empresa)
  → middleware resolve e valida contra o control plane
  → ContextoDeTenant (AsyncLocal, imutável, escopo de requisição)
  → interceptor de conexão executa SET LOCAL app.tenant_id
  → RLS filtra
```

```csharp
public sealed record ContextoDeTenant(IdDeEmpresa Empresa, IdDeUnidade? UnidadePadrao, Plano Plano);

// Adaptador de persistência — roda em TODA abertura de transação, sem exceção
public async Task AbrirTransacaoAsync(CancellationToken ct)
{
    var tx = await _conexao.BeginTransactionAsync(ct);
    await _conexao.ExecuteAsync(
        "SELECT set_config('app.tenant_id', @t, true)",  // true = LOCAL, morre com a transação
        new { t = _contexto.Empresa.Valor.ToString() }, tx);
}
```

Um caso de uso que precisasse receber `tenant_id` seria, por construção, um caso de
uso capaz de ler dados de outra empresa. Se algum dia aparecer essa necessidade,
ela vira um caso de uso do **control plane**, com autorização própria e auditoria
separada — não uma sobrecarga do caso de uso normal.

### 3. Isolamento imposto pelo banco, não pela aplicação

Toda tabela do data plane ganha `tenant_id UUID NOT NULL` e RLS:

```sql
ALTER TABLE item_catalogo ADD COLUMN tenant_id UUID NOT NULL;
ALTER TABLE item_catalogo ENABLE  ROW LEVEL SECURITY;
ALTER TABLE item_catalogo FORCE   ROW LEVEL SECURITY;   -- vale até para o dono da tabela

CREATE POLICY isolamento_tenant ON item_catalogo
  USING      (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);

-- Índices compostos sempre com tenant_id à esquerda
CREATE INDEX ix_item_tenant_tipo ON item_catalogo (tenant_id, tipo_item);

-- Unicidade passa a ser POR EMPRESA. Hoje `codigo` é UNIQUE global e
-- ux_item_nome_tipo é UNIQUE (tipo_item, lower(nome)) — ambos quebrariam
-- entre clientes diferentes.
CREATE UNIQUE INDEX ux_item_codigo    ON item_catalogo (tenant_id, codigo);
CREATE UNIQUE INDEX ux_item_nome_tipo ON item_catalogo (tenant_id, tipo_item, lower(nome));
```

Três detalhes que decidem se isso funciona de verdade:

- **`FORCE ROW LEVEL SECURITY`**, não apenas `ENABLE`. Sem o `FORCE`, o dono da
  tabela ignora a política — e é comum a aplicação conectar como dono.
- **A role da aplicação não tem `BYPASSRLS` nem é superusuário.** Migrações usam
  uma role separada, com credencial separada, fora do caminho de requisição.
- **`WITH CHECK` além de `USING`**, senão é possível *inserir* linha com
  `tenant_id` alheio mesmo sem conseguir lê-la.

Com PgBouncer em modo *transaction pooling* (padrão no Azure Database for
PostgreSQL), `set_config(..., true)` é obrigatório: o `true` torna o ajuste local à
transação, e o contexto não vaza para a próxima requisição que reusar a conexão.
Usar `SET` de sessão aqui seria um vazamento entre empresas esperando acontecer.

### 4. Três tiers de isolamento

Modelo **particionado verticalmente**: a mesma base de código atende os três, e o
control plane roteia.

| Tier | Compute | Dados | Custo marginal | Para quem |
|---|---|---|---|---|
| **Padrão** | Compartilhado | Banco compartilhado + RLS | Muito baixo | A maioria dos clientes |
| **Dedicado** | Compartilhado | Schema ou banco próprio | Médio | Regulados, grande volume, exigência contratual |
| **Soberano** | *Stamp* completo | Rede, banco e app próprios | Alto — precifique | Setor público, residência de dados específica |

A aplicação **não sabe em qual tier está rodando**: ela pede a connection string ao
resolvedor de tenant, que consulta o mapa no control plane. Promover um cliente de
Padrão para Dedicado é migração de dados + atualização do mapa, sem mudar código.

O tier Soberano é o *Deployment Stamps pattern* e depende do ADR-0011 estar maduro.
**Não venda esse tier antes disso.**

### 5. Teste que tenta vazar e espera falhar

```csharp
[Fact]
public async Task Empresa_A_nao_enxerga_ativo_da_empresa_B()
{
    await Como(empresaA, async ctx => await ctx.Ativos.AdicionarAsync(UmAtivo("SIS-001")));
    await Como(empresaB, async ctx => Assert.Empty(await ctx.Ativos.BuscarAsync("SIS-001")));
}

[Fact]
public async Task Escrever_com_tenant_id_alheio_e_recusado_pelo_banco()
{
    await Como(empresaA, async ctx =>
        await Assert.ThrowsAsync<PostgresException>(() =>
            ctx.Executar("INSERT INTO item_catalogo (tenant_id, ...) VALUES (@outro, ...)",
                         new { outro = empresaB.Id })));   // barrado pelo WITH CHECK
}

[Fact]
public async Task Nenhuma_tabela_do_data_plane_esta_sem_RLS()
{
    // varre pg_class e falha listando as tabelas sem relrowsecurity — pega a tabela nova
    // que alguém criou sem lembrar da política
}
```

O terceiro é o mais importante: ele protege contra o erro que realmente acontece,
que é esquecer o RLS numa tabela criada seis meses depois.

## Consequências

### Positivas

- Vazamento entre empresas exige **duas** falhas simultâneas (bug de aplicação *e*
  falha de política no banco), não uma.
- Custo por cliente marginal próximo de zero no tier Padrão — essencial para vender
  a empresas pequenas.
- Clientes exigentes têm caminho de upgrade sem fork de código, e esse upgrade é
  um produto vendável (ADR-0010).
- A holding enxerga suas unidades de forma consolidada, porque unidade nunca foi
  barreira de dados.

### Negativas

- **RLS tem custo de plano de execução.** Toda consulta ganha um predicado extra.
  Sem `tenant_id` como primeira coluna de todo índice composto, o desempenho
  degrada de forma silenciosa. Exige teste de carga desde a Fase 1.
- **Migração de schema fica mais cara**: `ALTER TABLE` numa tabela compartilhada
  afeta todos os clientes ao mesmo tempo. Obriga migração compatível para trás,
  em duas fases (expandir → migrar → contrair).
- **Ruído entre vizinhos é possível** no tier Padrão. Mitigação: cota por empresa
  no gateway (ADR-0010), `statement_timeout` por role e alerta de consumo
  desproporcional.
- **Três tiers é mais código de infraestrutura** e mais caminhos a testar.

### Neutras / a monitorar

- O `id_squad` atual continua existindo como escopo, agora sempre dentro de uma
  empresa. Squad e unidade são dimensões ortogonais — uma squad pode atravessar
  unidades — e o modelo de papel suporta as duas.
- A tabela `pessoa` fica em situação especial: uma pessoa física pode ser usuária
  de mais de uma empresa (consultor). O vínculo `pessoa × empresa` é uma tabela de
  associação; a identidade é global (ADR-0007), a associação é por tenant.

## Alternativas consideradas

**Banco por empresa desde o dia 1.** Isolamento máximo e sem risco de RLS. Recusada:
migração de schema em N bancos, custo fixo por cliente pequeno, e consultas
agregadas de plataforma (o próprio dashboard executivo) viram trabalho de ETL.
Continua disponível como tier Dedicado.

**Schema por empresa (schema-based sharding).** Meio-termo elegante, suportado por
elastic clusters no Azure Database for PostgreSQL. Recusada como padrão: o número
de objetos no catálogo do Postgres cresce linearmente com o número de clientes e
degrada `pg_dump`, migração e o próprio planner. Fica como implementação do tier
Dedicado, onde o número de clientes é pequeno por definição.

**Só `WHERE tenant_id = ?` na aplicação, sem RLS.** Mais simples e mais rápido.
Recusada: transforma cada consulta nova em risco de vazamento, e um `JOIN`
esquecido em código de relatório é suficiente para o incidente. Para este produto
— que guarda o mapa de sistemas e dependências de empresas clientes — o dado é
sensível o bastante para justificar a rede de segurança.

**Unidade como tenant.** Recusada pelos motivos da seção de Contexto: custo
multiplicado e quebra do caso de uso consolidado da holding.

## Revisitar quando

- O tier Padrão passar de ~500 empresas ou o maior cliente passar de ~30% do volume
  do banco — avaliar elastic clusters com sharding por `tenant_id`.
- Um teste de carga mostrar degradação atribuível ao predicado de RLS acima de 15%
  em consultas do caminho crítico.
- Surgir exigência de residência de dados em região onde ainda não há stamp.
