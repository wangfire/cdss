# 演示环境一键播种（开发/演示专用，勿用于生产）：
#   1. 用开发签名密钥铸造 ADMIN JWT（B2-5 后导入端点已挂 RBAC，占位头不再放行）
#   2. 建医院 → 角色(ADMIN/CODER) → admin 用户（默认密码 admin123）→ 角色绑定
#   3. 导入 ICD-10 / ICD-9-CM-3 字典、医保规则（含术语同义词）、52 批 ICD 索引树
# 前置：API 已在 http://localhost:5080 运行（Development 环境）。
$ErrorActionPreference = "Stop"
$api = if ($env:API_BASE) { $env:API_BASE } else { "http://localhost:5080" }
$key = if ($env:DEV_JWT_KEY) { $env:DEV_JWT_KEY } else { "DEV-ONLY-DO-NOT-USE-IN-PRODUCTION-0123456789abcdef" }
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-DevJwt([string]$SigningKey, [string]$HospitalId) {
    $roleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header = @{ alg = "HS256"; typ = "JWT" } | ConvertTo-Json -Compress
    $payload = @{
        sub        = "seed-script"
        iss        = "hospitalai"
        aud        = "hospitalai-admin"
        hospitalId = $HospitalId
        jti        = [Guid]::NewGuid().ToString("N")
        $roleClaim = "ADMIN"
        nbf        = $now - 30
        iat        = $now
        exp        = $now + 7200
    } | ConvertTo-Json -Compress
    $headB64 = ConvertTo-Base64Url ([System.Text.Encoding]::ASCII.GetBytes($header))
    $payloadB64 = ConvertTo-Base64Url ([System.Text.Encoding]::UTF8.GetBytes($payload))
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($SigningKey))
    $sig = ConvertTo-Base64Url ($hmac.ComputeHash([System.Text.Encoding]::ASCII.GetBytes("$headB64.$payloadB64")))
    return "$headB64.$payloadB64.$sig"
}

function Post-Json([string]$Uri, [object]$Headers, [string]$Body, [int]$TimeoutSec = 600) {
    Invoke-RestMethod -Method Post -Uri $Uri -Headers $Headers `
        -ContentType "application/json; charset=utf-8" -Body $Body -TimeoutSec $TimeoutSec
}

# ===== 1. 医院（先铸临时 JWT：建医院端点本身已挂 RBAC）=====
$bootstrapToken = New-DevJwt $key ([Guid]::NewGuid().ToString())
$bootstrapHeaders = @{ Authorization = "Bearer $bootstrapToken" }
$hospitalCode = "DEMO-" + (Get-Date -Format "MMddHHmm")
$hospital = Post-Json "$api/api/v1/hospitals" $bootstrapHeaders `
    -Body (@{ code = $hospitalCode; name = "演示医院" } | ConvertTo-Json -Compress)
$hospitalId = $hospital.data.id
Write-Host ("[1/6] 医院已创建: {0} (hospitalId={1})" -f $hospitalCode, $hospitalId)

# ===== 2. 用真实医院 ID 铸正式 JWT，建角色 / 管理员 =====
$token = New-DevJwt $key $hospitalId
$headers = @{ Authorization = "Bearer $token"; "X-Hospital-Id" = $hospitalId }

$adminRole = Post-Json "$api/api/v1/roles" $headers `
    -Body (@{ code = "ADMIN"; name = "管理员"; description = "演示环境管理员" } | ConvertTo-Json -Compress)
$coderRole = Post-Json "$api/api/v1/roles" $headers `
    -Body (@{ code = "CODER"; name = "编码员"; description = "病案编码员" } | ConvertTo-Json -Compress)
$admin = Post-Json "$api/api/v1/users" $headers `
    -Body (@{ code = "admin"; displayName = "系统管理员" } | ConvertTo-Json -Compress)
Post-Json "$api/api/v1/users/$($admin.data.id)/roles/$($adminRole.data.id)" $headers -Body "{}" | Out-Null
Post-Json "$api/api/v1/users/$($admin.data.id)/roles/$($coderRole.data.id)" $headers -Body "{}" | Out-Null
Write-Host ("[2/6] admin 用户已创建（默认密码 admin123），已绑定 ADMIN/CODER")

# ===== 3. 字典与规则 =====
$icd10 = Get-ChildItem (Join-Path $root "data/knowledge/icd10-2026.json")
$resp = Post-Json "$api/api/v1/code-systems/import" $headers (Get-Content $icd10.FullName -Raw -Encoding UTF8)
Write-Host ("[3/6] ICD-10 导入: imported={0} updated={1}" -f $resp.data.importedCount, $resp.data.updatedCount)

$icd9 = Get-ChildItem (Join-Path $root "data/knowledge/icd9cm3-2026.json")
$resp = Post-Json "$api/api/v1/code-systems/import" $headers (Get-Content $icd9.FullName -Raw -Encoding UTF8)
Write-Host ("[3/6] ICD-9-CM-3 导入: imported={0} updated={1}" -f $resp.data.importedCount, $resp.data.updatedCount)

$rules = Get-ChildItem (Join-Path $root "data/knowledge/coding-rules-2026.json")
$resp = Post-Json "$api/api/v1/coding-rules/import" $headers (Get-Content $rules.FullName -Raw -Encoding UTF8)
Write-Host ("[3/6] 规则+同义词导入: synonyms={0} rules={1}" -f $resp.data.synonymCount, $resp.data.ruleCount)

# ===== 4. ICD 索引树（52 批） =====
$dir = Join-Path $root "data/knowledge/icd-index-batches"
$files = Get-ChildItem $dir -Filter "batch-*.json" | Sort-Object Name
$totalImported = 0
$totalUpdated = 0
$i = 0
foreach ($f in $files) {
    $i++
    $resp = Post-Json "$api/api/v1/icd-index/import" $headers `
        (Get-Content $f.FullName -Raw -Encoding UTF8) -TimeoutSec 300
    $totalImported += $resp.data.importedCount
    $totalUpdated += $resp.data.updatedCount
    Write-Host ("  batch {0}/{1}: +{2} ~{3}" -f $i, $files.Count, $resp.data.importedCount, $resp.data.updatedCount)
}
Write-Host ("[4/6] ICD 索引树导入完成: imported={0} updated={1}" -f $totalImported, $totalUpdated)

