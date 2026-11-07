#Requires -Version 5.1
<#
    StudyGate 安装脚本
    做四件事：校验题库 -> 设置口令 -> 劫持 Winlogon Shell -> 注册计划任务
    不启动答题门。装完要重启才生效。
#>
[CmdletBinding()]
param(
    [string]$SourceDir = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)),
    [switch]$SkipBankCheck,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host "  $msg" }
function Write-Ok($msg)   { Write-Host "  [ok] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  [warn] $msg" -ForegroundColor Yellow }
function Write-Bad($msg)  { Write-Host "  [fail] $msg" -ForegroundColor Red }

# ---------------------------------------------------------------- 0. 管理员
$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Bad '需要管理员权限。'
    Write-Step '右键 install.ps1 -> 以管理员身份运行 PowerShell，或执行：'
    Write-Step "  Start-Process powershell -Verb RunAs -ArgumentList '-ExecutionPolicy','Bypass','-File','$PSCommandPath'"
    Read-Host '按回车关闭'
    exit 1
}

Write-Host ''
Write-Host 'StudyGate 安装' -ForegroundColor Cyan
Write-Host "  source : $SourceDir"
Write-Host ''

# ---------------------------------------------------------- 1. 定位产物
$appExe = Join-Path $SourceDir 'src\App\bin\Release\net8.0-windows\StudyGate.exe'
if (-not (Test-Path $appExe)) {
    $alt = Join-Path $SourceDir 'src\App\bin\Debug\net8.0-windows\StudyGate.exe'
    if (Test-Path $alt) { $appExe = $alt } else { throw "找不到 StudyGate.exe。先跑 dotnet build -c Release。期望路径：$appExe" }
}
Write-Ok "可执行文件 : $appExe"

$bankDir = Join-Path $SourceDir 'data\bank'
if (-not (Test-Path $bankDir)) { throw "找不到题库目录：$bankDir" }

# ---------------------------------------------------------- 2. 校验题库
if (-not $SkipBankCheck) {
    $node = (Get-Command node -ErrorAction SilentlyContinue).Source
    if ($node) {
        $validator = Join-Path $bankDir 'validate.mjs'
        if (Test-Path $validator) {
            Write-Step '校验题库...'
            & $node $validator | Out-Host
            if ($LASTEXITCODE -ne 0) { throw '题库校验未通过，中止安装。加 -SkipBankCheck 可跳过（不建议）。' }
            Write-Ok '题库校验通过'
        } else {
            Write-Warn2 '未找到 validate.mjs，跳过题库校验'
        }
    } else {
        Write-Warn2 '未安装 Node，跳过题库校验（运行期仍会校验，非法题会被剔除）'
    }
}

# ---------------------------------------------------------- 3. 口令
$stateDir = Join-Path $env:ProgramData 'StudyGate'
if (-not (Test-Path $stateDir)) { New-Item -Path $stateDir -ItemType Directory -Force | Out-Null }
$vaultPath = Join-Path $stateDir 'vault.dat'

if ((Test-Path $vaultPath) -and -not $Force) {
    Write-Warn2 "已存在口令文件：$vaultPath"
    Write-Step '要重设口令，加 -Force 重跑本脚本。'
} else {
    Write-Host ''
    Write-Host '设置管理员口令（隐蔽通道用，界面不会有任何提示）' -ForegroundColor Yellow
    Write-Step '要求：长度 >= 12，含数字或符号。记住它 —— 忘了就只能走 WinRE 卸载。'
    Write-Host ''
    do {
        $pwd1 = Read-Host '口令'
        if ($pwd1.Length -lt 12) { Write-Bad '至少 12 位' ; continue }
        $hasDigit   = $pwd1 -match '\d'
        $hasSymbol  = $pwd1 -match '[^\w]'
        if (-not ($hasDigit -or $hasSymbol)) { Write-Bad '至少含一个数字或符号' ; continue }
        $pwd2 = Read-Host '再输一次'
        if ($pwd1 -ne $pwd2) { Write-Bad '两次不一致' ; continue }
        break
    } while ($true)

    # 交给程序写入（PBKDF2 逻辑只在 C# 侧一份，脚本不重复实现）
    Push-Location (Split-Path -Parent $appExe)
    try {
        $env:STUDYGATE_SET_PWD = $pwd1
        & $appExe --install --set-password | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "设置口令失败（退出码 $LASTEXITCODE）" }
    } finally {
        Remove-Item Env:\STUDYGATE_SET_PWD -ErrorAction SilentlyContinue
        Pop-Location
    }
    $pwd1 = $null; $pwd2 = $null
    [GC]::Collect()
    Write-Ok "口令已写入 : $vaultPath"
}

