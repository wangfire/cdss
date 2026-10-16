# 本地自签 dev JWT 后导入：
#   $env:DEV_JWT = node -e 'const c=require("node:crypto"),fs=require("fs");const k=JSON.parse(fs.readFileSync("src/HospitalAi.Api/appsettings.Development.json","utf8")).JwtAuth.Key;const b=v=>Buffer.from(typeof v==="string"?v:JSON.stringify(v)).toString("base64url");const n=Math.floor(Date.now()/1000);const h=b({alg:"HS256",typ:"JWT"});const p=b({sub:"cm3-import",nameid:"cm3-import",iss:"hospitalai",aud:"hospitalai-admin",iat:n,nbf:n-30,exp:n+7200,"http://schemas.microsoft.com/ws/2008/06/identity/claims/role":"ADMIN"});const s=c.createHmac("sha256",k).update(h+"."+p).digest("base64url");process.stdout.write(h+"."+p+"."+s)'
# 把 data/knowledge/cm3-index-batches/*.json 逐批 POST 到 CM3 索引库导入接口。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$dir = Join-Path $root "data/knowledge/cm3-index-batches"
$api = if ($env:API_BASE) { $env:API_BASE } else { "http://localhost:5080" }
$hospitalId = if ($env:HOSPITAL_ID) { $env:HOSPITAL_ID } else { "868F2319-9A0C-46C8-B478-C78FB350237D" }

$files = Get-ChildItem $dir -Filter "batch-*.json" | Sort-Object Name
$headers = @{ "X-Hospital-Id" = $hospitalId; "X-User-Id" = "knowledge-import" }
# /cm3-index/import 走 KnowledgeManager 策略（需要 CODER/ADMIN 角色声明），
# 而 X-User-Id 头身份不带角色 → 403。本地跑法：先用 appsettings.Development.json 的
# JwtAuth:Key 自签一个 dev JWT（参考 logs/bootstrap_hospital_1012.mjs 的 mintJwt），再 DEV_JWT 传进来。
if ($env:DEV_JWT) { $headers["Authorization"] = "Bearer $env:DEV_JWT" }
$totalImported = 0
$totalUpdated = 0
foreach ($f in $files) {
    $body = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $resp = Invoke-RestMethod -Method Post -Uri "$api/api/v1/cm3-index/import" `
        -ContentType "application/json; charset=utf-8" `
        -Headers $headers `
        -Body $body
    $totalImported += $resp.data.importedCount
    $totalUpdated += $resp.data.updatedCount
    Write-Host ("{0}: imported={1} updated={2}" -f $f.Name, $resp.data.importedCount, $resp.data.updatedCount)
}
Write-Host ("DONE batches={0} imported={1} updated={2}" -f $files.Count, $totalImported, $totalUpdated)
