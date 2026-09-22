# ADR-0002 — Usar .NET 10 (C#) no núcleo e TypeScript/React no front

- **Status:** proposto
- **Data:** 2026-09-22
- **Relacionados:** ADR-0001, ADR-0011, ADR-0012

## Contexto

A opção escolhida para a evolução foi migrar para **.NET ou Node/TypeScript**, com
**Azure** como alvo de infraestrutura. As duas opções cabem no hexágono do ADR-0001,
mas produzem produtos com custos de operação muito diferentes ao longo de cinco anos.

Restrições que pesam nesta escolha:

- O núcleo é **regra de negócio densa**: política de publicação, máquina de ciclo
  de vida, score ponderado, RBAC com escopo, segregação de função. É domínio de
  tipos, não de I/O.
- O alvo é **SaaS B2B multiempresa em Azure**, com exigências de isolamento,
  auditoria e contrato de API estável.
- A integridade entre empresas é requisito de segurança, não de conveniência: um
  `tenant_id` trocado por engano é incidente reportável.
- O front já tem direção definida — a experiência do time em React/JavaScript é
  um ativo, e a UI atual (Jinja + CSS puro, sem build step) não escala para um
  produto comercial.

## Decisão

**Núcleo em .NET 10 (LTS), C#.** Front em **TypeScript + React**.

### Runtime e versões

| Componente | Escolha | Observação |
|---|---|---|
| Runtime do núcleo | **.NET 10 (LTS)** | Lançado 11/11/2025, suporte até 14/11/2028. O .NET 8 e o 9 encerram suporte em novembro de 2026 — começar neles seria nascer em migração |
| Linguagem | C# 14, `nullable` e `TreatWarningsAsErrors` ligados em todo o `src/` | |
| API | ASP.NET Core Minimal API | Baixa cerimônia; o contrato vive no ADR-0009 |
| Persistência | **EF Core** para escrita/agregado + **Dapper** para consulta pesada | Ver ADR-0004 |
| Orquestração local | **.NET Aspire** | Sobe Postgres, Redis, Service Bus emulado e os serviços com um comando |
| Front | React + TypeScript + Vite | Hospedado em Azure Static Web Apps |
| Testes | xUnit, FluentAssertions, **Testcontainers**, **NetArchTest** | |

### Por que C# e não TypeScript no núcleo

1. **O sistema de tipos é a principal defesa.** O domínio é cheio de conceitos que
   pedem tipo nominal: `IdDeAtivo`, `IdDeEmpresa`, `Matricula`, `Criticidade`.
   Em C#, `record struct IdDeEmpresa(Guid Valor)` torna **impossível** passar um
   `IdDeAtivo` onde se espera um `IdDeEmpresa` — o compilador recusa. Em
   TypeScript isso depende de *branded types*, que são convenção e desaparecem em
   runtime. Dado que o erro que este produto não pode cometer é justamente
   confundir identificadores entre empresas, isso não é preferência estética.
2. **Tipos existem em produção.** TypeScript apaga os tipos na compilação; validação
   de fronteira exige Zod ou equivalente em todo limite. Em .NET, os tipos
   sobrevivem e a deserialização já os respeita.
3. **Cargas de longa duração.** Workers de outbox, vigilância de SLA, snapshot de
   indicadores e descoberta automática são processos com estado, concorrência real
   e paralelismo. `async`/`await` sobre thread pool com `Channel<T>` e
   `IHostedService` resolve isso de forma direta; em Node exige cluster ou
   worker_threads para não competir com o event loop.
4. **Azure é ecossistema de primeira classe.** Entra ID, Service Bus, Key Vault,
   Managed Identity e OpenTelemetry têm SDK, documentação e suporte
   dimensionados para .NET primeiro.
5. **Guardas de arquitetura.** NetArchTest/ArchUnitNET inspecionam o assembly e
   falham o build (ADR-0001). O equivalente em TypeScript (`dependency-cruiser`,
   regras de ESLint) atua sobre caminho de arquivo, não sobre dependência real, e
   é contornável com um import dinâmico.

