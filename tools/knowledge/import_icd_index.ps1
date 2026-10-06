# 把 data/knowledge/icd-index-batches/*.json 逐批 POST 到 ICD 索引库导入接口。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$dir = Join-Path $root "data/knowledge/icd-index-batches"
$api = if ($env:API_BASE) { $env:API_BASE } else { "http://localhost:5080" }
$hospitalId = if ($env:HOSPITAL_ID) { $env:HOSPITAL_ID } else { "868F2319-9A0C-46C8-B478-C78FB350237D" }

$files = Get-ChildItem $dir -Filter "batch-*.json" | Sort-Object Name
$totalImported = 0
$totalUpdated = 0
foreach ($f in $files) {
    $body = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $resp = Invoke-RestMethod -Method Post -Uri "$api/api/v1/icd-index/import" `
        -ContentType "application/json; charset=utf-8" `
        -Headers @{ "X-Hospital-Id" = $hospitalId; "X-User-Id" = "knowledge-import" } `
        -Body $body
    $totalImported += $resp.data.importedCount
    $totalUpdated += $resp.data.updatedCount
    Write-Host ("{0}: imported={1} updated={2}" -f $f.Name, $resp.data.importedCount, $resp.data.updatedCount)
}
Write-Host ("DONE batches={0} imported={1} updated={2}" -f $files.Count, $totalImported, $totalUpdated)
