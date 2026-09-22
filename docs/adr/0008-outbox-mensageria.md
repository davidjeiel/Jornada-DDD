# ADR-0008 — Manter o padrão outbox transacional, com Azure Service Bus como transporte

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0004, ADR-0006

## Contexto

O projeto já usa outbox, e usa bem. De `CLAUDE.md`:

> `notificacoes.py` — padrão outbox: grava na mesma transação do fato gerador,
> `notificar`/`vigiar-sla` despacham depois (idempotente).

E no schema:

```sql
chave_unica TEXT,   -- evita alerta repetido do mesmo fato no mesmo dia
enviado_em  TEXT,   -- nulo = ainda na outbox
CREATE UNIQUE INDEX ux_notif_chave ON notificacao(chave_unica) WHERE chave_unica IS NOT NULL;
```

Chave de idempotência, marcador de pendência, despacho assíncrono — está tudo lá.
O que muda ao virar SaaS:

- o consumidor deixa de ser só a própria tela (`/notificacoes`) e passa a incluir
  e-mail, Teams, webhook do cliente e outros módulos;
- com módulos caminhando para extração (ADR-0006), a comunicação entre eles precisa
  de um transporte que sobreviva à separação de processo;
- múltiplas instâncias da aplicação significam **múltiplos despachantes concorrendo
  pela mesma linha da outbox** — problema que não existe com uma instância só;
- o evento vira também insumo de medição para cobrança (ADR-0010) e de reindexação
  da busca (ADR-0004), então perder evento passa a ter custo financeiro.

O erro clássico a evitar é conhecido: publicar na fila dentro do mesmo bloco da
escrita no banco. Se o `COMMIT` falha depois da publicação, o mundo exterior
acredita em algo que não aconteceu; se a publicação falha depois do `COMMIT`, o
fato aconteceu e ninguém soube. Não existe forma de acertar isso sem outbox (ou
transação distribuída, que é pior).

## Decisão

### 1. Eventos de domínio são cidadãos de primeira classe

O agregado acumula eventos; a unidade de trabalho os grava na outbox **na mesma
transação** da mudança de estado.

```csharp
// Catalogo.Dominio
public sealed class Ativo : RaizDeAgregado
{
    public void Submeter(Motivo motivo, Ator ator, IRelogio relogio)
    {
        // ... invariantes ...
        Registrar(new AtivoSubmetido(Id, RevisaoAtual, ator.Id, relogio.Agora));
    }
}
```

```csharp
// Catalogo.Persistencia.Postgres — um ponto só, impossível de esquecer
public override async Task<int> SaveChangesAsync(CancellationToken ct)
{
    var eventos = ChangeTracker.Entries<RaizDeAgregado>()
                               .SelectMany(e => e.Entity.DrenarEventos());
    await Outbox.AddRangeAsync(eventos.Select(MensagemDeSaida.De), ct);
    return await base.SaveChangesAsync(ct);   // uma transação, dois efeitos
}
```

Eventos iniciais, derivados do fluxo que já existe em `servicos.py`:
`AtivoCadastrado`, `AtivoSubmetido`, `ValidacaoAtribuida`, `ValidacaoDecidida`,
`RevisaoPublicada`, `AtivoDescontinuado`, `RelacaoCriada`, `ScoreRecalculado`,
`SlaEstourado`, `DescobertaImportada`.

### 2. Tabela de outbox

```sql
CREATE TABLE mensagem_de_saida (
  id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id        UUID        NOT NULL,
  tipo             TEXT        NOT NULL,
  payload          JSONB       NOT NULL,
  chave_idempotencia TEXT      NOT NULL,       -- generaliza o `chave_unica` atual
  correlacao       UUID        NOT NULL,       -- generaliza `auditoria_evento.correlacao`
  ocorrido_em      TIMESTAMPTZ NOT NULL,
  despachado_em    TIMESTAMPTZ,                -- nulo = pendente (era `enviado_em`)
  tentativas       INT         NOT NULL DEFAULT 0,
  proxima_tentativa TIMESTAMPTZ,
  ultimo_erro      TEXT
);
CREATE INDEX ix_outbox_pendente ON mensagem_de_saida (proxima_tentativa)
  WHERE despachado_em IS NULL;
CREATE UNIQUE INDEX ux_outbox_idem ON mensagem_de_saida (tenant_id, chave_idempotencia);
```

A `correlacao` merece nota: `auditoria_evento` já tem essa coluna. Promovê-la a
`traceparent` do OpenTelemetry faz a trilha de auditoria de negócio e o rastro
distribuído convergirem — dá para pular de um evento auditado para o trace da
requisição que o gerou.

### 3. Despacho concorrente e seguro

O ponto que muda com múltiplas instâncias:

```sql
SELECT * FROM mensagem_de_saida
 WHERE despachado_em IS NULL AND proxima_tentativa <= now()
 ORDER BY id
 LIMIT 100
 FOR UPDATE SKIP LOCKED;     -- ← a peça que faz N despachantes conviverem
```

`FOR UPDATE SKIP LOCKED` garante que duas instâncias não peguem a mesma mensagem,
sem lock global e sem eleição de líder. Uma linha de SQL resolve o que costuma virar
um componente de coordenação.

