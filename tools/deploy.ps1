#Requires -Version 5.1
<#
.SYNOPSIS
    Build the API image on this PC, push it to GHCR, and wait until the NAS runs it.

.DESCRIPTION
    The NAS must never build .NET: a build pins its CPU for 5-15 minutes and makes
    everything else on it crawl, phone uploads included. So the PC builds, GHCR carries
    the image, and watchtower on the NAS pulls it and restarts the API by itself.

    Every image is tagged twice: with the commit it was built from, and as `latest`.
    Watchtower follows `latest`; the commit tags are what makes a rollback possible.

    Rolling back to an earlier build, from this PC:
        docker pull  ghcr.io/hoangan9999/storechecking-api:<commit>
        docker tag   ghcr.io/hoangan9999/storechecking-api:<commit> ghcr.io/hoangan9999/storechecking-api:latest
        docker push  ghcr.io/hoangan9999/storechecking-api:latest

.PARAMETER NoVerify
    Push and stop, without waiting for the NAS to pick the image up.

.EXAMPLE
    .\tools\deploy.ps1
#>
[CmdletBinding()]
param(
    [string]$Image       = 'ghcr.io/hoangan9999/storechecking-api',
    [string]$HealthUrl   = 'https://storechecking.tail631d54.ts.net/health',
    [int]   $WaitSeconds = 420,
    [switch]$NoVerify
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
    # ---------- Version: the exact commit this image was built from ----------
    $sha = & git rev-parse --short HEAD
    if ($LASTEXITCODE -ne 0) { throw "Không đọc được commit. Thư mục này không phải kho git?" }
    $sha = $sha.Trim()

    $version = $sha
    if (& git status --porcelain) {
        # Uncommitted changes: mark the build so it is never mistaken for a real commit.
        $version = "$sha-dirty-" + (Get-Date -Format 'MMdd-HHmm')
        Write-Host "Đang có thay đổi chưa commit -> đánh dấu bản build là $version" -ForegroundColor Yellow
    }

    $tagVersion = "${Image}:${version}"
    $tagLatest  = "${Image}:latest"

    # ---------- Build ----------
    # --platform is explicit on purpose: the NAS is x86_64, and an image built for ARM
    # would fail there with a baffling "exec format error".
    Write-Host ""
    Write-Host "==> Build ảnh $version" -ForegroundColor Cyan
    & docker build --platform linux/amd64 --build-arg "APP_VERSION=$version" -t $tagVersion -t $tagLatest .
    if ($LASTEXITCODE -ne 0) { throw "Build ảnh hỏng." }

    # ---------- Push ----------
    Write-Host ""
    Write-Host "==> Đẩy lên GHCR" -ForegroundColor Cyan
    & docker push $tagVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Đẩy ảnh hỏng. Nếu báo unauthorized hoặc denied thì đăng nhập trước: docker login ghcr.io"
    }
    & docker push $tagLatest
    if ($LASTEXITCODE -ne 0) { throw "Đẩy nhãn latest hỏng." }

    if ($NoVerify) {
        Write-Host ""
        Write-Host "Đã đẩy xong $version. NAS sẽ tự cập nhật trong khoảng 1 phút." -ForegroundColor Green
        return
    }

    # ---------- Wait for the NAS to actually be running it ----------
    # Polling /health is what turns "đã đẩy" into "đang chạy". Without this the script
    # would report success while the NAS still ran the old build.
    Write-Host ""
    Write-Host "==> Chờ NAS kéo ảnh mới (watchtower kiểm tra mỗi 60 giây)" -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $lastSeen = ''

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 10
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
            Write-Host "    NAS còn chạy bản cũ: $lastSeen"
        }
    }

    throw ("Hết $WaitSeconds giây mà NAS vẫn chưa chạy bản $version. " +
           "Xem log container storechecking-watchtower trong Container Manager.")
}
finally {
    Pop-Location
}
