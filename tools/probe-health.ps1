#Requires -Version 5.1
<#
.SYNOPSIS
    Measure /health at intervals and record which phase was slow.

.DESCRIPTION
    The API is fast when it has just been used and slow after sitting idle — /health
    included, which rules out anything that happens once per process. Two candidates are
    left, and they leave different fingerprints:

      tls large     -> the Tailscale Funnel path went cold and had to be rebuilt
      server large  -> the NAS side: disks asleep (Synology HDD Hibernation) so the first
                       query waits for spin-up, or the process was paged out to swap

    One measurement taken WHILE it is slow settles it. Leave this running and read the
    lines with a big TONG; the phase that dominates is the answer.

    Each probe opens a fresh connection on purpose, so it measures the worst case a
    browser sees on its first call. Later calls in a browser reuse the connection and are
    much faster.

.EXAMPLE
    .\tools\probe-health.ps1
    .\tools\probe-health.ps1 -EverySeconds 900 -LogPath health-probe.csv
#>
[CmdletBinding()]
param(
    [string]$Url          = 'https://storechecking.tail631d54.ts.net/health',
    # 10 minutes: long enough for the path to go cold between probes, which is exactly the
    # state worth catching. Raise it if the slow spells only show up after longer gaps.
    [int]   $EverySeconds = 600,
    [string]$LogPath      = 'health-probe.csv'
)

$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# curl.exe ships with Windows 10+ and is the only easy way to get a per-phase breakdown.
$curl = Join-Path $env:SystemRoot 'System32\curl.exe'
if (-not (Test-Path $curl)) { throw "Không tìm thấy curl.exe ở $curl." }

if (-not (Test-Path $LogPath)) {
    'thoi_diem,http,dns,tcp,tls,server,tong' | Out-File -FilePath $LogPath -Encoding utf8
}

Write-Host "Đo $Url mỗi $EverySeconds giây. Ghi vào $LogPath. Ctrl+C để dừng." -ForegroundColor Cyan
Write-Host "Dòng nào TONG lớn thì xem giai đoạn nào chiếm phần lớn — đó là thủ phạm." -ForegroundColor Cyan
Write-Host ""

while ($true) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    # -m 90 rather than a short timeout: a probe that gives up early would record a failure
    # where the interesting number is how long it actually took.
    $raw = & $curl -s -o NUL -m 90 -w '%{http_code},%{time_namelookup},%{time_connect},%{time_appconnect},%{time_starttransfer},%{time_total}' $Url 2>$null

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
        $line = "$stamp,LOI-$LASTEXITCODE,,,,,"
        Write-Host "$stamp  THẤT BẠI (curl lỗi $LASTEXITCODE)" -ForegroundColor Red
    }
    else {
        $line = "$stamp,$raw"
        $p = $raw -split ','
        $tls    = [double]$p[3]
        $server = [double]$p[4] - $tls
        $total  = [double]$p[5]

        $phase = if ($tls -ge $server) { 'TLS (Funnel)' } else { 'SERVER (NAS)' }
        $colour = if ($total -gt 5) { 'Yellow' } else { 'Gray' }

        Write-Host ("{0}  tong={1,6:0.00}s  tls={2,6:0.00}s  server={3,6:0.00}s  -> chậm ở {4}" -f `
            $stamp, $total, $tls, $server, $phase) -ForegroundColor $colour
    }

    $line | Out-File -FilePath $LogPath -Encoding utf8 -Append
    Start-Sleep -Seconds $EverySeconds
}
