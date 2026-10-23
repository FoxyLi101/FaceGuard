#Requires -Version 5.1
<#
    下载一份静态编译的 ffmpeg 到 bin\ffmpeg.exe。
    仅在 DirectShow 取不到帧（日志出现「DirectShow 取流不可用」）时才需要。
    用法:
        powershell -ExecutionPolicy Bypass -File .\get-ffmpeg.ps1
        powershell -ExecutionPolicy Bypass -File .\get-ffmpeg.ps1 -Force        # 重新下载
        powershell -ExecutionPolicy Bypass -File .\get-ffmpeg.ps1 -Url <地址>    # 自定义源
#>
[CmdletBinding()]
param(
    [string]$Url = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = Join-Path $here 'bin'
$exe = Join-Path $binDir 'ffmpeg.exe'

if ((Test-Path $exe) -and (-not $Force)) {
    Write-Host ("已存在，无需下载: " + $exe)
    Write-Host "如需重新下载，加 -Force"
    exit 0
}

New-Item -ItemType Directory -Force $binDir | Out-Null

$urls = @()
if ($Url) { $urls += $Url }
$urls += @(
    'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip',
    'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'
)

$tmp = Join-Path $env:TEMP ('ffmpeg-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.zip')
$zip = $null

# 支持直接给一个本地 zip（没网 / 已有安装包时最快）
if ($Url -and (Test-Path $Url)) {
    Write-Host ("使用本地压缩包: " + (Resolve-Path $Url).Path)
    $zip = (Resolve-Path $Url).Path
}

if (-not $zip) {
    foreach ($u in $urls) {
        try {
            Write-Host ("下载: " + $u)
            Write-Host "（约 80-120 MB，请耐心等待）"
            Invoke-WebRequest -Uri $u -OutFile $tmp -UseBasicParsing -TimeoutSec 900
            if ((Get-Item $tmp).Length -gt 10MB) { $zip = $tmp; break }
            Write-Warning "文件太小，可能不是有效压缩包，换下一个源"
        }
        catch {
            Write-Warning ("下载失败: " + $_.Exception.Message)
        }
    }
}

if (-not $zip) {
    Write-Host ""
    Write-Host "自动下载失败。请手动下载 ffmpeg 并把 ffmpeg.exe 放到:" -ForegroundColor Yellow
    Write-Host ("  " + $binDir) -ForegroundColor Yellow
    Write-Host "下载页: https://www.gyan.dev/ffmpeg/builds/  或  https://github.com/BtbN/FFmpeg-Builds/releases"
    exit 1
}

Write-Host "解压..."
$ext = Join-Path $env:TEMP ('ffmpeg-x-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    Expand-Archive -Path $zip -DestinationPath $ext -Force
    $found = Get-ChildItem $ext -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $found) { throw "压缩包里没有 ffmpeg.exe" }
    Copy-Item $found.FullName $exe -Force
}
finally {
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Remove-Item $ext -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "验证..."
& $exe -hide_banner -version 2>&1 | Select-Object -First 1

Write-Host ""
Write-Host ("完成: " + $exe) -ForegroundColor Green
Write-Host "提示: 该文件已被 .gitignore 忽略，不会被提交到仓库。"
exit 0