# ===== 5. CM3 手术索引树（20 批，编码录入手术链路 + 知识库 CM3 页数据源） =====
$dir = Join-Path $root "data/knowledge/cm3-index-batches"
$files = Get-ChildItem $dir -Filter "batch-*.json" | Sort-Object Name
$totalImported = 0
$totalUpdated = 0
$i = 0
foreach ($f in $files) {
    $i++
    $resp = Post-Json "$api/api/v1/cm3-index/import" $headers `
        (Get-Content $f.FullName -Raw -Encoding UTF8) -TimeoutSec 300
    $totalImported += $resp.data.importedCount
    $totalUpdated += $resp.data.updatedCount
    Write-Host ("  batch {0}/{1}: +{2} ~{3}" -f $i, $files.Count, $resp.data.importedCount, $resp.data.updatedCount)
}
Write-Host ("[5/6] CM3 手术索引树导入完成: imported={0} updated={1}" -f $totalImported, $totalUpdated)

# ===== 6. 肿瘤索引（种子 SQL + 部位补丁；编码录入形态学链路 + 知识库肿瘤页数据源） =====
# SQL 直接打库（脚本由 build_tumor_index_seed.py 生成，幂等：全删全插）。
# 连接串：优先 HOSPITAL_AI_CONNECTION_STRING，回落 LocalDB（与 API 开发配置一致）。
$seedConn = if ($env:HOSPITAL_AI_CONNECTION_STRING) {
    $env:HOSPITAL_AI_CONNECTION_STRING
} else {
    "Server=(localdb)\MSSQLLocalDB;Database=HospitalAi;Integrated Security=True;TrustServerCertificate=True;Encrypt=False"
}
$sqlFiles = @(
    "data/knowledge/tumor-index-seed.sql",
    "data/knowledge/tumor-index-site-patch.sql"
)
$db = New-Object System.Data.SqlClient.SqlConnection $seedConn
$db.Open()
try {
    foreach ($rel in $sqlFiles) {
        $path = Join-Path $root $rel
        if (-not (Test-Path $path)) {
            Write-Host ("[6/6] 跳过缺失文件: {0}" -f $rel) -ForegroundColor Yellow
            continue
        }
        $cmd = $db.CreateCommand()
        $cmd.CommandTimeout = 300
        $cmd.CommandText = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
        $cmd.ExecuteNonQuery() | Out-Null
        Write-Host ("  applied: {0}" -f $rel)
    }
    $cmd = $db.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM tumor_index"
    $tumorRows = $cmd.ExecuteScalar()
    Write-Host ("[6/6] 肿瘤索引导入完成: tumor_index={0} 行" -f $tumorRows)
}
finally {
    $db.Close()
}

Write-Host ""
Write-Host "==== 演示环境就绪 ===="
Write-Host ("前端地址:   http://localhost:3001")
Write-Host ("登录账号:   admin / admin123")
Write-Host ("医院 ID:    $hospitalId")
Write-Host ("医院代码:   $hospitalCode")
