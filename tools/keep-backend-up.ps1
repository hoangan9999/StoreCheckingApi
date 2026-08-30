#Requires -Version 5.1
<#
.SYNOPSIS
    Make sure the backend is running. Meant to be run by a scheduled task, not by hand.

.DESCRIPTION
    The rule this enforces is simple: if the machine is on, the API answers.

    Docker Desktop's own "start when you sign in" covers a reboot, but not the case that
    actually happened three times in one afternoon — Docker Desktop stopping while the
    machine stayed on. Nothing brings it back on its own, and the app on the phone is dead
    until somebody notices. This checks, and fixes it.

    Everything here is safe to repeat. `docker compose up -d` starts what is missing and
    leaves what is already running alone, so a run when all is well does nothing at all.

.PARAMETER Quiet
    Only write a log line when something was actually wrong. What the scheduled task uses:
    a line every five minutes saying "fine" would bury the ones worth reading.
#>
[CmdletBinding()]
param(
    [int]   $DockerWaitSeconds = 180,
    [string]$LogPath           = "$env:LOCALAPPDATA\storechecking-keepalive.log",
    [switch]$Quiet,

    # Kiểm mãi thay vì kiểm một lần rồi thoát. Dùng cho lối tắt trong thư mục Startup —
    # tác vụ theo lịch của Windows thì gọn hơn nhưng đăng ký nó cần quyền quản trị.
    [switch]$Loop,
    [int]   $IntervalMinutes = 5
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent $PSScriptRoot

function Write-Line([string]$text, [switch]$Always) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $text
    if ($Always -or -not $Quiet) { Write-Host $line }
    try {
        # Keep the log from growing forever — nobody prunes a file they never look at.
        if ((Test-Path $LogPath) -and (Get-Item $LogPath).Length -gt 512KB) {
            $keep = Get-Content $LogPath -Tail 200
            Set-Content $LogPath $keep -Encoding utf8
        }
        Add-Content -Path $LogPath -Value $line -Encoding utf8
    } catch { }
}

function Test-Docker {
    & docker version --format '{{.Server.Version}}' 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Invoke-Check {

# Two runs at once would fight over the same containers.
$lock = Join-Path $env:TEMP 'storechecking-keepalive.lock'
$mine = $false
try {
    try {
        [IO.File]::Open($lock, 'CreateNew', 'Write', 'None').Close()
        $mine = $true
    } catch {
        Write-Line "Đang có lượt kiểm khác chạy, bỏ qua lượt này."
        return
    }

    if (-not (Test-Path (Join-Path $root '.env'))) {
        Write-Line "Chưa có .env nên chưa dựng được gì. Bỏ qua." -Always
        return
    }

    # ---------- Docker engine ----------
    if (-not (Test-Docker)) {
        Write-Line "Docker không trả lời — đang bật Docker Desktop." -Always

        $exe = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
        if (-not (Test-Path $exe)) { Write-Line "Không thấy Docker Desktop ở $exe." -Always; return }
        if (-not (Get-Process 'Docker Desktop' -ErrorAction SilentlyContinue)) { Start-Process $exe }

        $deadline = (Get-Date).AddSeconds($DockerWaitSeconds)
        while (-not (Test-Docker) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }

        if (-not (Test-Docker)) {
            Write-Line "Docker vẫn chưa lên sau $DockerWaitSeconds giây. Sẽ thử lại ở lượt sau." -Always
            return
        }
        Write-Line "Docker đã lên." -Always
    }

    # ---------- Containers ----------
    # Compare against what SHOULD run rather than counting: a container can be present and
    # stopped, and a count would call that fine.
    Push-Location $root
    try {
        $running = (& docker compose ps --services --filter status=running) -split "`r?`n" | Where-Object { $_ }
        $wanted  = (& docker compose config --services) -split "`r?`n" | Where-Object { $_ }
        $missing = $wanted | Where-Object { $running -notcontains $_ }

        if (-not $missing) { Write-Line "Mọi thứ đang chạy."; return }

        Write-Line ("Chưa chạy: {0} — đang dựng lại." -f ($missing -join ', ')) -Always
        & docker compose up -d 2>&1 | Out-Null

        if ($LASTEXITCODE -eq 0) { Write-Line "Đã dựng lại xong." -Always }
        else { Write-Line "Dựng lại hỏng (mã $LASTEXITCODE). Xem: docker compose logs" -Always }
    }
    finally { Pop-Location }
}
finally {
    if ($mine) { Remove-Item $lock -ErrorAction SilentlyContinue }
}

}

if ($Loop) {
    Write-Line "Bắt đầu canh, mỗi $IntervalMinutes phút một lượt." -Always
    while ($true) {
        # Một lượt hỏng không được phép giết vòng canh — hỏng thì lượt sau kiểm lại.
        try { Invoke-Check } catch { Write-Line "Lượt kiểm hỏng: $($_.Exception.Message)" -Always }
        Start-Sleep -Seconds ($IntervalMinutes * 60)
    }
} else {
    Invoke-Check
}
