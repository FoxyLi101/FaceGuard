#Requires -Version 5.1
<#
    FaceGuard 登录留影 - 安装脚本
    用户模式（无需管理员）：登录自启，锁屏 / 解锁 / 唤醒 留影
    系统模式（需管理员）  ：开机自启 SYSTEM 守护，覆盖开机登录界面 + 失败密码（4625）
#>
[CmdletBinding()]
param(
    [ValidateSet('auto', 'user', 'system')]
    [string]$Mode = 'auto',
    [string]$FfmpegPath = '',
    [switch]$NoAudit
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$binSrc = Join-Path $here 'bin'
if (-not (Test-Path (Join-Path $binSrc 'FaceGuard.exe'))) {
    throw "找不到 bin\FaceGuard.exe，请先运行 build.ps1 编译"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "================ FaceGuard 登录留影 安装 ================"
Write-Host ("用户    : " + $identity.Name)
Write-Host ("管理员  : " + $isAdmin)

if ($Mode -eq 'auto') {
    if ($isAdmin) { $Mode = 'system' } else { $Mode = 'user' }
}
Write-Host ("安装模式: " + $Mode)
Write-Host ""

function Find-Ffmpeg {
    if ($FfmpegPath -and (Test-Path $FfmpegPath)) { return (Resolve-Path $FfmpegPath).Path }
    $cands = @(
        (Join-Path $binSrc 'ffmpeg.exe'),
        (Join-Path $env:ProgramFiles 'ffmpeg\bin\ffmpeg.exe'),
        'C:\ffmpeg\bin\ffmpeg.exe',
        'C:\ffmpeg\ffmpeg.exe'
    )
    foreach ($c in $cands) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function New-Shortcut([string]$Path, [string]$Target, [string]$Arguments, [string]$WorkDir) {
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($Path)
    $sc.TargetPath = $Target
    if ($Arguments) { $sc.Arguments = $Arguments }
    if ($WorkDir) { $sc.WorkingDirectory = $WorkDir }
    $sc.WindowStyle = 7
    $sc.Description = 'FaceGuard 登录留影'
    $sc.Save()
}

# 把照片输出目录设到桌面，返回该路径
function Set-ShotsDir([string]$Exe) {
    $shots = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FaceGuard 照片'
    New-Item -ItemType Directory -Force $shots | Out-Null
    & $Exe set-shots --path $shots | Out-Null
    return $shots
}

# ---------------------------------------------------------------- 用户模式
function Install-User {
    $dest = Join-Path $env:LOCALAPPDATA 'Programs\FaceGuard'

    # 先停掉正在运行的守护进程，否则 exe 被占用会复制失败
    Get-Process FaceGuardDaemon -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600

    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $binSrc '*') $dest -Force -Recurse

    $ff = Find-Ffmpeg
    if ($ff) {
        if ($ff -ne (Join-Path $dest 'ffmpeg.exe')) { Copy-Item $ff (Join-Path $dest 'ffmpeg.exe') -Force }
        Write-Host ("已内置 ffmpeg: " + $ff)
    }
    else {
        Write-Warning "未找到 ffmpeg.exe。若 DirectShow 取流失败，将无法拍照；可先运行 get-ffmpeg.ps1 自动下载，或用 -FfmpegPath 指定。"
    }

    $shots = Set-ShotsDir (Join-Path $dest 'FaceGuard.exe')
    Write-Host ("照片输出: " + $shots)

    $startup = [Environment]::GetFolderPath('Startup')
    $lnk = Join-Path $startup 'FaceGuard 守护.lnk'
    New-Shortcut $lnk (Join-Path $dest 'FaceGuardDaemon.exe') 'agent' $dest

    Start-Process -FilePath (Join-Path $dest 'FaceGuardDaemon.exe') -ArgumentList 'agent' -WorkingDirectory $dest

    Write-Host ""
    Write-Host "安装完成（用户模式）"
    Write-Host ("  程序    : " + $dest)
    Write-Host ("  登录自启: " + $lnk)
    Write-Host ("  照片    : " + $shots)
    Write-Host ""
    Write-Host "注意：用户模式只能覆盖【登录后】的锁屏/解锁/唤醒；开机登录界面需要管理员运行本脚本（系统模式）。"
    $script:installedExe = Join-Path $dest 'FaceGuard.exe'
}

# ---------------------------------------------------------------- 系统模式
function Install-System {
    if (-not $isAdmin) {
        throw "系统模式需要管理员权限：请右键 install.ps1 -> 以管理员身份运行（或在管理员 PowerShell 里执行）"
    }

    $dest = Join-Path $env:ProgramFiles 'FaceGuard'

    # 先停掉正在运行的守护进程，否则 exe 被占用会复制失败
    Get-Process FaceGuardDaemon -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600

    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $binSrc '*') $dest -Force -Recurse

    $ff = Find-Ffmpeg
    if ($ff) {
        if ($ff -ne (Join-Path $dest 'ffmpeg.exe')) { Copy-Item $ff (Join-Path $dest 'ffmpeg.exe') -Force }
        Write-Host ("已内置 ffmpeg: " + $ff)
    }
    else {
        Write-Warning "未找到 ffmpeg.exe。若 DirectShow 取流失败，将无法拍照；可先运行 get-ffmpeg.ps1 自动下载，或用 -FfmpegPath 指定。"
    }

    # 数据目录：只允许 SYSTEM / 管理员 / 当前用户
    $data = Join-Path $env:ProgramData 'FaceGuard'
    New-Item -ItemType Directory -Force $data | Out-Null
    $sid = $identity.User.Value
    & icacls $data /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*${sid}:(OI)(CI)M" | Out-Null
    & icacls $data /inheritance:r | Out-Null
    Write-Host ("数据目录: " + $data + "（权限已收紧）")

    # 照片输出到桌面
    $shots = Set-ShotsDir (Join-Path $dest 'FaceGuard.exe')
    Write-Host ("照片输出: " + $shots)

    $exe = Join-Path $dest 'FaceGuardDaemon.exe'
    $taskName = 'FaceGuard Watch'

    # 如果之前装过用户模式，移除其登录启动项，避免重复拍摄
    $oldLnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'FaceGuard 守护.lnk'
    if (Test-Path $oldLnk) {
        Remove-Item $oldLnk -Force
        Write-Host "已移除用户模式的登录启动项（改由开机任务接管）"
        Get-Process FaceGuardDaemon -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }

    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    try {
        $action = New-ScheduledTaskAction -Execute $exe -Argument 'watch' -WorkingDirectory $dest
        $trigger = New-ScheduledTaskTrigger -AtStartup
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
        $task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
            -Description 'FaceGuard 登录留影：开机/锁屏期间自动拍摄尝试登录者'
        $task.Settings.Hidden = $true
        Register-ScheduledTask -TaskName $taskName -InputObject $task | Out-Null
        Write-Host "已注册计划任务: $taskName（开机启动，SYSTEM，隐藏窗口）"
    }
    catch {
        Write-Warning ("Register-ScheduledTask 失败，改用 schtasks: " + $_.Exception.Message)
        & schtasks /create /tn $taskName /tr "`"$exe`" watch" /sc onstart /ru SYSTEM /rl highest /f | Out-Null
    }

    if (-not $NoAudit) {
        # 登录失败审核（4625）：GUID 形式，避免中英文系统子类别名差异
        & auditpol /set /subcategory:"{0CCE9215-69AE-11D9-BED3-505054503030}" /failure:enable | Out-Null
        & auditpol /set /subcategory:"{0CCE9215-69AE-11D9-BED3-505054503030}" /success:enable | Out-Null
        $chk = & auditpol /get /subcategory:"{0CCE9215-69AE-11D9-BED3-505054503030}" 2>&1
        Write-Host "登录审核策略:"
        $chk | ForEach-Object { Write-Host ("  " + $_) }
    }

    # 登录后弹一次状态提示（watch 进程在 session 0，无法在桌面显示界面）
    $notifyTask = 'FaceGuard Notify'
    Unregister-ScheduledTask -TaskName $notifyTask -Confirm:$false -ErrorAction SilentlyContinue
    try {
        $nAction = New-ScheduledTaskAction -Execute (Join-Path $dest 'FaceGuardDaemon.exe') -Argument 'notify' -WorkingDirectory $dest
        $nTrigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
        $nPrincipal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
        $nSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
        $nTask = New-ScheduledTask -Action $nAction -Trigger $nTrigger -Principal $nPrincipal -Settings $nSettings `
            -Description 'FaceGuard 启动提示'
        $nTask.Settings.Hidden = $true
        Register-ScheduledTask -TaskName $notifyTask -InputObject $nTask | Out-Null
        Write-Host "已注册登录提示任务: $notifyTask"
    }
    catch {
        Write-Warning ("登录提示任务注册失败（不影响拍摄）: " + $_.Exception.Message)
    }

    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 2

    Write-Host ""
    Write-Host "安装完成（系统模式）"
    Write-Host ("  程序    : " + $dest)
    Write-Host ("  计划任务: " + $taskName + "（开机自启，SYSTEM）")
    Write-Host ("  照片    : " + $shots)
    Write-Host ("  日志    : " + (Join-Path $data 'FaceGuard.log'))
    Write-Host ""
    Write-Host "验证："
    Write-Host ('  & "' + $dest + '\FaceGuard.exe" diagnose')
    Write-Host '  然后按 Win+L 锁屏 5 秒再解锁，看照片目录是否新增图片。'
    $script:installedExe = Join-Path $dest 'FaceGuard.exe'
}

try {
    if ($Mode -eq 'user') { Install-User } else { Install-System }
}
catch {
    Write-Host ""
    Write-Host ("安装失败: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}

Write-Host ""
if ($script:installedExe -and (Test-Path $script:installedExe)) {
    Write-Host ("状态检查: & '" + $script:installedExe + "' status")
}
