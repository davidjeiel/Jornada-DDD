<#
.SYNOPSIS
  Ambiente local do Catálogo DDD sobre Podman (ADR-0013).

.DESCRIPTION
  O objetivo é que "clone → rodando" caiba em um comando, como hoje acontece
  com `python run.py`. Se este script crescer muito, a decisão do ADR-0013
  está falhando.

.EXAMPLE
  .\local\dev.ps1 subir        # sobe as dependências e espera ficarem saudáveis
  .\local\dev.ps1 testar-isolamento   # prova que o isolamento entre empresas funciona
  .\local\dev.ps1 limpar       # remove containers órfãos de Testcontainers
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('verificar','subir','descer','recriar','testar-isolamento','limpar','estado','logs')]
    [string]$Comando = 'estado',

    [Parameter(Position = 1)]
    [string]$Alvo
)

$ErrorActionPreference = 'Stop'
$Raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$Compose = Join-Path $Raiz 'compose.yaml'

# Chave PÚBLICA e fixa do emulador do Cosmos (documentada pela Microsoft).
$COSMOS_LOCAL = 'AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='

function Info($m) { Write-Host "  $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "  OK   $m" -ForegroundColor Green }
function Aviso($m){ Write-Host "  !    $m" -ForegroundColor Yellow }
function Erro($m) { Write-Host "  X    $m" -ForegroundColor Red }

function Test-Ferramentas {
    Info 'Verificando pré-requisitos...'
    $falhou = $false

    if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
        Erro 'podman não encontrado no PATH.'
        Aviso 'Instale o Podman Desktop e reabra o terminal.'
        $falhou = $true
    } else {
        Ok "podman $((podman --version) -replace '[^\d\.]','')"
    }

    $maquina = podman machine list --format '{{.Name}} {{.Running}}' 2>$null
    if (-not $maquina) {
        Aviso 'Nenhuma podman machine. Crie com:'
        Aviso '  podman machine init --cpus 4 --memory 8192 --disk-size 60'
        Aviso '  podman machine start'
        $falhou = $true
    } elseif ($maquina -notmatch 'true') {
        Aviso 'A podman machine existe mas está parada: podman machine start'
        $falhou = $true
    } else {
        Ok 'podman machine rodando'
    }

    # Docker Compatibility: necessária para Testcontainers (ADR-0013 §7)
    if ($env:DOCKER_HOST) {
        Ok "DOCKER_HOST = $env:DOCKER_HOST"
    } else {
        Aviso 'DOCKER_HOST não definido — Testcontainers pode não achar o Podman.'
        Aviso 'Ligue "Docker Compatibility" no Podman Desktop, ou defina:'
        Aviso '  $env:DOCKER_HOST = "npipe:////./pipe/docker_engine"'
    }
    if ($env:TESTCONTAINERS_RYUK_DISABLED -ne 'true') {
        Aviso 'TESTCONTAINERS_RYUK_DISABLED não é "true" — Ryuk falha em rootless.'
    }

    # Memória: o MSSQL do emulador de Service Bus sozinho quer ~2 GB
    $memGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
    if ($memGB -lt 16) { Aviso "RAM total ${memGB} GB. 16 GB é o piso prático." }
    else { Ok "RAM ${memGB} GB" }

    if (-not (Test-Path (Join-Path $Raiz '.env'))) {
        Aviso 'local\.env não existe. Criando a partir de .env.exemplo...'
        Copy-Item (Join-Path $Raiz '.env.exemplo') (Join-Path $Raiz '.env')
        Ok 'local\.env criado'
    }
    return -not $falhou
}

