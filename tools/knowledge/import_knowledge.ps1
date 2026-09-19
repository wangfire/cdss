param(
    [string]$ApiBaseUrl = "http://localhost:5080",
    [Parameter(Mandatory = $true)][string]$HospitalId,
    [string]$KnowledgeDirectory = "data/knowledge",
    [int]$BatchSize = 1000
)

$ErrorActionPreference = "Stop"

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][object]$Body
    )

    Invoke-RestMethod `
        -Method $Method `
        -Uri $Uri `
        -Headers @{ "X-Hospital-Id" = $HospitalId; "X-User-Id" = "knowledge-import" } `
        -ContentType "application/json" `
        -Body ($Body | ConvertTo-Json -Depth 8)
}

function Import-CodeSystem {
    param([string]$Path)

    $document = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    $codes = @($document.codes)
    $totalImported = 0
    $totalUpdated = 0
    for ($offset = 0; $offset -lt $codes.Count; $offset += $BatchSize) {
        $end = [Math]::Min($offset + $BatchSize, $codes.Count)
        $batch = @($codes[$offset..($end - 1)])
        $response = Invoke-JsonRequest `
            -Method Post `
            -Uri "$ApiBaseUrl/api/v1/code-systems/import" `
            -Body @{
                codeSystem = $document.codeSystem
                version = $document.version
                codes = $batch
            }
        $totalImported += [int]$response.importedCount
        $totalUpdated += [int]$response.updatedCount
        Write-Host "$($document.codeSystem): $end/$($codes.Count)"
    }
    Write-Host "$($document.codeSystem) 完成：新增 $totalImported，更新 $totalUpdated"
}

$knowledgeRoot = (Resolve-Path -LiteralPath $KnowledgeDirectory).Path
Import-CodeSystem -Path (Join-Path $knowledgeRoot "icd10-2026.json")
Import-CodeSystem -Path (Join-Path $knowledgeRoot "icd9cm3-2026.json")

$rules = Get-Content -Raw -LiteralPath (Join-Path $knowledgeRoot "coding-rules-2026.json") |
    ConvertFrom-Json
$ruleResponse = Invoke-JsonRequest `
    -Method Post `
    -Uri "$ApiBaseUrl/api/v1/coding-rules/import" `
    -Body @{
        synonyms = @($rules.synonyms)
        rules = @($rules.rules)
    }
Write-Host "同义词和规则完成：同义词 $($ruleResponse.synonymCount)，规则 $($ruleResponse.ruleCount)"