# ------------------------------------------------ 4. 备份并劫持 Shell
$wlx = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$currentShell = (Get-ItemProperty -Path $wlx -Name Shell -ErrorAction SilentlyContinue).Shell
if (-not $currentShell) { $currentShell = 'explorer.exe' }

$backup = Join-Path $stateDir 'shell.bak'
if (-not (Test-Path $backup)) {
    Set-Content -Path $backup -Value $currentShell -Encoding ASCII -NoNewline
    Write-Ok "原 Shell 已备份 : $currentShell -> $backup"
} else {
    Write-Step "已有备份，沿用 : $(Get-Content $backup -Raw)"
}

$target = '"{0}" --shell' -f $appExe
Set-ItemProperty -Path $wlx -Name Shell -Value $target -Force
Write-Ok "Winlogon Shell 已改为 : $target"

# ------------------------------------------------ 5. 计划任务
$taskName = 'StudyGateAutostart'
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Step "已清理旧任务 $taskName"
}

$action = New-ScheduledTaskAction -Execute $appExe -Argument '--watchdog' -WorkingDirectory (Split-Path -Parent $appExe)

$atLogon = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
# 每 30 秒复查，持续 3650 天：杀掉主进程最多多活 30 秒
$repeat = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(30) `
            -RepetitionInterval (New-TimeSpan -Seconds 30) `
            -RepetitionDuration (New-TimeSpan -Days 3650)

$principalDef = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
                  -LogonType Interactive -RunLevel Highest

$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
              -StartWhenAvailable -MultipleInstances IgnoreNew `
              -ExecutionTimeLimit ([TimeSpan]::Zero) -Hidden `
              -DisallowHardTerminate -Priority 4

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($atLogon, $repeat) `
    -Principal $principalDef -Settings $settings -Force | Out-Null
Write-Ok "计划任务已注册 : $taskName（登录触发 + 每 30 秒复查，最高权限，隐藏）"

# ------------------------------------------------ 6. 复制题库到 ProgramData
$bankTarget = Join-Path $stateDir 'bank'
if (Test-Path $bankTarget) { Remove-Item $bankTarget -Recurse -Force }
New-Item -Path $bankTarget -ItemType Directory -Force | Out-Null
Get-ChildItem -Path $bankDir -Filter '*.json' | Copy-Item -Destination $bankTarget -Force
$jsonCount = (Get-ChildItem -Path $bankTarget -Filter '*.json').Count
Write-Ok "题库已复制 : $jsonCount 个文件 -> $bankTarget"

# ------------------------------------------------ 7. 收尾
Write-Host ''
Write-Host '安装完成。重启生效。' -ForegroundColor Cyan
Write-Host ''
Write-Step '重启后：登录 -> 全屏答题门 -> 20 题 -> 达到 90 分才进桌面'
Write-Step "撤回：管理员 PowerShell 跑 .\scripts\uninstall.ps1（不需要口令）"
Write-Step '被锁在门外无法进桌面时：Ctrl+Alt+Del -> 电源 -> 按住 Shift 点重启 -> WinRE 命令行 -> 跑 uninstall.ps1'
Write-Host ''
$answer = Read-Host '现在重启？(y/N)'
if ($answer -eq 'y' -or $answer -eq 'Y') { shutdown /r /t 5 /c "StudyGate installed" }
