using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Coding.Preprocessing;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.Elasticsearch;
using HospitalAi.Infrastructure.ModelGateways;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HospitalAi.Worker.Tests;

/// <summary>
/// V2.2-Lite 集成测试基座。每个用例独立建库，跑完即删，避免测试间相互污染。
/// 需要数据库：设置 HOSPITAL_AI_TEST_CONNECTION_STRING。
/// </summary>
public sealed class V22TestDatabase : IAsyncDisposable
{
    private V22TestDatabase(string databaseName, string masterConnectionString, string connectionString)
    {
        _masterConnectionString = masterConnectionString;
        ConnectionString = connectionString;
    }

    private readonly string _masterConnectionString;

    public string ConnectionString { get; }

    public static async Task<V22TestDatabase> CreateAsync()
    {
        var template = Environment.GetEnvironmentVariable("HOSPITAL_AI_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException(
                "运行 V2.2-Lite 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
        }

        var databaseName = $"HospitalAi_V22_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(template)
        {
            InitialCatalog = databaseName
        };
        var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString)
        {
            InitialCatalog = "master"
        };

        var database = new V22TestDatabase(
            databaseName,
            masterBuilder.ConnectionString,
            builder.ConnectionString);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public HospitalAiDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new HospitalAiDbContext(options);
    }

    /// <summary>
    /// 构造一个使用真实 Exact 检索、ES 与模型均不可用的 Lite Runner，
    /// 对应生产默认形态：Exact 快路径 + 降级标记。
    /// </summary>
    public V22LiteCodingPipelineRunner CreateV22Runner()
    {
        return CreateV22Runner(new UnavailableModelGateway(), UnavailableRouter());
    }

    /// <summary>
    /// 允许注入模型网关与路由器，用于验证"模型接通但输出不合法"这条独立路径。
    /// </summary>
    public V22LiteCodingPipelineRunner CreateV22Runner(
        IModelGateway gateway,
        IModelRouter router)
    {
        var context = CreateContext();
        return new V22LiteCodingPipelineRunner(
            context,
            new SqlServerExactCodingKnowledgeSearch(context),
            new UnavailableCodingKnowledgeSearch(),
            gateway,
            router,
            new SimpleJsonSchemaValidator(),
            Options.Create(new V22PipelineOptions()),
            NullLogger<V22LiteCodingPipelineRunner>.Instance);
    }

    private static IModelRouter UnavailableRouter()
    {
        return new StaticModelRouter(Options.Create(new ModelGatewayOptions()));
    }

    /// <summary>
    /// 播种一个 V2.2-Lite 编码任务：医院、患者、就诊、当前生效文书、任务与根 Trace。
    /// documentText 即文书正文（content_reference）。
    /// </summary>
    public async Task<SeededV22Task> SeedV22TaskAsync(
        string documentText,
        string? documentType = null)
    {
        var now = DateTimeOffset.UtcNow;
        var hospitalId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var traceId = $"trace-{Guid.NewGuid():N}";
        var type = documentType ?? "出院小结";

        await using var context = CreateContext();
        context.Hospitals.Add(new HospitalRecord
        {
            Id = hospitalId,
            Code = $"HV-{Guid.NewGuid():N}"[..16],
            Name = "V22 Test Hospital",
            Status = "ACTIVE",
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Patients.Add(new PatientRecord
        {
            Id = patientId,
            HospitalId = hospitalId,
            SourceSystem = "TEST",
            SourcePatientId = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Visits.Add(new VisitRecord
        {
            Id = visitId,
            HospitalId = hospitalId,
            PatientId = patientId,
            AdmissionAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.MedicalDocuments.Add(new MedicalDocumentRecord
        {
            Id = documentId,
            HospitalId = hospitalId,
            VisitId = visitId,
            DocumentType = type,
            ContentReference = documentText,
            ContentHash = DocumentChunker.ComputeHash(documentText),
            Version = 1,
            DocumentStatus = "ACTIVE",
            IsCurrent = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.CodingTasks.Add(new CodingTaskRecord
        {
            Id = taskId,
            HospitalId = hospitalId,
            VisitId = visitId,
            PipelineVersion = PipelineVersions.Lite,
            Status = CodingTaskStatus.Pending,
            CodingStage = CodingStage.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.PipelineTraces.Add(new PipelineTraceRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            CodingTaskId = taskId,
            TraceId = traceId,
            Status = "PENDING",
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        return new SeededV22Task(
            hospitalId,
            visitId,
            taskId,
            traceId,
            new CodingTaskCreatedMessage(
                Guid.NewGuid(),
                hospitalId,
                taskId,
                visitId,
                PipelineVersions.Lite,
                traceId));
    }

    /// <summary>
    /// 播种一条编码字典条目。SearchText 参与 Exact 检索的子串预筛，
    /// 因此必须包含要命中的原文。
    /// </summary>
    public async Task SeedCodeAsync(
        Guid hospitalId,
        string codeSystemCode,
        string code,
        string title,
        string? codeType = null)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        // 同一医院同一编码体系只建一条：code_system 上有 (hospital_id, code) 唯一索引。
        var codeSystem = await context.CodeSystems.FirstOrDefaultAsync(
            item => item.HospitalId == hospitalId && item.Code == codeSystemCode);
        if (codeSystem is null)
        {
            codeSystem = new CodeSystemRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                Code = codeSystemCode,
                Name = codeSystemCode,
                Version = "v1",
                CreatedAt = now,
                UpdatedAt = now
            };
            context.CodeSystems.Add(codeSystem);
            await context.SaveChangesAsync();
        }

        context.MedicalCodes.Add(new MedicalCodeRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            CodeSystemId = codeSystem.Id,
            CodeSystemCode = codeSystemCode,
            Code = code,
            Title = title,
            CodeType = codeType ?? "诊断",
            SearchText = title,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 播种一条版本化 JSON 条件阻断规则：候选编码相同时命中并阻断安全推荐。
    /// </summary>
    public async Task SeedBlockingRuleAsync(Guid hospitalId, string code)
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        context.CodingRules.Add(new CodingRuleRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            RuleCode = $"test-block-{code}",
            RuleVersion = "v1",
            CodeSystemCode = "ICD-10",
            CodePattern = code,
            RuleType = "BLOCK",
            Severity = "HIGH",
            Message = "测试阻断规则",
            Priority = 10,
            ConditionJson = $$"""
                {
                  "all": [
                    { "field": "candidate.code", "op": "equals", "value": "{{code}}" },
                    { "field": "candidate.code_system", "op": "equals", "value": "ICD-10" }
                  ]
                }
                """,
            ActionJson = """{ "action": "require_human_review" }""",
            Blocking = true,
            IsBuiltin = false,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var databaseName = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>播种后的 V2.2-Lite 任务句柄。</summary>
public sealed record SeededV22Task(
    Guid HospitalId,
    Guid VisitId,
    Guid TaskId,
    string TraceId,
    CodingTaskCreatedMessage Message)
{
    /// <summary>大多数用例只需要医院、任务与消息。</summary>
    public void Deconstruct(out Guid hospitalId, out Guid taskId, out CodingTaskCreatedMessage message)
    {
        hospitalId = HospitalId;
        taskId = TaskId;
        message = Message;
    }
}
