-- ═══════════════════════════════════════════════════════════════════════════
--  Esquema do data plane — a parte que o EF Core NÃO gera.
--
--  As migrations do EF criam tabelas, colunas e índices. RLS, políticas e
--  GRANT por schema não saem de lá: precisam ser aplicados explicitamente,
--  em migration própria (`migrationBuilder.Sql(...)`) ou por este arquivo.
--
--  É por isso que este arquivo existe e por que ele não é opcional: sem ele,
--  o ADR-0003 não está implementado — está só documentado.
-- ═══════════════════════════════════════════════════════════════════════════

-- ─────────────────────────────────────────────────────── contexto de tenant
-- NÃO use current_setting('app.tenant_id', true)::uuid direto numa política.
-- Depois que a GUC é usada uma vez na sessão, "limpá-la" (fim de transação com
-- set_config LOCAL) devolve STRING VAZIA, não NULL — e ''::uuid é ERRO de
-- sintaxe, não NULL. A requisição sem contexto explodiria com erro de tipo em
-- vez de simplesmente não ver nada. O NULLIF garante falha fechada limpa.
CREATE SCHEMA IF NOT EXISTS plataforma;

CREATE OR REPLACE FUNCTION plataforma.tenant_atual() RETURNS uuid
LANGUAGE sql STABLE AS $$
  SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid;
$$;

-- ┌─────────────────────────────────────────────────────────────────────────┐
-- │ A ORDEM DESTE ARQUIVO IMPORTA.                                          │
-- │                                                                         │
-- │ Toda tabela com tenant_id precisa existir ANTES do bloco que aplica RLS.│
-- │ Na primeira versão a outbox era criada depois — e ficava sem política,  │
-- │ guardando payload de evento de todas as empresas no mesmo lugar.        │
-- │ Quem pegou foi a própria plataforma.exigir_rls(), que é o motivo de ela │
-- │ existir: o erro real não é esquecer a política, é criar a tabela depois.│
-- │                                                                         │
-- │ Ordem: 1) funções  2) TABELAS  3) RLS  4) guarda                        │
-- └─────────────────────────────────────────────────────────────────────────┘

-- ────────────────────────────────────────────────────────────────── outbox
CREATE TABLE IF NOT EXISTS catalogo.mensagem_de_saida (
  id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id           uuid        NOT NULL,
  tipo                text        NOT NULL,
  payload             jsonb       NOT NULL,
  chave_idempotencia  text        NOT NULL,
  correlacao          uuid        NOT NULL,
  ocorrido_em         timestamptz NOT NULL,
  despachado_em       timestamptz,
  tentativas          int         NOT NULL DEFAULT 0,
  proxima_tentativa   timestamptz,
  ultimo_erro         text
);

CREATE INDEX IF NOT EXISTS ix_outbox_pendente
  ON catalogo.mensagem_de_saida (proxima_tentativa)
  WHERE despachado_em IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_idem
  ON catalogo.mensagem_de_saida (tenant_id, chave_idempotencia);

-- Consulta do despachante. FOR UPDATE SKIP LOCKED é a peça que faz N
-- instâncias conviverem sem lock global nem eleição de líder (ADR-0008 §3):
--
--   SELECT * FROM catalogo.mensagem_de_saida
--    WHERE despachado_em IS NULL AND proxima_tentativa <= now()
--    ORDER BY id LIMIT 100
--      FOR UPDATE SKIP LOCKED;

-- ─────────────────────────────────────────── RLS em toda tabela do data plane
DO $$
DECLARE t record;
BEGIN
  FOR t IN
    SELECT n.nspname AS esquema, c.relname AS tabela
      FROM pg_class c
      JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE c.relkind = 'r'
       AND n.nspname IN ('catalogo','governanca','notificacao','descoberta')
       AND EXISTS (SELECT 1 FROM pg_attribute a
                    WHERE a.attrelid = c.oid AND a.attname = 'tenant_id'
                      AND NOT a.attisdropped)
  LOOP
    EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', t.esquema, t.tabela);
    -- FORCE, e não só ENABLE: sem ele o DONO da tabela ignora a política —
    -- e é comum a aplicação conectar como dono.
    EXECUTE format('ALTER TABLE %I.%I FORCE  ROW LEVEL SECURITY', t.esquema, t.tabela);

    EXECUTE format('DROP POLICY IF EXISTS isolamento_tenant ON %I.%I', t.esquema, t.tabela);
    -- WITH CHECK além de USING: sem ele dá para INSERIR linha com tenant_id
    -- alheio, mesmo sem conseguir lê-la depois.
    EXECUTE format(
      'CREATE POLICY isolamento_tenant ON %I.%I
         USING      (tenant_id = plataforma.tenant_atual())
         WITH CHECK (tenant_id = plataforma.tenant_atual())',
      t.esquema, t.tabela);
  END LOOP;
END $$;

-- ───────────────────────────────── a guarda contra a tabela nova sem política
CREATE OR REPLACE FUNCTION plataforma.exigir_rls() RETURNS void
LANGUAGE plpgsql STABLE AS $$
DECLARE falhas text;
BEGIN
  SELECT string_agg(format('%s.%s', n.nspname, c.relname), ', ') INTO falhas
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
   WHERE c.relkind = 'r'
     AND n.nspname IN ('catalogo','governanca','notificacao','descoberta')
     AND EXISTS (SELECT 1 FROM pg_attribute a
                  WHERE a.attrelid = c.oid AND a.attname = 'tenant_id' AND NOT a.attisdropped)
     AND NOT (c.relrowsecurity AND c.relforcerowsecurity
              AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid));

  IF falhas IS NOT NULL THEN
    RAISE EXCEPTION 'Tabelas com tenant_id sem RLS completa: %', falhas
      USING HINT = 'Ver ADR-0003. Precisa de ENABLE + FORCE + POLICY.';
  END IF;
END $$;
