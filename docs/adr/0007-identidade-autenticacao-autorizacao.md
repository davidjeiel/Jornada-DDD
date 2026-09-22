# ADR-0007 — Entra External ID para identidade; autorização de negócio no domínio, em um ponto só

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0001, ADR-0003, ADR-0009

## Contexto

O projeto já acertou o desenho conceitual da autorização e deixou a porta aberta
para SSO. Em `acesso.py`:

- **ponto único de decisão**: `pode(con, pessoa, acao, item)`;
- **papéis com escopo** resolvidos por `papeis_efetivos`, onde `global` sempre conta
  e `dominio`/`squad` só contam se o item pertencer àquele escopo;
- **segregação de função**: `pode_decidir` cruza o papel exigido pela etapa
  (`PAPEL_POR_ETAPA`) com ownership, e impede que quem submeteu decida;
- **fluxo de concessão auditado**: `solicitacao_acesso` não vira papel sozinho —
  alguém decide, e a decisão fica registrada com autor, data e resposta;
- **defesa na rota**: o decorador `@exige(acao)` recusa mesmo com o botão escondido.

E em `schema.sql`, a preparação para SSO já existe:

```sql
identidade_externa TEXT,   -- 'sub' do provedor quando houver SSO
origem_identidade  TEXT NOT NULL DEFAULT 'local'  -- local|oidc|ldap
```

O que falta para o produto multiempresa:

1. **Autenticação de verdade.** Hoje é login local com matrícula.
2. **Federação por empresa.** Nenhuma empresa cliente vai criar usuários manualmente
   num sistema de fornecedor — ela quer entrar com o Entra ID / Okta / Google
   Workspace dela. Isso é requisito de venda, não de conforto.
3. **Empresa no token.** Sem o claim de empresa, o ADR-0003 não se sustenta.
4. **Identidade para máquinas.** A API pública (ADR-0009) é consumida por CI/CD e
   integrações, não por humanos.

## Decisão

### 1. Autenticação: Microsoft Entra External ID (tenant externo)

Escolha para CIAM B2B. Nota relevante para o planejamento: o **Azure AD B2C** está
em modo de manutenção — sem venda de novos tenants desde 01/05/2025, com suporte
até maio de 2030. Não é opção para um produto novo.

O Entra External ID atende os requisitos essenciais:

- **Federação por empresa**: cada cliente conecta seu próprio IdP (OIDC ou SAML),
  e o mapeamento domínio de e-mail → empresa faz o roteamento na tela de login.
- Protocolo OIDC/OAuth 2.0 padrão, `client_credentials` para máquinas.
- Passkeys/FIDO2 e MFA.
- Integração nativa com APIM (ADR-0009) e com o SDK .NET (ADR-0002).

**Limitações conhecidas, a validar antes de fechar contrato com o primeiro cliente
grande:** a customização de marca é em nível de tenant (não por aplicação), os
fluxos customizados são mais simples que o Identity Experience Framework do B2C, e
o leque de MFA é mais restrito. Se algum cliente exigir marca própria completa na
tela de login ou fluxo de cadastro muito específico, **Keycloak autogerenciado** é
a alternativa — mais controle e custo previsível, ao preço de operar o serviço.

A porta `IProvedorDeIdentidade` (ADR-0001) mantém essa troca viável: o domínio não
sabe quem autentica.

### 2. O token carrega empresa; o servidor nunca confia nele sozinho

Claims esperados: `sub` (identidade externa — exatamente o campo que o schema já
previu), `tid_empresa`, `email`, `unidades` (opcional, quando pequeno).

**Papéis não vão no token.** Motivo: papel muda por concessão e revogação dentro do
produto (`atribuicao_papel`, `solicitacao_acesso`) e teria latência de propagação
igual ao tempo de vida do token — uma revogação demoraria até uma hora para valer.
Papel é resolvido no servidor, a cada requisição, com cache curto em Redis
invalidado por evento de concessão/revogação.

E `tid_empresa` é **validado contra o control plane**, não aceito de olhos fechados:
a empresa existe, está ativa, e a pessoa está vinculada a ela.

### 3. A autorização de negócio continua no domínio

O que muda de `acesso.py` para o alvo, e o que não muda:

| Hoje | Alvo | Muda? |
|---|---|---|
| `pode(con, pessoa, acao, item)` | `PoliticaDeAutorizacao.Pode(Ator, Acao, Alvo)` — **puro, sem `con`** | Forma, não conceito |
| `papeis_efetivos(con, id_pessoa, id_item)` | porta `IProvedorDePapeis`, com cache | Forma |
| Escopos `global \| dominio \| squad` | `empresa \| unidade \| dominio \| squad` (ADR-0003) | Ganha um nível |
| `pode_decidir` com segregação de função | Invariante de domínio, testada isoladamente | Não |
| `@exige(acao)` na rota | Filtro de endpoint que delega à política | Não |
| `BLOCOS_POR_PAPEL` a partir de `tipos.blocos()` | Lê do metamodelo (ADR-0005) | Fonte do dado |

