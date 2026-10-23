$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

$outDir = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$refs = @(
  '/r:System.dll',
  '/r:System.Core.dll',
  '/r:System.Drawing.dll',
  '/r:System.Windows.Forms.dll',
  '/r:System.Xml.dll'
)

$common = @('/nologo', '/codepage:65001', '/utf8output', '/optimize+', '/platform:anycpu') + $refs
$src = Join-Path $root 'src\*.cs'

Write-Host "[1/2] FaceGuard.exe (console CLI)"
& $csc ($common + @('/target:exe', "/out:$outDir\FaceGuard.exe", $src))
if ($LASTEXITCODE -ne 0) { throw "build FaceGuard.exe failed ($LASTEXITCODE)" }

Write-Host "[2/2] FaceGuardDaemon.exe (background, no console window)"
& $csc ($common + @('/target:winexe', "/out:$outDir\FaceGuardDaemon.exe", $src))
if ($LASTEXITCODE -ne 0) { throw "build FaceGuardDaemon.exe failed ($LASTEXITCODE)" }

Get-ChildItem $outDir -Filter *.exe | ForEach-Object {
  Write-Host ("  OK  {0}  {1:N0} bytes" -f $_.Name, $_.Length)
}
