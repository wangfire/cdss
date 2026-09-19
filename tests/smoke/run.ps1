param(
    [string]$ComposeFile = "deploy/docker-compose.dev.yml",
    [string]$ApiBaseUrl = "http://localhost:5080",
    [int]$ReadyTimeoutSeconds = 120,
    [int]$TaskTimeoutSeconds = 120,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    $arguments = @{
        Method = $Method
        Uri = $Uri
        Headers = $Headers
    }

    if ($null -ne $Body) {
        $arguments.ContentType = "application/json"
        $arguments.Body = ($Body | ConvertTo-Json -Depth 8)
    }

    Invoke-RestMethod @arguments
}

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Probe,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [string]$FailureMessage = "等待超时。"
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $result = & $Probe
            if ($result) {
                return $result
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    throw $FailureMessage
}

function Invoke-ComposeUp {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = docker compose @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }

    return @{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

function Build-ApplicationImagesWithLegacyBuilder {
    $previousBuildKit = $env:DOCKER_BUILDKIT
    try {
        # 当前 Docker Desktop/Compose 组合可能在 BuildKit 会话头中写入非法字符。
        # 传统 builder 不走该 gRPC 会话，作为开发冒烟的兼容回退路径。
        $env:DOCKER_BUILDKIT = "0"

        docker build -f src/HospitalAi.Api/Dockerfile -t deploy-api .
        if ($LASTEXITCODE -ne 0) {
            throw "API 镜像构建失败，退出码：$LASTEXITCODE。"
        }

        docker build -f src/HospitalAi.Worker/Dockerfile -t deploy-worker .
        if ($LASTEXITCODE -ne 0) {
            throw "Worker 镜像构建失败，退出码：$LASTEXITCODE。"
        }
    }
    finally {
        $env:DOCKER_BUILDKIT = $previousBuildKit
    }
}

if (-not (Test-Path $ComposeFile)) {
    throw "未找到 Docker Compose 文件：$ComposeFile"
}

$composeUpArguments = @("-f", $ComposeFile, "up", "-d")
if (-not $SkipBuild) {
    # 默认自动构建镜像；本地已有镜像时可用 -SkipBuild 避免重复构建。
    $composeUpArguments += "--build"
}

$composeResult = Invoke-ComposeUp -Arguments $composeUpArguments
if ($composeResult.ExitCode -ne 0) {
    $isBuildKitSessionHeaderFailure = -not $SkipBuild `
        -and $composeResult.Output.Contains("x-docker-expose-session-sharedkey")

    if (-not $isBuildKitSessionHeaderFailure) {
        throw "Docker Compose 启动失败，退出码：$($composeResult.ExitCode)。请确认 Docker Desktop 已启动。"
    }

    Write-Warning "Docker Compose BuildKit 构建通道异常，回退到传统 docker build 后再启动。"
    Build-ApplicationImagesWithLegacyBuilder

    $composeResult = Invoke-ComposeUp -Arguments @("-f", $ComposeFile, "up", "-d", "--no-build")
    if ($composeResult.ExitCode -ne 0) {
        throw "Docker Compose 回退启动失败，退出码：$($composeResult.ExitCode)。"
    }
}

try {
    Wait-Until -TimeoutSeconds $ReadyTimeoutSeconds -FailureMessage "API ready 检查超时。" -Probe {
        $ready = Invoke-RestMethod "$ApiBaseUrl/api/v1/health/ready"
        $ready.status -eq "healthy"
    } | Out-Null

    $hospital = Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/hospitals" `
        -Body @{
            code = "DEV-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
            name = "开发冒烟医院"
        }

    $headers = @{
        "X-Hospital-Id" = $hospital.id
        "X-User-Id" = "smoke-user"
    }

    $patient = Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/patients" `
        -Headers $headers `
        -Body @{
            sourceSystem = "SMOKE"
            sourcePatientId = [Guid]::NewGuid().ToString("N")
            displayName = "冒烟测试患者"
        }

    $visit = Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/visits" `
        -Headers $headers `
        -Body @{
            patientId = $patient.id
            admissionAt = (Get-Date).ToUniversalTime().ToString("o")
            dischargeAt = $null
        }

    Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/code-systems/import" `
        -Headers $headers `
        -Body @{
            codeSystem = "ICD-10"
            version = "2026"
            codes = @(
                @{
                    code = "S82.142A"
                    title = "左胫骨平台粉碎性骨折"
                    codeType = "诊断"
                    searchText = "左胫骨平台 粉碎性 骨折"
                    isEnabled = $true
                }
            )
        } | Out-Null

    Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/code-systems/import" `
        -Headers $headers `
        -Body @{
            codeSystem = "ICD-9-CM-3"
            version = "2026"
            codes = @(
                @{
                    code = "79.36"
                    title = "胫骨内固定术"
                    codeType = "手术"
                    searchText = "胫骨 内固定术"
                    isEnabled = $true
                }
            )
        } | Out-Null

    Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/documents" `
        -Headers $headers `
        -Body @{
            visitId = $visit.id
            documentType = "discharge"
            contentReference = "出院诊断：左胫骨平台粉碎性骨折。手术名称：胫骨内固定术。"
            contentHash = [Guid]::NewGuid().ToString("N")
            version = 1
        } | Out-Null

    $taskHeaders = $headers.Clone()
    $taskHeaders["Idempotency-Key"] = [Guid]::NewGuid().ToString("N")
    $task = Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/coding-tasks" `
        -Headers $taskHeaders `
        -Body @{
            visitId = $visit.id
            pipelineVersion = "pipeline-v1"
        }

    $pendingReview = Wait-Until -TimeoutSeconds $TaskTimeoutSeconds -FailureMessage "编码任务未在限定时间内进入 PENDING_REVIEW。" -Probe {
        $current = Invoke-JsonRequest `
            -Method Get `
            -Uri "$ApiBaseUrl/api/v1/coding-tasks/$($task.id)" `
            -Headers $headers
        if ($current.status -eq "PENDING_REVIEW") {
            return $current
        }

        return $null
    }

    $recommendations = Invoke-JsonRequest `
        -Method Get `
        -Uri "$ApiBaseUrl/api/v1/coding-tasks/$($task.id)/recommendations" `
        -Headers $headers

    if ($recommendations.recommendations.Count -lt 2) {
        throw "编码任务推荐数量不足，期望至少 2 条。"
    }

    $finalCodes = @(
        $recommendations.recommendations | ForEach-Object {
            @{
                recommendationId = $_.id
                resultType = $_.recommendationType
                codeSystem = $_.codeSystem
                code = $_.code
                title = $_.title
            }
        }
    )

    $review = Invoke-JsonRequest `
        -Method Post `
        -Uri "$ApiBaseUrl/api/v1/coding-tasks/$($task.id)/review" `
        -Headers $headers `
        -Body @{
            reviewStatus = "ACCEPTED"
            finalCodes = $finalCodes
            comment = "冒烟测试确认推荐"
        }

    if ($review.reviewStatus -ne "ACCEPTED" -or $review.finalCodeCount -lt 2) {
        throw "人工审核未生成期望的最终编码。"
    }

    Write-Host "Smoke PASS: task $($pendingReview.id) reached PENDING_REVIEW and review accepted with trace $($pendingReview.traceId)"
}
finally {
    docker compose -f $ComposeFile ps
}
