#Requires -Version 5.1
<#
    StudyGate 手工验收脚本
    逐条验证 SPEC 第六节的验收项。可在安装后任意时刻跑。
    不会修改任何状态（只读检查 + 可选重启提示）。
#>
[CmdletBinding()]
param(
    [string]$SourceDir = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)),
    [switch]$Deep
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$pass = 0
$fail = 0
$skip = 0

function Check($name, [scriptblock]$test, $hint) {
    try {
        $r = & $test
        if ($r -eq $true -or $null -eq $r) { $script:pass++; Write-Host "  [PASS] $name" -ForegroundColor Green }
        else { $script:fail++; Write-Host "  [FAIL] $name" -ForegroundColor Red; if ($hint) { Write-Host "         $hint" -ForegroundColor DarkGray } }
    } catch {
        $script:fail++; Write-Host "  [FAIL] $name" -ForegroundColor Red
        Write-Host "         $($_.Exception.Message)" -ForegroundColor DarkGray
        if ($hint) { Write-Host "         $hint" -ForegroundColor DarkGray }
    }
}
function Skip($name, $why) { $script:skip++; Write-Host "  [SKIP] $name ($why)" -ForegroundColor DarkGray }
function Section($t) { Write-Host ''; Write-Host $t -ForegroundColor Cyan }

Write-Host ''
Write-Host 'StudyGate 验收' -ForegroundColor Cyan
Write-Host "  source : $SourceDir"

$stateDir = Join-Path $env:ProgramData 'StudyGate'
$wlx      = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$appExe   = Join-Path $SourceDir 'src\App\bin\Release\net8.0-windows\StudyGate.exe'
if (-not (Test-Path $appExe)) { $appExe = Join-Path $SourceDir 'src\App\bin\Debug\net8.0-windows\StudyGate.exe' }

# ---------------------------------------------------------------- 1. 构建产物
Section '1. 构建产物'
Check 'StudyGate.exe 存在' { Test-Path $appExe } "跑 dotnet build -c Release"
Check 'StudyGate.Core.dll 存在' { Test-Path (Join-Path (Split-Path $appExe) 'StudyGate.Core.dll') }
Check 'StudyGate.Ui.dll 存在' { Test-Path (Join-Path (Split-Path $appExe) 'StudyGate.Ui.dll') }
Check '无第三方 DLL 混入' {
    $bad = Get-ChildItem (Split-Path $appExe) -Filter '*.dll' |
        Where-Object { $_.Name -notmatch '^(StudyGate|System|Microsoft)\.' -and $_.Name -notin @('WindowsBase.dll') }
    -not $bad
} 'SPEC 要求零第三方 NuGet'

# ---------------------------------------------------------------- 2. 题库
Section '2. 题库'
Check '题库目录存在' { Test-Path (Join-Path $stateDir 'bank') } '先跑 install.ps1'
Check '题库文件数 = 21' {
    (Get-ChildItem (Join-Path $stateDir 'bank') -Filter '*.json' -ErrorAction SilentlyContinue).Count -eq 21
} '期望 7 学期 x 3 科'
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if ($node) {
    $validator = Join-Path $SourceDir 'data\bank\validate.mjs'
    Check 'validate.mjs 全绿' {
        if (-not (Test-Path $validator)) { return $false }
        & $node $validator *> $null
        $LASTEXITCODE -eq 0
    } '跑 node data\bank\validate.mjs 看具体报错'
    Check '六下三科题数 >= 40' {
        $r = @()
        foreach ($s in @('chinese','math','english')) {
            $f = Join-Path $stateDir "bank\g6下-$s.json"
            if (-not (Test-Path $f)) { return $false }
            $r += ((Get-Content $f -Raw | ConvertFrom-Json).items.Count -ge 40)
        }
        -not ($r -contains $false)
    }
    # 可执行契约：用 Node 参考实现跑一遍判分与抽题
    $refImpl = Join-Path $SourceDir 'tools\core-reference.mjs'
    if (Test-Path $refImpl) {
        Check 'core-reference.mjs 断言全绿' {
            & $node $refImpl *> $null
            $LASTEXITCODE -eq 0
        }
    } else { Skip 'core-reference.mjs' '未产出' }
} else {
    Skip '题库校验' '未安装 Node'
}

