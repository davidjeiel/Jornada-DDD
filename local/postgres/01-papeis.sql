-- ============================================================================
--  Papéis do banco local — reproduz a separação de privilégio do ADR-0003.
--
--  A aposta de segurança do produto é: mesmo que a aplicação tenha um bug,
--  o BANCO não deixa uma empresa ler dados de outra. Isso só é verdade se
--  o papel da aplicação NÃO puder contornar RLS. Reproduzir isso localmente
--  é o que impede que os testes de isolamento deem confiança falsa.
--
--  Executado uma vez, na criação do volume.
-- ============================================================================

-- ─────────────────────────────────────────────────────────── papel de migração
-- Dono do schema. Roda EF Core Migrations. NUNCA é usado no caminho de requisição.
CREATE ROLE catalogo_migrador LOGIN PASSWORD 'dev_migrador'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;

-- ───────────────────────────────────────────────────────── papel da aplicação
-- É este que a API e os workers usam. As três negações abaixo são o ponto
-- inteiro deste arquivo:
--   NOSUPERUSER  → superusuário ignora RLS silenciosamente
--   NOBYPASSRLS  → sem isto, a política vira decoração
--   NOCREATEROLE → não pode criar um papel que contorne
CREATE ROLE catalogo_app LOGIN PASSWORD 'dev_app'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;

-- ───────────────────────────────────────── papel somente-leitura (BI, suporte)
CREATE ROLE catalogo_leitor LOGIN PASSWORD 'dev_leitor'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;

-- Timeout de instrução: em produção protege contra vizinho barulhento (ADR-0003).
-- Localmente, faz a consulta ruim falhar na sua máquina em vez de na do cliente.
ALTER ROLE catalogo_app    SET statement_timeout = '15s';
ALTER ROLE catalogo_leitor SET statement_timeout = '60s';

-- ───────────────────────────────────────────────────────────────── schemas
\connect catalogo

CREATE SCHEMA IF NOT EXISTS catalogo    AUTHORIZATION catalogo_migrador;  -- data plane
CREATE SCHEMA IF NOT EXISTS governanca  AUTHORIZATION catalogo_migrador;
CREATE SCHEMA IF NOT EXISTS notificacao AUTHORIZATION catalogo_migrador;
CREATE SCHEMA IF NOT EXISTS descoberta  AUTHORIZATION catalogo_migrador;

-- Schemas separados por módulo (ADR-0006, regra 2): o GRANT por schema é o que
-- impede o "JOIN de conveniência" que degenera um monólito modular em monólito.
REVOKE ALL ON SCHEMA public FROM PUBLIC;

DO $$
DECLARE s text;
BEGIN
  FOREACH s IN ARRAY ARRAY['catalogo','governanca','notificacao','descoberta'] LOOP
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO catalogo_app, catalogo_leitor', s);

    -- privilégios sobre o que ainda será criado pelas migrações
    EXECUTE format(
      'ALTER DEFAULT PRIVILEGES FOR ROLE catalogo_migrador IN SCHEMA %I
         GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO catalogo_app', s);
    EXECUTE format(
      'ALTER DEFAULT PRIVILEGES FOR ROLE catalogo_migrador IN SCHEMA %I
         GRANT SELECT ON TABLES TO catalogo_leitor', s);
    EXECUTE format(
      'ALTER DEFAULT PRIVILEGES FOR ROLE catalogo_migrador IN SCHEMA %I
         GRANT USAGE, SELECT ON SEQUENCES TO catalogo_app', s);
  END LOOP;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
--  Auditoria de RLS — a rede contra o erro que REALMENTE acontece:
--  alguém cria uma tabela nova seis meses depois e esquece a política.
--
--  Uso:   SELECT * FROM plataforma.auditoria_rls();   -- tabelas desprotegidas
--         SELECT plataforma.exigir_rls();             -- levanta exceção se houver
--
--  O teste de integração (ADR-0003) chama exigir_rls() e falha o build.
--
--  Fica em schema próprio, e não em `public`: o REVOKE acima tira USAGE de
--  public, então uma função criada lá seria invisível para catalogo_app.
-- ═══════════════════════════════════════════════════════════════════════════
CREATE SCHEMA IF NOT EXISTS plataforma AUTHORIZATION catalogo_migrador;
GRANT USAGE ON SCHEMA plataforma TO catalogo_app, catalogo_leitor;

CREATE OR REPLACE FUNCTION plataforma.auditoria_rls()
RETURNS TABLE (schema_nome text, tabela text, rls_ligado bool, rls_forcado bool,
               tem_politica bool, tem_coluna_tenant bool)
LANGUAGE sql STABLE AS $$
  SELECT n.nspname::text,
         c.relname::text,
         c.relrowsecurity,
         c.relforcerowsecurity,
         EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid),
         EXISTS (SELECT 1 FROM pg_attribute a
                  WHERE a.attrelid = c.oid AND a.attname = 'tenant_id'
                    AND NOT a.attisdropped)
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
   WHERE c.relkind = 'r'
     AND n.nspname IN ('catalogo','governanca','notificacao','descoberta','sandbox')
     AND c.relname NOT LIKE '\_\_EFMigrations%'   -- tabela de controle do EF Core
   ORDER BY 1, 2;
$$;

CREATE OR REPLACE FUNCTION plataforma.exigir_rls() RETURNS void
LANGUAGE plpgsql STABLE AS $$
DECLARE falhas text;
BEGIN
  SELECT string_agg(format('%s.%s', schema_nome, tabela), ', ')
    INTO falhas
    FROM plataforma.auditoria_rls()
   WHERE tem_coluna_tenant
     AND NOT (rls_ligado AND rls_forcado AND tem_politica);

  IF falhas IS NOT NULL THEN
    RAISE EXCEPTION
      'Tabelas com tenant_id sem RLS completa (ENABLE + FORCE + POLICY): %', falhas
      USING HINT = 'Ver ADR-0003. Toda tabela do data plane precisa das três coisas.';
  END IF;
END $$;

GRANT EXECUTE ON FUNCTION plataforma.auditoria_rls(), plataforma.exigir_rls()
  TO catalogo_app, catalogo_leitor;

-- ═══════════════════════════════════════════════════════════════════════════
--  tenant_atual() — a forma CORRETA de ler o contexto numa política de RLS.
--
--  Por que não usar current_setting('app.tenant_id', true)::uuid direto:
--  depois que uma GUC personalizada é setada uma vez na sessão, "limpá-la"
--  (fim de transação com set_config LOCAL) devolve STRING VAZIA, não NULL.
--  E ''::uuid não dá NULL — dá ERRO:
--        invalid input syntax for type uuid: ""
--  Ou seja: a requisição sem contexto explodiria com erro de tipo em vez de
--  simplesmente não ver nada. O NULLIF converte '' em NULL, a comparação
--  `tenant_id = NULL` vira NULL, e a política nega tudo. Falha fechada, limpa.
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION plataforma.tenant_atual() RETURNS uuid
LANGUAGE sql STABLE AS $$
  SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid;
$$;

GRANT EXECUTE ON FUNCTION plataforma.tenant_atual() TO catalogo_app, catalogo_leitor;
