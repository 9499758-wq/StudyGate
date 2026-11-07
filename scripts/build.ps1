#Requires -Version 5.1
<#
    StudyGate 构建脚本
    在 Windows + .NET 8 SDK 上跑。做：还原 -> 编译 -> 跑 Core 断言 -> 跑题库校验。
    本机（macOS）无 dotnet，此脚本只能在 Windows 上验证。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Verbose
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)

function Step($msg) { Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    [ok] $msg" -ForegroundColor Green }
function Bad($msg)  { Write-Host "    [fail] $msg" -ForegroundColor Red }

Write-Host ''
Write-Host 'StudyGate build' -ForegroundColor Cyan
Write-Host "  root   : $root"
Write-Host "  config : $Configuration"

# ---------------------------------------------------------------- 0. 环境
Step '0. 环境检查'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { Bad 'dotnet 未安装。装 .NET 8 SDK: winget install Microsoft.DotNet.SDK.8'; exit 1 }
$ver = & $dotnet --version
Ok "dotnet $ver"
$sdkList = & $dotnet --list-sdks
if (-not ($sdkList -match '^8\.')) { Bad "--list-sdks 里没有 8.x：`n$sdkList"; exit 1 }
Ok 'SDK 8.x 可用'
if (-not $IsWindows -and $PSVersionTable.PSVersion.Major -ge 6) { Bad '本脚本要求 Windows'; exit 1 }

# ---------------------------------------------------------------- 1. 还原
Step '1. dotnet restore'
& $dotnet restore (Join-Path $root 'StudyGate.sln') --nologo
if ($LASTEXITCODE -ne 0) { Bad 'restore 失败'; exit $LASTEXITCODE }
Ok 'restore 完成'

# ---------------------------------------------------------------- 2. 编译
Step "2. dotnet build -c $Configuration"
$buildArgs = @('build', (Join-Path $root 'StudyGate.sln'), '-c', $Configuration, '--nologo', '--no-restore')
if ($Verbose) { $buildArgs += '-v'; $buildArgs += 'normal' }
& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Bad 'build 失败'; exit $LASTEXITCODE }
Ok "build 完成（TreatWarningsAsErrors 已开启，有警告即失败）"

# ---------------------------------------------------------------- 3. Core 断言
if (-not $SkipTests) {
    Step '3. Core 判分内核断言'
    $testProj = Join-Path $root 'src\Core\Tests\StudyGate.CoreTests.csproj'
    if (-not (Test-Path $testProj)) { Bad "找不到 $testProj"; exit 1 }
    & $dotnet run --project $testProj -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { Bad 'Core 断言失败'; exit $LASTEXITCODE }
    Ok 'Core 断言全绿'
}

# ---------------------------------------------------------------- 4. 题库
Step '4. 题库校验'
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
$validator = Join-Path $root 'data\bank\validate.mjs'
if (-not $node) {
    Bad '未安装 Node，跳过（运行期仍会剔除非法题，但不建议）'
} elseif (-not (Test-Path $validator)) {
    Bad "找不到 $validator"
} else {
    & $node $validator
    if ($LASTEXITCODE -ne 0) { Bad '题库校验失败'; exit $LASTEXITCODE }
    Ok '题库全绿'
}

# ---------------------------------------------------------------- 5. Node 对拍
Step '5. Node 参考实现对拍'
$ref = Join-Path $root 'tools\core-reference.mjs'
if ($node -and (Test-Path $ref)) {
    & $node $ref
    if ($LASTEXITCODE -ne 0) { Bad '对拍失败：C# 与 Node 参考实现逻辑不一致'; exit $LASTEXITCODE }
    Ok '对拍全绿（判分/抽题/HMAC/口令/隐蔽通道一致）'
} else {
    Bad '跳过对拍（缺 node 或 tools\core-reference.mjs）'
}

# ---------------------------------------------------------------- 6. 产物
Step '6. 产物'
$outDir = Join-Path $root "src\App\bin\$Configuration\net8.0-windows"
if (Test-Path $outDir) {
    $files = Get-ChildItem $outDir -File
    Ok "$($files.Count) 个文件 -> $outDir"
    $exe = Join-Path $outDir 'StudyGate.exe'
    if (Test-Path $exe) { Ok "StudyGate.exe $((Get-Item $exe).Length) 字节" } else { Bad 'StudyGate.exe 缺失' }
    $third = $files | Where-Object { $_.Extension -eq '.dll' -and $_.Name -notmatch '^(StudyGate|System|Microsoft)\.' }
    if ($third) { Bad "混入第三方 DLL：$($third.Name -join ', ')" } else { Ok '零第三方依赖' }
} else {
    Bad "产物目录不存在：$outDir"
}

Write-Host ''
Write-Host '构建成功。下一步：' -ForegroundColor Green
Write-Host '  管理员 PowerShell 跑 .\scripts\install.ps1'
Write-Host ''

# ---------------------------------------------------------------- 7. 版本记录
Step '7. 版本记录'
$now = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
Write-Host "    $now / $Configuration / dotnet $ver" -ForegroundColor DarkGray
