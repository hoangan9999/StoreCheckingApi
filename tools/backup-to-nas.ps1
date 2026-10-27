#Requires -Version 5.1
<#
.SYNOPSIS
    Dump the database and push it to the NAS.

.DESCRIPTION
    The database lives on this PC, which can be switched off; the NAS runs all day. So the
    backup goes the other way from where the data lives, and the PC does the pushing.

    Deliberately NOT the NAS pulling from here. A pull at 01:00 against a PC that happens to
    be off is simply a missed night, with nothing able to fix it afterwards. Pushing lets a
    missed run be caught up the moment the machine is next on — see keep-backend-up.ps1,
    which calls this.

    Reuses the upload endpoint the phone already uses for packing videos, so there is no new
    endpoint and no new secret anywhere.

.PARAMETER KeepLocal
    How many dumps to keep on this PC. The copies on the NAS are never touched: deleting
    backups automatically is how people find out their backups were gone months later.
#>
[CmdletBinding()]
param(
    [string]$OutDir    = "$env:LOCALAPPDATA\storechecking-backup",
    [string]$RemoteDir = 'db-backup',
    [int]   $KeepLocal = 7,
    [string]$LogPath   = "$env:LOCALAPPDATA\storechecking-backup.log"
)

# 'Continue', not 'Stop': PowerShell 5.1 turns each stderr line from a native program into an
# error record, and docker writes ordinary progress there. Correctness rests on $LASTEXITCODE.
$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent $PSScriptRoot

function Write-Line([string]$text) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $text
    Write-Host $line
    try {
        if ((Test-Path $LogPath) -and (Get-Item $LogPath).Length -gt 256KB) {
            Set-Content $LogPath (Get-Content $LogPath -Tail 200) -Encoding utf8
        }
        Add-Content -Path $LogPath -Value $line -Encoding utf8
    } catch { }
}

function Read-EnvValue([string]$name) {
    $m = Select-String -Path (Join-Path $root '.env') -Pattern "^\s*$name\s*=\s*(.+)$"
    if ($m) { return $m.Matches.Groups[1].Value.Trim() }
    return $null
}

