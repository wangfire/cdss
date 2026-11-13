# =====================================================================
# CDSS 一键验证脚本（阶段 0 建立）
# 目的：把「构建 → 后端测试 → 知识库测试」串成一条命令，
#      让每次缺陷修复都有「改前绿、改后也绿」的证据。
#
# 用法：
#   powershell -NoProfile -File scripts\verify.ps1
#   powershell -NoProfile -File scripts\verify.ps1 -SkipBuild
#   powershell -NoProfile -File scripts\verify.ps1 -Frontend   # 追加前端类型检查
#
# 环境说明（重要）：
#   1. 本机 .NET SDK 10.0.401 缺少 workload resolver 目录，
#      `dotnet build` / `dotnet test` 会报 MSB4276。
#      因此本脚本直接调用 MSBuild.exe 与 vstest.console.dll 绕过 CLI 路径。
#      长期修复：winget upgrade Microsoft.Dotnet.SDK.10
#   2. 测试库用 LocalDB（Docker Desktop 未运行，且本机 SQL Server 的 sa 口令未知）。
#      需要 HospitalAiTests 库已存在（首次可用下面的 -CreateTestDb 开关创建）。
# =====================================================================
param(
    [switch]$SkipBuild,
    [switch]$Frontend,
    [switch]$CreateTestDb
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$msb = 'C:\Program Files\dotnet\sdk\10.0.401\MSBuild.exe'
$vst = 'C:\Program Files\dotnet\sdk\10.0.401\vstest.console.dll'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

# 测试库连接串：LocalDB + Windows 集成认证。
# 覆盖方式：设置同名环境变量 HOSPITAL_AI_TEST_CONNECTION_STRING 即可。
$testConn = if ($env:HOSPITAL_AI_TEST_CONNECTION_STRING) {
    $env:HOSPITAL_AI_TEST_CONNECTION_STRING
} else {
    'Server=(localdb)\MSSQLLocalDB;Database=HospitalAiTests;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;'
}
$env:HOSPITAL_AI_TEST_CONNECTION_STRING = $testConn

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

if (-not (Test-Path $msb)) { throw "找不到 MSBuild：$msb" }
if (-not (Test-Path $vst)) { throw "找不到 vstest.console：$vst" }

# ---------- 可选：创建测试库 ----------
if ($CreateTestDb) {
    Write-Step "创建测试库 HospitalAiTests（LocalDB）"
    $master = $testConn -replace 'Database=[^;]*', 'Database=master'
    $conn = New-Object System.Data.SqlClient.SqlConnection $master
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "IF DB_ID('HospitalAiTests') IS NULL CREATE DATABASE [HospitalAiTests]"
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host "  测试库就绪。" -ForegroundColor Green
    } finally { $conn.Close() }
}

# ---------- 构建 ----------
if (-not $SkipBuild) {
    Write-Step "构建解决方案（13 个项目）"
    & $msb HospitalAi.slnx -restore -nologo -v:m
    if ($LASTEXITCODE) { throw "构建失败（exit=$LASTEXITCODE）" }
}

# ---------- 后端测试 ----------
# 注意：Start-Process -PassThru 在重定向输出时进程对象的 ExitCode 为空，
#       因此改为解析测试输出里的「已通过!/失败!」判断成败。
Write-Step "运行后端测试（5 个项目）"
$failed = @()
foreach ($p in 'Domain', 'Application', 'Infrastructure', 'Worker', 'Api') {
    $dll = "tests\HospitalAi.$p.Tests\bin\Debug\net10.0\HospitalAi.$p.Tests.dll"
    if (-not (Test-Path $dll)) { $failed += "$p (未构建: $dll)"; continue }

    $log = Join-Path $env:TEMP "cdss-verify-$p.txt"
    $proc = Start-Process -FilePath $dotnet `
        -ArgumentList "`"$vst`"", "`"$dll`"", '--logger:console;verbosity=minimal' `
        -WorkingDirectory $root -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err"

    if (-not $proc.WaitForExit(600000)) {
        $proc.Kill()
        $failed += "$p (超时)"
        continue
    }

    $line = (Get-Content $log -Encoding UTF8 -ErrorAction SilentlyContinue |
        Select-String '已通过!|失败!' | Select-Object -Last 1).Line
    if ($line -notmatch '已通过!') {
        $failed += "$p -> $line"
        Write-Host "  $p : $line" -ForegroundColor Red
    } else {
        Write-Host "  $p : $line" -ForegroundColor Green
    }
}

# ---------- 知识库（Python）----------
Write-Step "运行知识库测试（Python）"
py -3 -m unittest discover -s tests\knowledge -v
if ($LASTEXITCODE) { $failed += "tests/knowledge (unittest)" }

py -3 tools\knowledge\build_knowledge.py --self-test
if ($LASTEXITCODE) { $failed += "build_knowledge.py --self-test" }

# ---------- 前端类型检查（可选）----------
if ($Frontend) {
    Write-Step "前端类型检查（vue-tsc）"
    Push-Location "$root\web-ui"
    try {
        pnpm exec vue-tsc --noEmit -p apps/admin/tsconfig.app.json
        if ($LASTEXITCODE) { $failed += "vue-tsc" }
    } finally { Pop-Location }
}

# ---------- 汇总 ----------
Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "验证失败：" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "全部验证通过。" -ForegroundColor Green
exit 0
