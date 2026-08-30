#Requires -Version 5.1
<#
.SYNOPSIS
    Restore a pg_dump into a fresh database and bring the whole stack up.

.DESCRIPTION
    One-time move of this project from the NAS to a machine with enough memory. The NAS is
    a DS124 with 1 GB of soldered RAM; running PostgreSQL, a .NET API and several other
    containers on it took the whole box down repeatedly, DSM included.

    The dump is restored into an EMPTY database on purpose. db/*.sql would otherwise run
    first and create the tables, and the dump's own CREATE TABLE statements would then fail
    on tables that already exist. Dropping and recreating first leaves exactly one authority
    on what the schema looks like: the dump.

    The data directory cannot simply be copied instead. The NAS is ARM64 and this machine is
    x86: a PostgreSQL data directory is not portable between architectures, so SQL is the
    only way across.

.PARAMETER Dump
    The .sql file from `pg_dump` on the old machine.

.EXAMPLE
    .\tools\restore-and-start.ps1
    .\tools\restore-and-start.ps1 -Dump C:\tmp\backup.sql
#>
[CmdletBinding()]
param(
    [string]$Dump        = 'backup.sql',
    [string]$HealthUrl   = 'http://localhost:8140/health',
    [int]   $WaitSeconds = 180
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    # ---------- Checks worth making before anything is destroyed ----------
    if (-not (Test-Path $Dump)) { throw "Không thấy file dump '$Dump'." }

    if (-not (Test-Path '.env')) {
        throw "Chưa có file .env. Chép .env.example thành .env rồi điền giá trị thật trước đã."
    }

    & docker version --format '{{.Server.Version}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Docker chưa chạy. Mở Docker Desktop rồi chạy lại." }

    $dbUser = (Select-String -Path '.env' -Pattern '^\s*DB_USER\s*=\s*(.+)$').Matches.Groups[1].Value.Trim()
    if (-not $dbUser) { throw "Không đọc được DB_USER trong .env." }

    Write-Host ""
    Write-Host "==> Khởi động database" -ForegroundColor Cyan
    & docker compose up -d db
    if ($LASTEXITCODE -ne 0) { throw "Không khởi động được database." }

    # ---------- Wait for it to actually accept queries ----------
    Write-Host "==> Chờ database sẵn sàng" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds(120)
    do {
        Start-Sleep -Seconds 2
        & docker compose exec -T db pg_isready -U $dbUser -d storechecking 2>&1 | Out-Null
        $ready = $LASTEXITCODE -eq 0
    } while (-not $ready -and (Get-Date) -lt $deadline)

    if (-not $ready) { throw "Database không sẵn sàng sau 120 giây." }

    # ---------- Empty database, then the dump decides everything ----------
    Write-Host "==> Dựng lại database rỗng" -ForegroundColor Cyan
    & docker compose exec -T db psql -U $dbUser -d postgres -v ON_ERROR_STOP=1 `
        -c "drop database if exists storechecking;" -c "create database storechecking;"
    if ($LASTEXITCODE -ne 0) { throw "Không dựng lại được database rỗng." }

    Write-Host "==> Nạp dữ liệu từ $Dump" -ForegroundColor Cyan
    # cmd /c because PowerShell's own redirection re-encodes the file, which corrupts every
    # Vietnamese character on the way in.
    cmd /c "docker compose exec -T db psql -U $dbUser -d storechecking -v ON_ERROR_STOP=1 < `"$Dump`"" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Nạp dữ liệu hỏng. Database đang rỗng, chạy lại được." }

    # ---------- Count what landed, so "restored" is a fact ----------
    Write-Host ""
    Write-Host "==> Số dòng đã nạp" -ForegroundColor Cyan
    $sql = @"
select 'expenses', count(*) from expenses
union all select 'sales', count(*) from sales
union all select 'packing_videos', count(*) from packing_videos
union all select 'english_words', count(*) from english_words
union all select 'speaking_saved', count(*) from speaking_saved
union all select 'products', count(*) from products
union all select 'batches', count(*) from batches
order by 1;
"@
    $sql | & docker compose exec -T db psql -U $dbUser -d storechecking -t -A -F' : '

    # ---------- The rest of the stack ----------
    Write-Host ""
    Write-Host "==> Khởi động API và Tailscale" -ForegroundColor Cyan
    & docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw "Không khởi động được phần còn lại." }

    Write-Host "==> Chờ API trả lời" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        try {
            $r = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 10
            Write-Host ""
            Write-Host "XONG. API sống, database nối được: $($r.db), bản build: $($r.version)" -ForegroundColor Green
            return
        } catch { Write-Host "    chưa trả lời, đang chờ…" }
    }

    throw "API không trả lời sau $WaitSeconds giây. Xem log: docker compose logs api"
}
finally {
    Pop-Location
}
