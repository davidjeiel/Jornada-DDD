-- ============================================================================
--  Sandbox de RLS — prova que o ambiente local está correto ANTES de existir
--  qualquer código .NET.
--
--  Rode  ./dev.ps1 testar-rls  (ou o psql do fim deste arquivo) e você saberá,
--  no dia 1, se o par Postgres+PgBouncer reproduz de verdade a aposta do
--  ADR-0003. Sem isso, o time descobre que o isolamento não funciona lá na
--  frente, quando corrigir custa caro.
--
--  APAGUE este arquivo quando as migrações reais do EF Core existirem: ele é
--  andaime, não schema.
-- ============================================================================

\connect catalogo

CREATE SCHEMA IF NOT EXISTS sandbox AUTHORIZATION catalogo_migrador;
GRANT USAGE ON SCHEMA sandbox TO catalogo_app;

SET ROLE catalogo_migrador;

CREATE TABLE sandbox.ativo_exemplo (
  id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  codigo    text NOT NULL,
  nome      text NOT NULL
);

-- Unicidade POR EMPRESA, não global. No schema SQLite atual, `codigo` é UNIQUE
-- global — o que quebraria assim que duas empresas usassem o mesmo código.
CREATE UNIQUE INDEX ux_sandbox_codigo ON sandbox.ativo_exemplo (tenant_id, codigo);
CREATE INDEX ix_sandbox_tenant_nome   ON sandbox.ativo_exemplo (tenant_id, lower(nome));

-- As TRÊS coisas que precisam estar juntas:
ALTER TABLE sandbox.ativo_exemplo ENABLE ROW LEVEL SECURITY;  -- 1. liga
ALTER TABLE sandbox.ativo_exemplo FORCE  ROW LEVEL SECURITY;  -- 2. vale até para o dono

CREATE POLICY isolamento_tenant ON sandbox.ativo_exemplo      -- 3. a política
  USING      (tenant_id = plataforma.tenant_atual())
  WITH CHECK (tenant_id = plataforma.tenant_atual());
--             ↑ USING filtra a LEITURA; WITH CHECK barra a ESCRITA com tenant alheio.
--               Sem o WITH CHECK dá para INSERIR linha que você não consegue ler.
--
--  NÃO escreva `current_setting('app.tenant_id', true)::uuid` aqui. Depois do fim
--  de uma transação a GUC volta a STRING VAZIA (não NULL), e ''::uuid é ERRO de
--  sintaxe, não NULL — a requisição sem contexto explodiria em vez de ver nada.
--  plataforma.tenant_atual() faz o NULLIF e garante falha fechada limpa.

GRANT SELECT, INSERT, UPDATE, DELETE ON sandbox.ativo_exemplo TO catalogo_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA sandbox TO catalogo_app;

-- ── Carga das duas empresas fictícias (UUIDs fixos: testes determinísticos) ──
--
--  Repare que a carga PRECISA abrir contexto de tenant, mesmo rodando como dono
--  da tabela. Isso não é burocracia: é o FORCE ROW LEVEL SECURITY funcionando.
--  Um INSERT em massa sem contexto falha com
--      ERROR: new row violates row-level security policy
--  e essa falha é a melhor prova de que o ambiente está correto. Se este bloco
--  passasse sem set_config, o FORCE não estaria valendo.

BEGIN;
  SET ROLE catalogo_migrador;
  SELECT set_config('app.tenant_id', '11111111-1111-1111-1111-111111111111', true);
  INSERT INTO sandbox.ativo_exemplo (tenant_id, codigo, nome) VALUES
    ('11111111-1111-1111-1111-111111111111', 'SIS-001', 'Sistema da Empresa A'),
    ('11111111-1111-1111-1111-111111111111', 'API-001', 'API da Empresa A');
COMMIT;

BEGIN;
  SET ROLE catalogo_migrador;
  SELECT set_config('app.tenant_id', '22222222-2222-2222-2222-222222222222', true);
  INSERT INTO sandbox.ativo_exemplo (tenant_id, codigo, nome) VALUES
    ('22222222-2222-2222-2222-222222222222', 'SIS-001', 'Sistema da Empresa B');
    --                                        ↑ MESMO código da Empresa A: permitido,
    --                                          porque a unicidade é (tenant_id, codigo)
COMMIT;

RESET ROLE;


-- ════════════════════════════════════════════════════════════════════════════
--  ROTEIRO DE VERIFICAÇÃO
--  Rode conectado como catalogo_app, ATRAVÉS DO PGBOUNCER (porta 6432):
--
--     psql "postgresql://catalogo_app:dev_app@localhost:6432/catalogo"
--
--  A porta importa. Conectar direto na 5432 não exercita o transaction pooling,
--  e é justamente aí que o `set_config(..., true)` prova seu valor.
-- ════════════════════════════════════════════════════════════════════════════

-- (1) SEM contexto: não se vê nada. Esperado: 0 linhas.
--     SELECT count(*) FROM sandbox.ativo_exemplo;

-- (2) Como Empresa A: vê só as duas dela. Esperado: 2.
--     BEGIN;
--       SELECT set_config('app.tenant_id', '11111111-1111-1111-1111-111111111111', true);
--       SELECT count(*) FROM sandbox.ativo_exemplo;
--     COMMIT;

-- (3) O TESTE QUE IMPORTA — o `true` do set_config torna o ajuste LOCAL à
--     transação. Depois do COMMIT o contexto morre, e a conexão devolvida ao
--     pool do PgBouncer não carrega a empresa anterior para a próxima
--     requisição. Esperado: 0. Se vier 2, há vazamento entre empresas.
--     SELECT count(*) FROM sandbox.ativo_exemplo;

-- (4) Escrita com tenant alheio é recusada pelo WITH CHECK.
--     Esperado: ERROR "new row violates row-level security policy".
--     BEGIN;
--       SELECT set_config('app.tenant_id', '11111111-1111-1111-1111-111111111111', true);
--       INSERT INTO sandbox.ativo_exemplo (tenant_id, codigo, nome)
--       VALUES ('22222222-2222-2222-2222-222222222222', 'X-1', 'invasor');
--     ROLLBACK;

-- (5) A auditoria não acusa nada. Esperado: nenhuma linha.
--     SELECT * FROM auditoria_rls()
--      WHERE tem_coluna_tenant AND NOT (rls_ligado AND rls_forcado AND tem_politica);
