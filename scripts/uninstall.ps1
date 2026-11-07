#Requires -Version 5.1
<#
    StudyGate 卸载脚本
    安全阀：不需要口令，不需要答题。任何情况下都能把机器还回来。
    顺序：先还原 Shell（保证能进桌面），再删任务，最后删数据。
#>
[CmdletBinding()]
param(
    [switch]$KeepBank,
    [switch]$KeepLogs
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host "  $msg" }
function Write-Ok($msg)   { Write-Host "  [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [warn] $msg" -ForegroundColor Yellow }

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin   = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host ''
Write-Host 'StudyGate 卸载' -ForegroundColor Cyan
if (-not $isAdmin) { Write-Warn2 '非管理员：还原 Shell 会失败，请用管理员 PowerShell 重跑' }
Write-Host ''

$stateDir = Join-Path $env:ProgramData 'StudyGate'
$wlx      = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'

# ------------------------------------------------ 1. 还原 Shell（最优先）
$restored = $false
if ($isAdmin) {
    $original = $null
    $backup   = Join-Path $stateDir 'shell.bak'
    if (Test-Path $backup) {
        $original = (Get-Content -Path $backup -Raw).Trim()
    }

    # 备份缺失、为空、或被自己写成 StudyGate 时，一律回落到 explorer.exe
    if ([string]::IsNullOrWhiteSpace($original) -or $original -like '*StudyGate*') {
        $original = 'explorer.exe'
        Write-Step '备份无效或缺失，回落为 explorer.exe'
    }

    try {
        Set-ItemProperty -Path $wlx -Name Shell -Value $original -Force
        $now = (Get-ItemProperty -Path $wlx -Name Shell -ErrorAction Stop).Shell
        if ($now -eq $original) {
            Write-Ok "Winlogon Shell 已还原 : $original"
            $restored = $true
        } else {
            Write-Warn2 "还原后读回不一致 : $now"
        }
    } catch {
        Write-Warn2 "还原 Shell 失败 : $($_.Exception.Message)"
        Write-Step '手工修：reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d explorer.exe /f'
    }
} else {
    Write-Warn2 '跳过 Shell 还原（需要管理员）'
}

# ------------------------------------------------ 2. 结束进程
foreach ($name in @('StudyGate', 'StudyGate.Watchdog')) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); Write-Ok "已结束进程 $name (PID $($_.Id))" } catch { Write-Warn2 "结束 $name 失败" }
    }
}
Start-Sleep -Milliseconds 300
Get-Process -Name 'StudyGate*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# ------------------------------------------------ 3. 删计划任务
foreach ($task in @('StudyGateAutostart', 'StudyGate Guard')) {
    $existing = Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
    if ($existing) {
        try {
            Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction Stop
            Write-Ok "已删除计划任务 $task"
        } catch {
            Write-Warn2 "删除任务 $task 失败 : $($_.Exception.Message)"
            Write-Step "手工修：schtasks /delete /tn `"$task`" /f"
        }
    }
}

# ------------------------------------------------ 4. 清 IFEO 软屏蔽
if ($isAdmin) {
    $ifeo = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
    foreach ($exe in @('taskmgr.exe','cmd.exe','powershell.exe','regedit.exe','mmc.exe','msconfig.exe')) {
        $key = Join-Path $ifeo $exe
        if (Test-Path $key) {
            $dbg = (Get-ItemProperty -Path $key -Name Debugger -ErrorAction SilentlyContinue).Debugger
            if ($dbg -and $dbg -like '*StudyGate*') {
                try {
                    Remove-ItemProperty -Path $key -Name Debugger -Force -ErrorAction Stop
                    Write-Ok "已清除 IFEO Debugger : $exe"
                } catch { Write-Warn2 "清除 IFEO $exe 失败" }
            }
        }
    }
}

# ------------------------------------------------ 5. 清数据
if (Test-Path $stateDir) {
    $targets = @('vault.dat', 'state.json', 'state.json.sig', 'shell.bak', '*.tmp')
    foreach ($pattern in $targets) {
        Get-ChildItem -Path $stateDir -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
            try { Remove-Item $_.FullName -Force -ErrorAction Stop; Write-Ok "已删 $($_.Name)" } catch { Write-Warn2 "删除 $($_.Name) 失败" }
        }
    }
    if ($KeepBank) {
        Write-Step "保留题库 : $stateDir\bank"
    } else {
        $bank = Join-Path $stateDir 'bank'
        if (Test-Path $bank) { Remove-Item $bank -Recurse -Force -ErrorAction SilentlyContinue; Write-Ok '已删题库副本' }
    }
    if (-not $KeepLogs) {
        Get-ChildItem -Path $stateDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    $left = Get-ChildItem -Path $stateDir -Recurse -Force -ErrorAction SilentlyContinue
    if (-not $left) {
        Remove-Item $stateDir -Force -ErrorAction SilentlyContinue
        Write-Ok "已删除 $stateDir"
    } else {
        Write-Warn2 "残留项：$($left.Name -join ', ')"
    }
}

# ------------------------------------------------ 6. 收尾
Write-Host ''
if ($restored) {
    Write-Host '卸载完成。桌面恢复正常，无需重启。' -ForegroundColor Green
} else {
    Write-Host '部分步骤未完成（多为权限）。用管理员 PowerShell 重跑本脚本。' -ForegroundColor Yellow
    Write-Host '若无法进桌面：Ctrl+Alt+Del -> 电源 -> 按住 Shift 点重启 -> WinRE 命令行 -> 跑本脚本' -ForegroundColor Yellow
}
Write-Host ''
