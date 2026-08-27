#Requires -Version 5.1
<#
.SYNOPSIS
    Push the current commit and wait until the NAS is actually running it.

.DESCRIPTION
    Nothing is built here. This is a company laptop and Docker Desktop needs a paid
    licence on one, so GitHub Actions does the build instead — see
    .github/workflows/build-and-push.yml. The NAS must never build .NET either: a build
    pins its CPU for 5-15 minutes and makes everything else on it crawl, phone uploads
    included.

    The chain is: push -> GitHub Actions builds and pushes to GHCR -> watchtower on the
    NAS pulls it and restarts the API. This script drives the first link and then watches
    /health until the last one has happened, so "đã deploy" stays a measured fact.

    Rolling back to an earlier build is done from the Actions tab: re-run the workflow of
    the commit you want, which re-tags that build as `latest`.

.PARAMETER NoWait
    Push and stop, without waiting for the NAS to pick the image up.

.EXAMPLE
    .\tools\deploy.ps1
#>
[CmdletBinding()]
param(
    [string]$HealthUrl   = 'https://storechecking.tail631d54.ts.net/health',
    [string]$ActionsUrl  = 'https://github.com/hoangan9999/StoreCheckingApi/actions',
    # Generous on purpose: an uncached Actions build takes several minutes, then
    # watchtower polls once a minute, then the API restarts.
    [int]   $WaitSeconds = 900,
    [switch]$NoWait
)

$ErrorActionPreference = 'Stop'

# This file is saved as UTF-8 WITH a byte order mark on purpose: Windows PowerShell 5.1
# reads a .ps1 as ANSI without one, which turns every Vietnamese message into garbage.
# The console also has to be told, or it prints the right bytes in the wrong codepage.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# Windows PowerShell 5.1 still negotiates TLS 1.0 by default, which ts.net refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Repo root is the parent of tools/, so the script runs from any working directory.
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    # ---------- The build comes from a commit, so there must not be loose changes ----------
    # This is the one real cost of dropping the local build: there is no such thing as
    # deploying uncommitted work any more. Actions only ever sees what was pushed.
    if (& git status --porcelain) {
        throw ("Còn thay đổi chưa commit. GitHub chỉ build được thứ đã đẩy lên, " +
               "nên hãy commit trước rồi chạy lại.")
    }

    # Fixed at 7 characters to match ${GITHUB_SHA:0:7} in the workflow. Git's default
    # --short length grows with the repo, which would break the comparison below.
    $version = (& git rev-parse --short=7 HEAD)
    if ($LASTEXITCODE -ne 0) { throw "Không đọc được commit. Thư mục này không phải kho git?" }
    $version = $version.Trim()

    $branch = (& git rev-parse --abbrev-ref HEAD).Trim()

    # ---------- Push: this is what starts the build ----------
    Write-Host ""
    Write-Host "==> Đẩy $branch lên GitHub (commit $version)" -ForegroundColor Cyan
    & git push origin $branch
    if ($LASTEXITCODE -ne 0) { throw "Đẩy lên GitHub hỏng." }

    Write-Host "    GitHub Actions đang build. Xem tiến độ: $ActionsUrl"

    if ($NoWait) {
        Write-Host ""
        Write-Host "Đã đẩy $version. NAS sẽ tự cập nhật sau khi build xong." -ForegroundColor Green
        return
    }

    # ---------- Wait for the NAS to actually be running it ----------
    # Polling /health is what turns "đã đẩy" into "đang chạy". Without this the script
    # would report success while the NAS still ran the old build.
    Write-Host ""
    Write-Host "==> Chờ Actions build xong rồi NAS kéo về (watchtower kiểm tra mỗi 60 giây)" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $lastSeen = ''

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 15
        $running = $null
        try {
            $running = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 20
        } catch {
            # The API is restarting, or the house connection dropped. Both resolve on
            # their own, so keep waiting instead of failing the deploy.
            Write-Host "    máy chủ chưa trả lời (bình thường lúc đang khởi động lại)"
            continue
        }

        if ($running.version -eq $version) {
            Write-Host ""
            Write-Host "XONG. NAS đang chạy bản $version, database nối được: $($running.db)" -ForegroundColor Green
            return
        }

        if ($running.version -ne $lastSeen) {
            $lastSeen = $running.version
            if ([string]::IsNullOrEmpty($lastSeen)) {
                # An image built before /health reported a version — i.e. one the NAS
                # built itself, from before this deploy pipeline existed.
                Write-Host "    NAS còn chạy ảnh cũ (không có trường version)"
            } else {
                Write-Host "    NAS còn chạy bản cũ: $lastSeen"
            }
        }
    }

    throw ("Hết $WaitSeconds giây mà NAS vẫn chưa chạy bản $version. " +
           "Xem build có đỏ không tại $ActionsUrl, rồi xem log container " +
           "storechecking-watchtower trong Container Manager.")
}
finally {
    Pop-Location
}
