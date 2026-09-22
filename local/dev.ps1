<#
.SYNOPSIS
  Ambiente local do Catálogo DDD sobre Podman (ADR-0013).

.DESCRIPTION
  O objetivo é que "clone → rodando" caiba em um comando, como hoje acontece
  com `python run.py`. Se este script crescer muito, a decisão do ADR-0013
  está falhando.

.EXAMPLE
  .\local\dev.ps1 subir        # sobe as dependências e espera ficarem saudáveis
  .\local\dev.ps1 testar-rls   # prova que o isolamento entre empresas funciona
  .\local\dev.ps1 limpar       # remove containers órfãos de Testcontainers
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('verificar','subir','descer','recriar','psql','pool','testar-rls','limpar','estado','logs')]
    [string]$Comando = 'estado',

    [Parameter(Position = 1)]
    [string]$Alvo
)

$ErrorActionPreference = 'Stop'
$Raiz = Split-Path -Parent $MyInvocation.MyCommand.Path
$Compose = Join-Path $Raiz 'compose.yaml'

$EMPRESA_A = '11111111-1111-1111-1111-111111111111'
$EMPRESA_B = '22222222-2222-2222-2222-222222222222'

function Info($m) { Write-Host "  $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "  OK   $m" -ForegroundColor Green }
function Aviso($m){ Write-Host "  !    $m" -ForegroundColor Yellow }
function Erro($m) { Write-Host "  X    $m" -ForegroundColor Red }

function Invoke-Psql([string]$Sql, [string]$Usuario = 'catalogo_app', [int]$Porta = 6432) {
    # Roda psql DE DENTRO do container do Postgres: não exige cliente no Windows.
    # O host 'pgbouncer'/'postgres' resolve pela rede do compose.
    $alvoHost = if ($Porta -eq 6432) { 'pgbouncer' } else { 'postgres' }
    $senha = switch ($Usuario) {
        'catalogo_app'      { 'dev_app' }
        'catalogo_migrador' { 'dev_migrador' }
        default             { 'dev_leitor' }
    }
    $bruto = podman exec -e PGPASSWORD=$senha catalogo-postgres `
        psql -h $alvoHost -p $Porta -U $Usuario -d catalogo -tA -c $Sql 2>&1

    # psql imprime os rótulos de comando (BEGIN, COMMIT, SET, "UPDATE 0"...)
    # junto com os resultados, mesmo com -tA. Sem filtrar, o último valor seria
    # "COMMIT" em vez do número — e a verificação compararia lixo.
    return ($bruto | Where-Object {
        $_ -and $_ -notmatch '^(BEGIN|COMMIT|ROLLBACK|SET|INSERT|UPDATE|DELETE|SELECT \d)'
    })
}

function Get-Valor($Saida) { if ($Saida) { ([string[]]$Saida)[-1].Trim() } else { '' } }

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

    Info 'Aguardando Postgres...'
    $limite = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Seconds 2
        podman exec catalogo-postgres pg_isready -U postgres -d catalogo 2>&1 | Out-Null
        $pronto = $LASTEXITCODE -eq 0
    } while (-not $pronto -and (Get-Date) -lt $limite)
    if (-not $pronto) { throw 'Postgres não ficou pronto em 2 min. Veja: .\dev.ps1 logs postgres' }
    Ok 'Postgres pronto'

    Info 'Aguardando PgBouncer...'
    $limite = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Seconds 2
        $r = Invoke-Psql 'SELECT 1' 2>&1
        $pronto = $r -match '1'
    } while (-not $pronto -and (Get-Date) -lt $limite)
    if ($pronto) { Ok 'PgBouncer pronto (porta 6432)' } else { Aviso 'PgBouncer não respondeu; veja os logs.' }

    Aviso 'Service Bus leva ~60 s a mais (o MSSQL precisa subir antes).'
    Write-Host ''
    Show-Estado
    Write-Host ''
    Info 'Próximos passos:'
    Write-Host '    dotnet run --project src/hosts/Catalogo.AppHost'
    Write-Host '    .\local\dev.ps1 testar-rls     # confirme o isolamento antes de codar'
}

function Show-Estado {
    Write-Host ''
    Write-Host '  SERVIÇO            ENDEREÇO                        PAPEL' -ForegroundColor DarkGray
    Write-Host '  ─────────────────  ──────────────────────────────  ─────────────────────────' -ForegroundColor DarkGray
    Write-Host '  PgBouncer          localhost:6432                  <- a APLICAÇÃO usa esta'
    Write-Host '  PostgreSQL         localhost:5432                  migrações, psql, DBeaver'
    Write-Host '  Valkey (Redis)     localhost:6379                  cache, rate limit'
    Write-Host '  Azurite (Blob)     localhost:10000                 evidências, exports'
    Write-Host '  Service Bus        localhost:5672 / :5300          outbox (AMQP)'
    Write-Host '  Keycloak           http://localhost:8080           OIDC (no lugar do Entra)'
    Write-Host '  Painel OTel        http://localhost:18888          traces e logs'
    Write-Host ''
    podman compose -f $Compose ps --format 'table {{.Names}}\t{{.Status}}' 2>$null
}

function Test-Rls {
    Info 'Verificando o isolamento entre empresas (ADR-0003)...'
    Write-Host ''
    $falhas = 0

    function Checar($nome, $sql, $esperado) {
        $obtido = Get-Valor (Invoke-Psql $sql)
        if ($obtido -eq $esperado) { Ok "$nome  ->  $obtido" }
        else { Erro "$nome  ->  esperado '$esperado', obtido '$obtido'"; $script:falhas++ }
    }

    # Sem BEGIN/COMMIT: `psql -c` já roda várias instruções numa transação
    # implícita, então o set_config LOCAL vale para o SELECT seguinte.
    Checar 'sem contexto não vê nada       ' `
           'SELECT count(*) FROM sandbox.ativo_exemplo' '0'

    Checar 'Empresa A vê os 2 ativos dela  ' `
           "SELECT set_config('app.tenant_id','$EMPRESA_A',true); SELECT count(*) FROM sandbox.ativo_exemplo" '2'

    Checar 'Empresa B vê só o dela         ' `
           "SELECT set_config('app.tenant_id','$EMPRESA_B',true); SELECT count(*) FROM sandbox.ativo_exemplo" '1'

    # O teste que justifica o PgBouncer existir no ambiente local: o COMMIT
    # encerra a transação, o contexto LOCAL morre junto, e a mesma conexão —
    # que o pooler entregaria a outra requisição — volta a não ver nada.
    Checar 'contexto morre no COMMIT       ' `
           "BEGIN; SELECT set_config('app.tenant_id','$EMPRESA_A',true); COMMIT; SELECT count(*) FROM sandbox.ativo_exemplo" '0'

    Checar 'UPDATE cruzando empresa é nulo ' `
           "SELECT set_config('app.tenant_id','$EMPRESA_A',true); UPDATE sandbox.ativo_exemplo SET nome='x' WHERE tenant_id='$EMPRESA_B'; SELECT count(*) FROM sandbox.ativo_exemplo WHERE nome='x'" '0'

    $insercao = Invoke-Psql "SELECT set_config('app.tenant_id','$EMPRESA_A',true); INSERT INTO sandbox.ativo_exemplo (tenant_id,codigo,nome) VALUES ('$EMPRESA_B','X-1','invasor')"
    if ("$insercao" -match 'row-level security') { Ok 'INSERT com tenant alheio é recusado' }
    else { Erro "INSERT com tenant alheio NÃO foi recusado: $insercao"; $falhas++ }

    $orfas = Get-Valor (Invoke-Psql "SELECT coalesce(string_agg(schema_nome||'.'||tabela,', '),'-') FROM plataforma.auditoria_rls() WHERE tem_coluna_tenant AND NOT (rls_ligado AND rls_forcado AND tem_politica)")
    if ($orfas -eq '-') { Ok 'nenhuma tabela com tenant_id sem RLS' }
    else { Erro "tabelas desprotegidas: $orfas"; $falhas++ }

    $bypass = Get-Valor (Invoke-Psql "SELECT count(*) FROM pg_roles WHERE rolname='catalogo_app' AND (rolbypassrls OR rolsuper)" 'catalogo_migrador' 5432)
    if ($bypass -eq '0') { Ok 'catalogo_app não tem BYPASSRLS nem é superusuário' }
    else { Erro 'catalogo_app PODE contornar RLS — a política vira decoração'; $falhas++ }

    Write-Host ''
    if ($falhas -eq 0) { Ok 'Isolamento entre empresas íntegro.' }
    else { Erro "$falhas verificação(ões) falharam. NÃO prossiga sem entender."; exit 1 }
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
    'testar-rls' { Test-Rls }
    'limpar'     { Clear-Orfaos }
    'logs'       { if ($Alvo) { podman compose -f $Compose logs -f $Alvo } else { podman compose -f $Compose logs --tail 50 } }
    'psql'       { podman exec -it -e PGPASSWORD=dev_app catalogo-postgres psql -h pgbouncer -p 6432 -U catalogo_app -d catalogo }
    'pool'       {
        # SHOW POOLS roda no banco virtual "pgbouncer", não em "catalogo".
        podman exec -e PGPASSWORD=dev_migrador catalogo-postgres `
            psql -h pgbouncer -p 6432 -U catalogo_migrador -d pgbouncer -c 'SHOW POOLS'
    }
}