### Onde TypeScript é a escolha certa — e é usado

- **SPA React** (`src/web/catalogo-web`): toda a UI.
- **Cliente de API gerado**: a partir do OpenAPI publicado, versionado no repo.
  O contrato só pode divergir se o gerador acusar (ADR-0009).
- **SDK público para clientes** da API, publicado no npm — é o que parceiros
  esperam consumir.

### Sobre a honestidade da escolha

O perfil técnico atual do projeto é Python e React, não C#. Isso é um custo real
de aprendizado e deve entrar no planejamento da Fase 1 com folga. O ponto a favor:
a transição Python → C# é bem menos áspera do que parece — o domínio do ADR-0001 é
majoritariamente lógica pura, e a suíte pytest existente vira a especificação a
ser satisfeita (ADR-0012). O que realmente se aprende é EF Core, DI e o modelo de
hospedagem; o resto é sintaxe.

Se o time decidir que esse custo não cabe no cronograma, a alternativa honesta
**não é** TypeScript no núcleo — é permanecer em Python com FastAPI e aplicar todo
o restante deste conjunto de ADRs, que é agnóstico de linguagem do 0003 ao 0012.
Essa opção foi descartada na escolha de estratégia, mas fica registrada.

## Consequências

### Positivas

- Erros de identificador entre empresas viram erro de compilação, não incidente.
- Uma stack para API, workers, jobs e CLI — sem dois runtimes para operar.
- `.NET Aspire` + `azd` reduz drasticamente o atrito de subir o ambiente local e
  de publicar em Container Apps (ADR-0011).
- O ciclo LTS de três anos dá previsibilidade de manutenção para um produto vendido
  a empresas.

### Negativas

- **Curva de aprendizado real** para um time com raiz em Python/JS.
- **Duas linguagens no repositório**, com duas toolchains, dois linters e dois
  pipelines de CI.
- **Nenhuma linha do código Python é reaproveitada.** Só as regras e os testes
  migram — conceitualmente, não por copiar e colar.
- Contêiner .NET é maior que um contêiner Node (mitigável com AOT nos workers, se
  o tempo de partida a frio incomodar).

### Neutras / a monitorar

- Mesma linguagem no front e no back tem valor de fluidez de equipe. Em times
  pequenos isso pesa. Abaixo de três pessoas em tempo integral, reabra esta
  decisão.
- `Catalogo.Dominio` deve permanecer compilável sem nenhum SDK de nuvem. Se um dia
  isso deixar de ser verdade, o ADR-0001 foi violado antes deste aqui.

## Alternativas consideradas

**Node.js + TypeScript (NestJS ou Fastify).** Menor curva para o time, uma
linguagem só, ecossistema enorme. Recusada pelos motivos 1–5 acima; o fator
decisivo foi o apagamento de tipos em runtime num produto cuja principal
invariante de segurança é a identidade de tenant.

**Java/Kotlin com Spring Boot ou Quarkus.** Excelente para SaaS corporativo
multi-tenant e com ótimo suporte a Postgres. Recusada: fora das opções escolhidas,
e em Azure o .NET tem integração superior sem perda de capacidade.

**Python + FastAPI.** Preservaria linguagem, parte da lógica e a suíte de testes
inteira. É a opção de menor risco de execução. Fora da estratégia escolhida, mas
registrada aqui como plano B legítimo caso a Fase 1 mostre que a curva de C# é
maior que o previsto.

**Go.** Ótimo para os workers e a camada de medição. Recusada como núcleo: modelar
agregado rico e invariante de domínio em Go é verboso e o ecossistema Azure é menos
completo. Pode voltar como escolha pontual para um serviço de medição de altíssimo
volume (ADR-0010), sem contaminar o núcleo.

## Revisitar quando

- O time estabilizar abaixo de três pessoas em tempo integral.
- O tempo de partida a frio dos workers virar custo mensurável em Container Apps
  (avaliar Native AOT antes de trocar de linguagem).
- Novembro de 2028, quando o .NET 10 sair de suporte — planejar o salto para o
  próximo LTS com pelo menos dois trimestres de antecedência.
