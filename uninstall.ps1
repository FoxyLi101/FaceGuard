#Requires -Version 5.1
<#
    FaceGuard 登录留影 - 卸载脚本
    默认保留照片；加 -Purge 一并删除照片和配置。
#>
[CmdletBinding()]
param([switch]$Purge)

$ErrorActionPreference = 'SilentlyContinue'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "================ FaceGuard 卸载 ================"

# 1. 停止进程
Get-Process FaceGuardDaemon -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# 2. 计划任务
foreach ($tn in @('FaceGuard Watch', 'FaceGuard Notify')) {
    $task = Get-ScheduledTask -TaskName $tn -ErrorAction SilentlyContinue
    if ($task) {
        if ($isAdmin) {
            Stop-ScheduledTask -TaskName $tn -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $tn -Confirm:$false
            Write-Host ("已删除计划任务 " + $tn)
        }
        else {
            Write-Warning ("存在计划任务 " + $tn + "，需要管理员权限才能删除")
        }
    }
}

# 3. 登录启动项
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'FaceGuard 守护.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Host "已删除登录启动项" }

# 4. 桌面快捷方式
$desk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FaceGuard 照片.lnk'
if (Test-Path $desk) { Remove-Item $desk -Force; Write-Host "已删除桌面快捷方式" }

# 5. 程序目录
$dirs = @(
    (Join-Path $env:ProgramFiles 'FaceGuard'),
    (Join-Path $env:LOCALAPPDATA 'Programs\FaceGuard')
)
foreach ($d in $dirs) {
    if (Test-Path $d) {
        Remove-Item $d -Recurse -Force
        if (Test-Path $d) { Write-Warning ("无法删除（可能权限不足）: " + $d) }
        else { Write-Host ("已删除程序目录: " + $d) }
    }
}

# 6. 数据（照片/配置/日志）
if ($Purge) {
    $datas = @(
        (Join-Path $env:ProgramData 'FaceGuard'),
        (Join-Path $env:LOCALAPPDATA 'FaceGuard')
    )
    foreach ($d in $datas) {
        if (Test-Path $d) {
            Remove-Item $d -Recurse -Force
            if (Test-Path $d) { Write-Warning ("无法删除（可能权限不足）: " + $d) }
            else { Write-Host ("已删除数据目录: " + $d) }
        }
    }
}
else {
    Write-Host "照片与配置已保留："
    Write-Host ("  " + (Join-Path $env:ProgramData 'FaceGuard'))
    Write-Host ("  " + (Join-Path $env:LOCALAPPDATA 'FaceGuard'))
    Write-Host "加 -Purge 可一并删除。"
}

Write-Host ""
Write-Host "卸载完成。"
