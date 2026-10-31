# =====================================================================
# CDSS 生产一键部署脚本（WP-A）
# 前置：deploy/secrets.env 已按 secrets.env.template 填写；
#       前端已构建（web-ui/apps/admin/dist → deploy/nginx/frontend-dist）；
#       TLS 证书已放 deploy/nginx/certs/。
# 用法：pwsh -File deploy/deploy.ps1 [-SkipBuild] [-Rollback]
# =====================================================================
param(
    [switch]$SkipBuild,
    [switch]$Rollback
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$composeFile = "deploy/docker-compose.prod.yml"
$secretsFile = "deploy/secrets.env"

if (-not (Test-Path $secretsFile)) {
    Write-Host "[X] 缺少 $secretsFile —— 请复制 secrets.env.template 并填写真实密钥。" -ForegroundColor Red
    exit 1
}

if ($Rollback) {
    Write-Host "[回滚] 使用上一版本镜像重启 api/worker..." -ForegroundColor Yellow
    docker compose -f $composeFile --env-file $secretsFile up -d --no-build api worker
    exit 0
}

# 1. 前端构建（除非 -SkipBuild）
if (-not $SkipBuild) {
    Write-Host "[1/4] 构建前端（vite production）..." -ForegroundColor Cyan
    Push-Location web-ui
    try {
        pnpm --filter @tabtab/admin build
        if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
    }
    finally { Pop-Location }

    $dist = "web-ui/apps/admin/dist"
    if (-not (Test-Path $dist)) { throw "前端产物 $dist 不存在" }
    Write-Host "[1/4] 同步前端产物到 deploy/nginx/frontend-dist..." -ForegroundColor Cyan
    if (Test-Path "deploy/nginx/frontend-dist") {
        Remove-Item "deploy/nginx/frontend-dist" -Recurse -Force
    }
    Copy-Item $dist "deploy/nginx/frontend-dist" -Recurse
}
else {
    Write-Host "[1/4] 跳过前端构建（-SkipBuild）"
}

# 2. 校验关键配置
Write-Host "[2/4] 校验密钥文件关键项..." -ForegroundColor Cyan
$secrets = Get-Content $secretsFile -Encoding utf8 | Where-Object { $_ -match '^HOSPITAL_AI_JWT_KEY=' }
if (-not $secrets -or $secrets -match 'REPLACE-WITH') {
    Write-Host "[X] secrets.env 中 HOSPITAL_AI_JWT_KEY 未替换（生成：openssl rand -base64 48）" -ForegroundColor Red
    exit 1
}

# 3. 构建镜像并启动
Write-Host "[3/4] 构建镜像并启动服务栈..." -ForegroundColor Cyan
docker compose -f $composeFile --env-file $secretsFile up -d --build
if ($LASTEXITCODE -ne 0) { throw "compose up 失败" }

# 4. 健康检查轮询
Write-Host "[4/4] 等待服务 healthy..." -ForegroundColor Cyan
$deadline = (Get-Date).AddMinutes(8)
$services = @("hospitalai-prod-sqlserver", "hospitalai-prod-elasticsearch", "hospitalai-prod-rabbitmq", "hospitalai-prod-api")
foreach ($svc in $services) {
    while ((Get-Date) -lt $deadline) {
        $state = docker inspect --format '{{.State.Health.Status}}' $svc 2>$null
        if ($state -eq "healthy") { Write-Host "  $svc healthy" -ForegroundColor Green; break }
        Start-Sleep 8
    }
    if ($state -ne "healthy") { Write-Host "[X] $svc 未达到 healthy（$state）" -ForegroundColor Red; exit 1 }
}

# 5. 冒烟探针
Write-Host "[5/5] 冒烟探针..." -ForegroundColor Cyan
$probe = docker exec hospitalai-prod-nginx curl -sf -k https://localhost/api/v1/health 2>&1
if ($probe -match '"status":"ok"') {
    Write-Host "✅ 部署完成：https://<server>/ 可访问，API 健康。" -ForegroundColor Green
}
else {
    Write-Host "[!] API 探针未通过，请检查 nginx 与 api 日志：docker logs hospitalai-prod-api" -ForegroundColor Yellow
}