**Garantia: pelo menos uma vez.** Portanto **todo consumidor é idempotente**, com
tabela de mensagens processadas por `(consumidor, chave_idempotencia)`. Exatamente
o raciocínio do `ux_notif_chave` atual, generalizado.

Backoff exponencial com jitter; após N tentativas, a mensagem vai para uma tabela
de "veneno" com alerta — nunca é descartada em silêncio.

### 4. Transporte

| Destino | Como |
|---|---|
| Entre módulos no mesmo processo (hoje) | Barramento em memória, consumido **após o commit** |
| Entre serviços (após extração, ADR-0006) | **Azure Service Bus** — tópico por tipo de evento, assinatura por consumidor |
| Webhook para o cliente | Serviço de entrega com assinatura HMAC, retry e DLQ visível ao cliente |
| Medição (ADR-0010) | **Event Hubs** — volume alto, ordenação por partição, retenção curta |

A troca de barramento em memória por Service Bus é implementação da porta
`IPublicadorDeEventos`. O caso de uso não muda uma linha.

Service Bus, e não Storage Queues: tópico com múltiplas assinaturas, DLQ,
sessões (ordenação por `tenant_id` quando necessário) e deduplicação. Event Hubs
para medição porque o perfil ali é *streaming* de alto volume, não mensageria
transacional.

### 5. Consumidores iniciais

| Consumidor | Reage a | Efeito |
|---|---|---|
| Notificações | `AtivoSubmetido`, `ValidacaoDecidida`, `SlaEstourado` | E-mail / Teams / no app |
| Índice de busca | `RevisaoPublicada`, `AtivoDescontinuado` | Atualiza o AI Search |
| Qualidade | `AtivoCadastrado`, `RelacaoCriada`, `RevisaoPublicada` | Recalcula score |
| Indicadores | todos | Alimenta `snapshot_indicador` |
| Medição | eventos faturáveis | Contador de uso (ADR-0010) |
| Webhook | conforme assinatura do cliente | Entrega externa |

Repare que `qualidade.registrar`, hoje chamado explicitamente pelo caso de uso, vira
reação a evento. Isso remove uma dependência direta entre módulos.

## Consequências

### Positivas

- **Nunca há divergência** entre o que o banco registrou e o que o mundo soube.
- Sobrevive a reinício, deploy e falha de rede — a propriedade que o
  `notificacoes.py` já tinha, agora preservada em escala.
- A extração de serviços (ADR-0006) fica barata porque o contrato entre módulos já
  é assíncrono e explícito.
- Reprocessamento é possível: reindexar a busca do zero é reprocessar eventos.

### Negativas

- **Consistência eventual.** Publicar um ativo e vê-lo na busca não é instantâneo.
  A UI precisa ser honesta sobre isso ("indexando…"), e não fingir sincronismo.
- **Idempotência é obrigação de todo consumidor**, e é o tipo de coisa que se
  esquece no consumidor número sete. Exige teste padrão: entregar duas vezes e
  verificar um efeito só.
- **Latência de polling.** Com intervalo de 1 s, o p99 de notificação ganha ~1 s.
  Aceitável aqui; se não fosse, `LISTEN/NOTIFY` do Postgres reduziria.
- A outbox cresce e precisa de expurgo — `pg_partman` por data, com retenção
  definida por política.

### Neutras / a monitorar

- Profundidade da fila e idade da mensagem mais antiga pendente são as métricas de
  saúde mais úteis do sistema. Alerta em ambas.
- Versionamento de evento: adicionar campo é compatível; remover ou renomear não é.
  Regra — **só adicione**; para mudar, publique `EventoV2` e mantenha os dois até o
  último consumidor migrar.

## Alternativas consideradas

**Publicar direto na fila dentro do caso de uso.** Simples e sem tabela extra.
Recusada pelo problema de consistência descrito no Contexto — é exatamente o erro
que o projeto já evitou.

**Change Data Capture (Debezium sobre o WAL).** Elegante: nenhum código de outbox,
o log de transação é a fonte. Recusada por ora: acopla o contrato de eventos ao
schema físico das tabelas (renomear coluna vira quebra de contrato) e adiciona
Kafka Connect à operação. Reavaliar se o número de eventos por segundo justificar.

**Transação distribuída (2PC) entre banco e broker.** Recusada: Service Bus não
oferece, e mesmo onde existe, o custo de disponibilidade é alto demais.

**Event sourcing completo** (o log de eventos como fonte da verdade). Tentador,
porque `revisao_catalogo` já é praticamente um log de eventos do agregado `Ativo`.
Recusada: mudaria o modelo de leitura inteiro e a complexidade de projeções e
versionamento supera o ganho. O estado atual — estado corrente + revisões imutáveis
com hash — já entrega auditabilidade, que é o que o negócio pede.

## Revisitar quando

- A latência de polling aparecer como reclamação de usuário (→ `LISTEN/NOTIFY`).
- Passar de ~1.000 eventos/s sustentados (→ reavaliar CDC ou Event Hubs como
  transporte principal).
- Um consumidor externo exigir ordenação estrita global (→ sessões do Service Bus,
  com o custo de vazão que isso implica).