function Start-Ambiente {
    if (-not (Test-Ferramentas)) { throw 'Pré-requisitos não atendidos.' }

    Info 'Subindo containers...'
    podman compose -f $Compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'podman compose falhou.' }

    Info 'Aguardando o emulador do Cosmos DB...'
    $limite = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Seconds 2
        podman exec catalogo-cosmos curl -fsS http://127.0.0.1:8080/ready 2>&1 | Out-Null
        $pronto = $LASTEXITCODE -eq 0
    } while (-not $pronto -and (Get-Date) -lt $limite)
    if (-not $pronto) { throw 'Cosmos DB não ficou pronto em 2 min. Veja: .\dev.ps1 logs cosmos' }
    Ok 'Cosmos DB pronto (porta 8081)'

    Aviso 'Service Bus leva ~60 s a mais (o MSSQL precisa subir antes).'
    Write-Host ''
    Show-Estado
    Write-Host ''
    Info 'Próximos passos:'
    Write-Host '    dotnet run --project src/hosts/Catalogo.AppHost'
    Write-Host '    .\local\dev.ps1 testar-isolamento   # confirme o isolamento antes de codar'
}

function Show-Estado {
    Write-Host ''
    Write-Host '  SERVIÇO            ENDEREÇO                        PAPEL' -ForegroundColor DarkGray
    Write-Host '  ─────────────────  ──────────────────────────────  ─────────────────────────' -ForegroundColor DarkGray
    Write-Host '  Cosmos DB          https://localhost:8081          <- a APLICAÇÃO usa esta'
    Write-Host '  Data Explorer      http://localhost:1234           consultar documentos'
    Write-Host '  Valkey (Redis)     localhost:6379                  cache, rate limit'
    Write-Host '  Azurite (Blob)     localhost:10000                 evidências, exports'
    Write-Host '  Service Bus        localhost:5672 / :5300          outbox (AMQP)'
    Write-Host '  Keycloak           http://localhost:8080           OIDC (no lugar do Entra)'
    Write-Host '  Painel OTel        http://localhost:18888          traces e logs'
    Write-Host ''
    podman compose -f $Compose ps --format 'table {{.Names}}\t{{.Status}}' 2>$null
}

function Test-Isolamento {
    # Sem RLS, o isolamento entre empresas é imposto pelo adaptador (ADR-0016 §4).
    # A prova é a suíte de contrato contra o emulador: toda consulta, em toda
    # porta, conferida com duas empresas.
    Info 'Verificando o isolamento entre empresas no Cosmos DB (ADR-0016)...'
    Write-Host ''
    $env:JORNADA_TESTES_COSMOS = $COSMOS_LOCAL
    $env:JORNADA_TESTES_COSMOS_EMULADOR = 'true'
    dotnet run --project (Join-Path $Raiz '..\tests\Jornada.Persistencia.Testes') -- Cosmos
    if ($LASTEXITCODE -ne 0) { Erro 'Isolamento NÃO confirmado. NÃO prossiga sem entender.'; exit 1 }
}

function Clear-Orfaos {
    # Ryuk fica desligado em Podman rootless (ADR-0013 §7), então os containers
    # de teste não são recolhidos sozinhos. Isto é higiene de rotina, não conserto.
    Info 'Removendo containers órfãos de Testcontainers...'
    $antes = (podman ps -aq | Measure-Object).Count
    podman ps -aq --filter 'label=org.testcontainers=true' | ForEach-Object { podman rm -f $_ 2>$null } | Out-Null
    podman container prune -f | Out-Null
    podman volume prune -f  | Out-Null
    $depois = (podman ps -aq | Measure-Object).Count
    Ok "removidos: $($antes - $depois) container(es)"
}

switch ($Comando) {
    'verificar'  { Test-Ferramentas | Out-Null }
    'subir'      { Start-Ambiente }
    'descer'     { podman compose -f $Compose down }
    'recriar'    { podman compose -f $Compose down -v; Start-Ambiente }
    'estado'     { Show-Estado }
    'testar-isolamento' { Test-Isolamento }
    'limpar'     { Clear-Orfaos }
    'logs'       { if ($Alvo) { podman compose -f $Compose logs -f $Alvo } else { podman compose -f $Compose logs --tail 50 } }
}