Push-Location $root
try {
    $dbUser = Read-EnvValue 'DB_USER'
    $nasUrl = Read-EnvValue 'NAS_UPLOAD_URL'
    $nasTok = Read-EnvValue 'NAS_UPLOAD_TOKEN'

    if (-not $dbUser) { Write-Line 'Không đọc được DB_USER trong .env. Dừng.'; exit 1 }
    if (-not $nasUrl -or -not $nasTok) {
        Write-Line 'Thiếu NAS_UPLOAD_URL hoặc NAS_UPLOAD_TOKEN trong .env. Dừng.'
        exit 1
    }

    & docker compose ps --services --filter status=running | Select-String -Quiet '^db$' | Out-Null
    if (-not (& docker compose ps --services --filter status=running | Where-Object { $_ -eq 'db' })) {
        Write-Line 'Container database không chạy, chưa sao lưu được. Sẽ thử lại lượt sau.'
        exit 1
    }

    if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

    $stamp = Get-Date -Format 'yyyy-MM-dd'
    $sql   = Join-Path $OutDir "storechecking-$stamp.sql"
    $zip   = Join-Path $OutDir "storechecking-$stamp.zip"

    # ---------- Dump ----------
    # Through cmd, not PowerShell redirection: PowerShell re-encodes redirected output to
    # UTF-16 and every Vietnamese character in the dump comes out corrupted.
    Write-Line "Đang kết xuất database…"
    cmd /c "docker compose exec -T db pg_dump -U $dbUser -d storechecking --no-owner --no-privileges > `"$sql`"" | Out-Null

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $sql)) {
        Write-Line "pg_dump hỏng (mã $LASTEXITCODE). Không gửi gì lên NAS."
        Remove-Item $sql -ErrorAction SilentlyContinue
        exit 1
    }

    # ---------- Refuse to ship a truncated dump ----------
    # pg_dump writes this as its last act. Without the check, a dump cut short by a crash
    # would upload happily and only be found wanting on the day it is needed.
    $tail = Get-Content $sql -Tail 20 -ErrorAction SilentlyContinue
    if (-not ($tail -match 'PostgreSQL database dump complete')) {
        Write-Line 'Bản kết xuất KHÔNG hoàn chỉnh (thiếu dòng kết thúc). Không gửi lên NAS.'
        Remove-Item $sql -ErrorAction SilentlyContinue
        exit 1
    }

    $rows = (Select-String -Path $sql -Pattern '^COPY public\.' -AllMatches).Count
    $kb   = [math]::Round((Get-Item $sql).Length / 1KB, 1)
    Write-Line "Kết xuất xong: $kb KB, $rows bảng."

    # ---------- Compress ----------
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path $sql -DestinationPath $zip -CompressionLevel Optimal
    $zkb = [math]::Round((Get-Item $zip).Length / 1KB, 1)

    # ---------- Push to the NAS ----------
    $name = Split-Path $zip -Leaf
    $url  = "$($nasUrl.TrimEnd('/'))/upload?dir=$RemoteDir"

    # Xác thực bằng HEADER chứ không phải ?token=. Trong server.js của nas-uploader, hàm
    # authed(req, u) nhận hai tham số, nhưng route /upload gọi authed(req) thiếu tham số thứ
    # hai — nên nhánh đọc query bị vô hiệu ở đúng route này và chỉ header mới qua được.
    # (Route /solar truyền đủ tham số nên ?token= vẫn dùng được ở đó.)
    # Dùng header cũng kín hơn: token không lọt vào log truy cập.
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    $out = & $curl -s -S --max-time 300 -X POST $url `
        -H "Authorization: Bearer $nasTok" -H "X-Filename: $name" --data-binary "@$zip" 2>&1

    if ($LASTEXITCODE -ne 0) { Write-Line "Gửi lên NAS hỏng (curl mã $LASTEXITCODE): $out"; exit 1 }
    if ("$out" -notmatch '"ok"\s*:\s*true') { Write-Line "NAS trả lời không như mong đợi: $out"; exit 1 }

    Write-Line "Đã gửi lên NAS: $RemoteDir/$name ($zkb KB)."

    # ---------- Media files ----------
    # pg_dump backs up the DATABASE, not the FILES. media_images stores only file NAMES; the
    # pictures themselves live in a Docker volume on this machine. Restoring the database
    # without them leaves a list pointing at images that no longer exist.
    #
    # Only images and notes are taken. Those were uploaded by hand and cannot be recreated.
    # The videos folder is left out DELIBERATELY: they are generated daily and deleted after
    # five days, so copying them spends bandwidth and NAS space on something the system
    # rebuilds by itself.
    $mediaOk = $true

    if (-not (& docker compose ps --services --filter status=running | Where-Object { $_ -eq 'api' })) {
        Write-Line 'Container api không chạy, bỏ qua phần media lần này.'
        $mediaOk = $false
    }
    else {
        # Fingerprint of the folders: names, sizes and modification times. Unchanged means
        # there is nothing to send, which matters because 39 MB a night would otherwise grow
        # into tens of GB a year on the NAS for data that never changed.
        $listing = & docker exec storechecking-api sh -c 'ls -laR /data/media/images /data/media/notes 2>/dev/null'
        $sha = [BitConverter]::ToString(
            [Security.Cryptography.SHA256]::Create().ComputeHash(
                [Text.Encoding]::UTF8.GetBytes(($listing -join "`n")))).Replace('-', '')

        $markPath = Join-Path $OutDir 'media.sha256'
        $prev = if (Test-Path $markPath) { (Get-Content $markPath -Raw).Trim() } else { '' }

        if ($sha -eq $prev) {
            Write-Line 'Media không đổi từ lần trước, không gửi lại.'
        }
        else {
            $stage = Join-Path $OutDir "media-$stamp"
            $mzip  = Join-Path $OutDir "storechecking-media-$stamp.zip"
            Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item $mzip -Force -ErrorAction SilentlyContinue
            New-Item -ItemType Directory -Path $stage | Out-Null

            # docker cp rather than tar inside the container: it needs no tooling in the
            # image, so it keeps working whatever that image turns into.
            & docker cp 'storechecking-api:/data/media/images' (Join-Path $stage 'images') | Out-Null
            $cpImages = $LASTEXITCODE
            & docker cp 'storechecking-api:/data/media/notes' (Join-Path $stage 'notes') | Out-Null
            $cpNotes = $LASTEXITCODE

            if ($cpImages -ne 0 -or $cpNotes -ne 0) {
                Write-Line "Lấy file media ra hỏng (images=$cpImages notes=$cpNotes). Không gửi lên NAS."
                $mediaOk = $false
            }
            else {
                Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $mzip -CompressionLevel Optimal
                $mkb   = [math]::Round((Get-Item $mzip).Length / 1KB, 1)
                $mname = Split-Path $mzip -Leaf
                $murl  = "$($nasUrl.TrimEnd('/'))/upload?dir=media-backup"

                $mout = & $curl -s -S --max-time 900 -X POST $murl `
                    -H "Authorization: Bearer $nasTok" -H "X-Filename: $mname" --data-binary "@$mzip" 2>&1

                if ($LASTEXITCODE -ne 0 -or "$mout" -notmatch '"ok"\s*:\s*true') {
                    Write-Line "Gửi media lên NAS hỏng: $mout"
                    $mediaOk = $false
                }
                else {
                    Write-Line "Đã gửi media lên NAS: media-backup/$mname ($mkb KB)."
                    # Written only AFTER a successful upload. Writing it earlier would make
                    # a failed send look done, and every later run would skip it for good.
                    Set-Content -Path $markPath -Value $sha -Encoding ascii
                }
            }

            Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # ---------- Prune local copies only ----------
    Get-ChildItem $OutDir -Filter 'storechecking-*.sql' |
        Sort-Object LastWriteTime -Descending | Select-Object -Skip $KeepLocal |
        ForEach-Object { Remove-Item $_.FullName -ErrorAction SilentlyContinue }
    Get-ChildItem $OutDir -Filter 'storechecking-*.zip' |
        Sort-Object LastWriteTime -Descending | Select-Object -Skip $KeepLocal |
        ForEach-Object { Remove-Item $_.FullName -ErrorAction SilentlyContinue }

    # The database reached the NAS safely, but reporting success while the media half
    # failed would be a lie — and that kind of lie only surfaces on the day the backup is
    # actually needed.
    if (-not $mediaOk) {
        Write-Line 'Database đã sao lưu xong, nhưng phần media thì KHÔNG. Xem dòng log ở trên.'
        exit 1
    }

    exit 0
}
finally { Pop-Location }