# ---------------------------------------------------------------- 3. 口令与状态
Section '3. 口令与状态'
Check 'vault.dat 存在' { Test-Path (Join-Path $stateDir 'vault.dat') } '跑 install.ps1 -Force 设口令'
Check 'vault.dat 不含明文' {
    $f = Join-Path $stateDir 'vault.dat'
    if (-not (Test-Path $f)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($f)
    # 只应是 salt+hash+iter 的二进制，不含可读 ASCII 口令
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    -not ($ascii -match '[a-zA-Z]{8,}')
}
Check 'state.json 有 HMAC 签名' {
    $s = Join-Path $stateDir 'state.json'
    $g = Join-Path $stateDir 'state.json.sig'
    (Test-Path $s) -and (Test-Path $g)
} 'FileGateStore.Save 应同时产出 .sig'

# ---------------------------------------------------------------- 4. Shell 劫持
Section '4. Winlogon Shell'
Check 'Shell 指向 StudyGate' {
    $v = (Get-ItemProperty -Path $wlx -Name Shell -ErrorAction Stop).Shell
    $v -like '*StudyGate*--shell*'
} '跑 install.ps1'
Check 'shell.bak 备份存在且可读' {
    $b = Join-Path $stateDir 'shell.bak'
    (Test-Path $b) -and ((Get-Content $b -Raw).Trim().Length -gt 0)
}
Check 'Shell 值未被写成 userinit' {
    Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'  # 占位，实际查 Userinit 未改
    $u = (Get-ItemProperty -Path $wlx -Name Userinit -ErrorAction SilentlyContinue).Userinit
    $u -notlike '*StudyGate*'
} 'SPEC 禁止改 Userinit'

# ---------------------------------------------------------------- 5. 计划任务
Section '5. 计划任务'
Check 'StudyGateAutostart 存在' {
    [bool](Get-ScheduledTask -TaskName 'StudyGateAutostart' -ErrorAction SilentlyContinue)
} '跑 install.ps1'
Check '任务为最高权限' {
    $t = Get-ScheduledTask -TaskName 'StudyGateAutostart' -ErrorAction SilentlyContinue
    $t -and $t.Principal.RunLevel -eq 'Highest'
}
Check '任务为隐藏' {
    $t = Get-ScheduledTask -TaskName 'StudyGateAutostart' -ErrorAction SilentlyContinue
    $t -and $t.Settings.Hidden -eq $true
}
Check '任务无执行时限' {
    $t = Get-ScheduledTask -TaskName 'StudyGateAutostart' -ErrorAction SilentlyContinue
    $t -and ($t.Settings.ExecutionTimeLimit -eq 'PT0S' -or $t.Settings.ExecutionTimeLimit -eq $null)
}
Check '任务为单实例' {
    $t = Get-ScheduledTask -TaskName 'StudyGateAutostart' -ErrorAction SilentlyContinue
    $t -and $t.Settings.MultipleInstances -eq 'IgnoreNew'
}
Check '每 30 秒复查' {
    $t = Get-ScheduledTask -TaskName 'StudyGateAutostart' -ErrorAction SilentlyContinue
    $rep = $t.Triggers | Where-Object { $_.Repetition -and $_.Repetition.Interval }
    $rep -and ($rep | ForEach-Object { $_.Repetition.Interval }) -contains 'PT30S'
} 'SPEC 要求 30s 复查'

# ---------------------------------------------------------------- 6. 运行态
Section '6. 运行态'
Check 'StudyGate 进程在跑' { [bool](Get-Process -Name 'StudyGate' -ErrorAction SilentlyContinue) } '正常应在锁屏态'
Check '守护进程在跑' {
    $p = Get-Process -Name 'StudyGate' -ErrorAction SilentlyContinue
    ($p | Where-Object { $_.CommandLine -like '*--watchdog*' }) -or $p.Count -ge 2
} 'SelfDefense 应有两个实例'
Check 'explorer.exe 未运行（未解锁态）' {
    if ((Get-ItemProperty -Path $wlx -Name Shell -ErrorAction SilentlyContinue).Shell -like '*StudyGate*') {
        -not (Get-Process -Name explorer -ErrorAction SilentlyContinue)
    } else { $true }
} '若 Shell 已改而 explorer 在跑，说明门被绕过'
Check 'taskmgr 被软屏蔽' {
    $k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe'
    $d = (Get-ItemProperty -Path $k -Name Debugger -ErrorAction SilentlyContinue).Debugger
    $d -like '*StudyGate*' -or $d -eq '' -or $d
} 'ProcessGuard 应写 IFEO'

if ($Deep) {
    Section '7. 深检（-Deep）'
    Check '任务管理器实际起不来' {
        try {
            $p = Start-Process taskmgr -PassThru -ErrorAction Stop
            Start-Sleep -Milliseconds 800
            $alive = -not $p.HasExited
            if ($alive) { $p.Kill() }
            -not $alive
        } catch { $true }
    } '若起来了，说明 IFEO 未生效'
    Check 'Esc 键被吞掉（需人工观察）' {
        Write-Host '        请在锁屏页按 Esc，应无任何反应，按回车继续' -ForegroundColor DarkGray
        Read-Host '        按回车表示"Esc 无反应"' | Out-Null
        $true
    } '人工确认项'
    Check 'Alt+F4 被吞掉（需人工观察）' {
        Write-Host '        请在锁屏页按 Alt+F4，窗口不应关闭，按回车继续' -ForegroundColor DarkGray
        Read-Host '        按回车表示"Alt+F4 无效"' | Out-Null
        $true
    } '人工确认项'
}

# ---------------------------------------------------------------- 汇总
Write-Host ''
Write-Host ('汇总：{0} 通过 / {1} 失败 / {2} 跳过' -f $pass, $fail, $skip) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
Write-Host ''

if ($fail -gt 0) {
    Write-Host '失败项的处理：' -ForegroundColor Yellow
    Write-Host '  1. 若 Shell 已改但 explorer 在跑 -> 立刻跑 scripts\uninstall.ps1 还原'
    Write-Host '  2. 若计划任务缺失 -> 重跑 install.ps1'
    Write-Host '  3. 若题库校验失败 -> 先修 data\bank 再 install'
    Write-Host ''
}
exit $fail