A `PoliticaDeAutorizacao` sendo pura é o ganho concreto: hoje testar autorização
exige banco; no alvo, é uma tabela de casos `(papéis, escopo, ação, alvo) → bool`,
com centenas de combinações rodando em milissegundos. A matriz real de hoje já é de
**6 papéis × 4 escopos × 11 ações**, cruzada ainda com `BLOCOS_POR_PAPEL` — isso é
a diferença entre cobertura real e cobertura de fachada.

### 4. Três camadas de defesa, propositalmente redundantes

```
1. Gateway (APIM)  — token válido? assinatura ok? cota do plano ok?
2. Aplicação       — PoliticaDeAutorizacao: este ator pode esta ação neste alvo?
3. Banco (RLS)     — o dado pertence a esta empresa?
```

Cada camada responde uma pergunta diferente e nenhuma substitui a outra. O CLAUDE.md
atual já ensina isto — *"a rota recusa mesmo que o botão esteja escondido na tela"* —
e o princípio apenas se estende para baixo.

### 5. Identidade de máquina

Integrações usam `client_credentials`, com credencial emitida **por empresa** e
escopo mínimo (`catalogo.leitura`, `catalogo.escrita`, `descoberta.executar`).
Uma credencial de máquina tem `tid_empresa` fixo e nunca pode ser reapontada.
Rotação obrigatória, com dois segredos válidos durante a janela de troca.

### 6. `pessoa` × empresa é N:N

Uma pessoa física (um consultor, um auditor) pode atender mais de uma empresa. A
identidade é global (um `sub` no IdP); o **vínculo** é por tenant, com papéis
próprios em cada uma. Trocar de empresa na sessão é uma ação explícita, auditada, e
emite novo contexto — nunca um parâmetro de query.

### 7. Auditoria e LGPD

`auditoria_evento` continua, agora com `tenant_id` e com encadeamento de hash
(cada evento carrega o hash do anterior), tornando a trilha *tamper-evident* — a
mesma ideia que `revisao_catalogo.payload_hash` já aplica às revisões.

Tensão a resolver explicitamente: **direito ao esquecimento × trilha imutável**. A
decisão é pseudonimizar o ator (substituir identificação pessoal por um
identificador opaco cuja tabela de correspondência é apagável) e **preservar o
fato**. Apaga-se quem, mantém-se o quê — que é o que a governança exige e o que a
LGPD permite mediante base legal de cumprimento de obrigação.

## Consequências

### Positivas

- Zero senha armazenada pelo produto.
- Federação com o IdP do cliente remove o maior obstáculo de venda B2B.
- A regra de autorização — a parte que realmente é do negócio — fica testável sem
  infraestrutura.
- Revogação de papel tem efeito em segundos, não no vencimento do token.

### Negativas

- **Dependência de serviço gerenciado** no caminho crítico de login. Precisa de
  página de status e comunicação de incidente.
- **Custo por usuário ativo mensal** cresce com a base (há faixa gratuita
  generosa, mas a curva existe).
- **Onboarding de cliente fica mais longo**: configurar federação envolve o time de
  identidade do cliente, o que costuma levar semanas.
- Cache de papéis introduz janela de inconsistência; exige invalidação por evento,
  não só TTL.

### Neutras / a monitorar

- `solicitacao_acesso` continua fazendo sentido mesmo com SSO: autenticar (o IdP
  resolve) é diferente de autorizar (quem concede papel é a governança do cliente).
  Não elimine esse fluxo ao adotar SSO — ele é o que mantém a segregação de função.
- Avaliar ReBAC (relationship-based, estilo Zanzibar/OpenFGA) se a matriz de
  permissões crescer muito. Hoje RBAC escopado basta e é mais simples de auditar.

## Alternativas consideradas

**Auth0 / Okta CIAM.** Excelente experiência de desenvolvimento e federação madura.
Recusada por ora: custo cresce rápido por MAU e o alvo é Azure, onde o Entra já
está integrado a APIM, Key Vault e Managed Identity.

**Keycloak autogerenciado.** Controle total, sem custo por usuário, customização
ilimitada de marca e fluxo. Recusada como padrão por exigir operação (HA, upgrade,
backup do realm) que o time não tem folga para assumir agora. **É a alternativa
designada** se a limitação de marca por aplicação bloquear uma venda.

**Identidade própria (continuar com `pessoa.login` + senha).** Recusada: guardar
senha é passivo de segurança sem nenhum ganho, e inviabiliza federação.

**Papéis dentro do token (JWT com claims de papel).** Menos consultas por
requisição. Recusada: revogação lenta e token grande. O cache em Redis dá o mesmo
desempenho sem o problema.

## Revisitar quando

- Um cliente exigir marca própria completa na tela de login (→ Keycloak).
- A matriz de permissões passar de ~10 papéis ou surgir necessidade de permissão
  por instância de recurso (→ avaliar OpenFGA).
- O custo de MAU do Entra passar de ~3% da receita recorrente.
