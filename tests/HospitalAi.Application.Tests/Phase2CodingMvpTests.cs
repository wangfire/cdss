using HospitalAi.Application.Abstractions;
using HospitalAi.Contracts.CodingKnowledge;
using HospitalAi.Contracts.CodingRecommendations;
using HospitalAi.Domain.CodingTasks;
using HospitalAi.Infrastructure.CodingKnowledge;
using HospitalAi.Infrastructure.CodingRecommendations;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Application.Tests;

public sealed class Phase2CodingMvpTests
{
    [Fact]
    public async Task ImportCodeSystemAsync_导入ICD诊断和手术编码()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateKnowledgeService(context, hospitalId);

        var response = await service.ImportCodeSystemAsync(new ImportCodeSystemRequest(
            "ICD-10",
            "2026",
            [
                new MedicalCodeImportItem(
                    "S82.142A",
                    "左胫骨平台粉碎性骨折",
                    "诊断",
                    "左胫骨平台 粉碎性 骨折",
                    true)
            ]));

        Assert.Equal(1, response.ImportedCount);
        Assert.Equal(0, response.UpdatedCount);
        Assert.Equal("ICD-10", await context.CodeSystems.Select(item => item.Code).SingleAsync());
        Assert.Equal("S82.142A", await context.MedicalCodes.Select(item => item.Code).SingleAsync());
    }

    [Fact]
    public async Task ImportCodeSystemAsync_同一批次重复编码按最后一条生效()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateKnowledgeService(context, hospitalId);

        var response = await service.ImportCodeSystemAsync(new ImportCodeSystemRequest(
            "ICD-10",
            "2026",
            [
                new MedicalCodeImportItem(
                    "A00.000",
                    "霍乱旧名称",
                    "诊断",
                    "霍乱旧名称",
                    true),
                new MedicalCodeImportItem(
                    "A00.000",
                    "霍乱新名称",
                    "诊断",
                    "霍乱新名称",
                    true)
            ]));

        var code = await context.MedicalCodes.SingleAsync();
        Assert.Equal(1, response.ImportedCount);
        Assert.Equal(0, response.UpdatedCount);
        Assert.Equal("霍乱新名称", code.Title);
    }

    [Fact]
    public async Task ImportCodingRulesAsync_同一批次重复同义词和规则按最后一条生效()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateKnowledgeService(context, hospitalId);

        var response = await service.ImportCodingRulesAsync(new ImportCodingRulesRequest(
            [
                new TermSynonymImportItem("糖尿病", "糖尿病旧归一词", "DIAGNOSIS"),
                new TermSynonymImportItem("糖尿病", "糖尿病新归一词", "DIAGNOSIS")
            ],
            [
                new CodingRuleImportItem(
                    "RULE-001",
                    "ICD-10",
                    "E11",
                    "DIAGNOSIS_SELECTION",
                    "WARNING",
                    "旧提示",
                    true),
                new CodingRuleImportItem(
                    "RULE-001",
                    "ICD-10",
                    "E11.9",
                    "DIAGNOSIS_SELECTION",
                    "ERROR",
                    "新提示",
                    true)
            ]));

        var synonym = await context.TermSynonyms.SingleAsync();
        var rule = await context.CodingRules.SingleAsync();
        Assert.Equal(1, response.SynonymCount);
        Assert.Equal(1, response.RuleCount);
        Assert.Equal("糖尿病新归一词", synonym.NormalizedTerm);
        Assert.Equal("E11.9", rule.CodePattern);
        Assert.Equal("新提示", rule.Message);
    }

    [Fact]
    public async Task ImportCodingRulesAsync_同一关键词对应多个编码时保留多条关系()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateKnowledgeService(context, hospitalId);

        var response = await service.ImportCodingRulesAsync(new ImportCodingRulesRequest(
            [
                new TermSynonymImportItem(
                    "白血病",
                    "白血病",
                    "DIAGNOSIS",
                    "ICD-10",
                    "C95.9"),
                new TermSynonymImportItem(
                    "白血病",
                    "白血病",
                    "DIAGNOSIS",
                    "ICD-10",
                    "M9800/3")
            ],
            []));

        Assert.Equal(2, response.SynonymCount);
        var mappings = await context.TermSynonyms
            .OrderBy(item => item.Code)
            .ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.Equal(["C95.9", "M9800/3"], mappings.Select(item => item.Code));
    }

    [Fact]
    public async Task ImportCodingRulesAsync_兼容旧版SQLServer兼容级别()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        await database.SeedHospitalAsync(hospitalId);
        // SQL Server 2019 允许的最低兼容级别为 110，且该级别不支持 OPENJSON。
        await database.SetCompatibilityLevelAsync(110);
        await using var context = database.CreateContext();
        var service = CreateKnowledgeService(context, hospitalId);

        // 使用多个同义词覆盖批量预加载路径，确保导入不依赖新版 OPENJSON 查询。
        var synonyms = Enumerable.Range(1, 3000)
            .Select(index => new TermSynonymImportItem(
                $"关键词-{index}",
                $"标准术语-{index}",
                "DIAGNOSIS"))
            .ToArray();

        var response = await service.ImportCodingRulesAsync(
            new ImportCodingRulesRequest(synonyms, []));

        Assert.Equal(synonyms.Length, response.SynonymCount);
        Assert.Equal(synonyms.Length, await context.TermSynonyms.CountAsync());
    }

    [Fact]
    public async Task ReviewAsync_确认推荐后写入最终编码()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var taskId = await database.SeedRecommendationAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateRecommendationService(context, hospitalId, "auditor-1");
        var recommendationId = await context.CodingRecommendations
            .Where(item => item.CodingTaskId == taskId)
            .Select(item => item.Id)
            .SingleAsync();

        var response = await service.ReviewAsync(
            taskId,
            new ReviewCodingTaskRequest(
                "ACCEPTED",
                [
                    new ReviewedCodeItem(
                        recommendationId,
                        "DIAGNOSIS",
                        "ICD-10",
                        "S82.142A",
                        "左胫骨平台粉碎性骨折")
                ],
                "编码员确认推荐"),
            CancellationToken.None);

        Assert.Equal("ACCEPTED", response.ReviewStatus);
        var finalResult = await context.FinalCodingResults.SingleAsync();
        Assert.Equal(taskId, finalResult.CodingTaskId);
        Assert.Equal("S82.142A", finalResult.Code);
        Assert.Equal("auditor-1", finalResult.ReviewerId);
    }

    [Fact]
    public async Task RunAsync_根据脱敏文书生成推荐证据并进入待审核()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var visitId = await database.SeedVisitWithDocumentAsync(
            hospitalId,
            "出院诊断：左胫骨平台粉碎性骨折。手术名称：胫骨内固定术。");
        var taskId = await database.SeedCodingTaskAsync(hospitalId, visitId);
        await using var context = database.CreateContext();
        await database.SeedCodeAsync(
            hospitalId,
            "ICD-10",
            "诊断",
            "S82.142A",
            "左胫骨平台粉碎性骨折",
            "左胫骨平台 粉碎性 骨折");
        await database.SeedCodeAsync(
            hospitalId,
            "ICD-9-CM-3",
            "手术",
            "79.36",
            "胫骨内固定术",
            "胫骨 内固定术");

        var runner = new SqlServerCodingRecommendationPipelineRunner(context);
        await runner.RunAsync(new(
            Guid.NewGuid(),
            hospitalId,
            taskId,
            visitId,
            "phase2-mvp",
            "trace-phase2",
            "request-phase2"));

        var task = await context.CodingTasks.SingleAsync(item => item.Id == taskId);
        var recommendations = await context.CodingRecommendations
            .Include(item => item.Evidences)
            .OrderBy(item => item.Code)
            .ToListAsync();

        Assert.Equal(CodingTaskStatus.PendingReview, task.Status);
        Assert.Equal(2, recommendations.Count);
        Assert.All(recommendations, item => Assert.NotEmpty(item.Evidences));
        Assert.Contains(recommendations, item => item.Code == "S82.142A" && item.ConfidenceScore >= 0.85m);
        Assert.Contains(recommendations, item => item.Code == "79.36" && item.ConfidenceScore >= 0.85m);
    }

    [Fact]
    public async Task RunAsync_根据同义词直接命中组合编码并保留加号和星号()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var visitId = await database.SeedVisitWithDocumentAsync(
            hospitalId,
            "出院诊断：13三体综合征。另见糖代谢相关心肌病。");
        var taskId = await database.SeedCodingTaskAsync(hospitalId, visitId);
        await database.SeedCodeAsync(
            hospitalId,
            "ICD-10",
            "诊断",
            "Q91.7",
            "13三体综合征",
            "13三体综合征");

        await using var context = database.CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.TermSynonyms.AddRange(
            new TermSynonymRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                Term = "13三体综合征",
                NormalizedTerm = "13三体综合征",
                CodeSystemCode = "ICD-10",
                Code = "Q91.7",
                EntityType = "DIAGNOSIS",
                CreatedAt = now,
                UpdatedAt = now
            },
            new TermSynonymRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                Term = "帕套综合征",
                NormalizedTerm = "13三体综合征",
                CodeSystemCode = "ICD-10",
                Code = "Q91.7",
                EntityType = "DIAGNOSIS",
                CreatedAt = now,
                UpdatedAt = now
            },
            new TermSynonymRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                Term = "糖代谢相关心肌病",
                NormalizedTerm = "糖代谢相关心肌病",
                CodeSystemCode = "ICD-10",
                Code = "E74.0+I43.1*",
                EntityType = "DIAGNOSIS",
                CreatedAt = now,
                UpdatedAt = now
            });
        await context.SaveChangesAsync();

        var runner = new SqlServerCodingRecommendationPipelineRunner(context);
        await runner.RunAsync(new(
            Guid.NewGuid(),
            hospitalId,
            taskId,
            visitId,
            "phase2-mvp",
            "trace-synonym-composite-code",
            "request-synonym-composite-code"));

        var recommendations = await context.CodingRecommendations
            .OrderBy(item => item.Code)
            .ToListAsync();

        Assert.Contains(recommendations, item => item.Code == "Q91.7");
        Assert.Contains(recommendations, item => item.Code == "E74.0+I43.1*");
        Assert.DoesNotContain(recommendations, item => item.Code is "E74.0" or "I43.1*");
    }

    [Fact]
    public async Task ListWorkbenchTasksAsync_支持下划线审核状态筛选()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var taskId = await database.SeedRecommendationAsync(hospitalId);
        await using var context = database.CreateContext();
        context.CodingTasks.Single(item => item.Id == taskId).Status = CodingTaskStatus.PendingReview;
        await context.SaveChangesAsync();

        var service = CreateRecommendationService(context, hospitalId, "auditor-1");
        var tasks = await service.ListWorkbenchTasksAsync("PENDING_REVIEW");

        var task = Assert.Single(tasks);
        Assert.Equal(taskId, task.TaskId);
        Assert.Equal("PENDING_REVIEW", task.Status);
    }

    [Fact]
    public async Task ReviewAsync_拒绝推荐时不生成最终编码()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var taskId = await database.SeedRecommendationAsync(hospitalId);
        await using var context = database.CreateContext();
        var service = CreateRecommendationService(context, hospitalId, "auditor-1");

        var response = await service.ReviewAsync(
            taskId,
            new ReviewCodingTaskRequest("REJECTED", [], "证据不足"),
            CancellationToken.None);

        Assert.Equal("REJECTED", response.ReviewStatus);
        Assert.Empty(await context.FinalCodingResults.ToListAsync());
        Assert.Equal(CodingTaskStatus.Rejected, await context.CodingTasks
            .Where(item => item.Id == taskId)
            .Select(item => item.Status)
            .SingleAsync());
    }

    [Fact]
    public async Task ReviewAsync_不能引用其他任务的推荐()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hospitalId = Guid.NewGuid();
        var firstTaskId = await database.SeedRecommendationAsync(hospitalId);
        var secondTaskId = await database.SeedRecommendationAsync(hospitalId);
        await using var context = database.CreateContext();
        var foreignRecommendationId = await context.CodingRecommendations
            .Where(item => item.CodingTaskId == secondTaskId)
            .Select(item => item.Id)
            .SingleAsync();
        var service = CreateRecommendationService(context, hospitalId, "auditor-1");

        await Assert.ThrowsAsync<HospitalAi.Application.Common.ValidationException>(() =>
            service.ReviewAsync(
                firstTaskId,
                new ReviewCodingTaskRequest(
                    "ACCEPTED",
                    [
                        new ReviewedCodeItem(
                            foreignRecommendationId,
                            "DIAGNOSIS",
                            "ICD-10",
                            "S82.142A",
                            "左胫骨平台粉碎性骨折")
                    ],
                    null)));
    }

    private static ICodingKnowledgeService CreateKnowledgeService(
        HospitalAiDbContext context,
        Guid hospitalId)
    {
        return new SqlServerCodingKnowledgeService(
            context,
            new TestRequestContext(hospitalId, "trace-knowledge", null));
    }

    private static ICodingRecommendationService CreateRecommendationService(
        HospitalAiDbContext context,
        Guid hospitalId,
        string userId)
    {
        return new SqlServerCodingRecommendationService(
            context,
            new TestRequestContext(hospitalId, "trace-review", userId));
    }

    private sealed record TestRequestContext(
        Guid HospitalId,
        string TraceId,
        string? UserId) : IRequestContext
    {
        public string RequestId { get; } = Guid.NewGuid().ToString("N");

        public string? IdempotencyKey { get; } = null;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;
        private readonly string _connectionString;

        private TestDatabase(
            string databaseName,
            string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            _connectionString = connectionString;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var template = Environment.GetEnvironmentVariable(
                "HOSPITAL_AI_TEST_CONNECTION_STRING");

            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException(
                    "运行 Phase2 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Phase2_{Guid.NewGuid():N}";
            var builder = new SqlConnectionStringBuilder(template)
            {
                InitialCatalog = databaseName
            };
            var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString)
            {
                InitialCatalog = "master"
            };

            var database = new TestDatabase(
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
                .UseSqlServer(_connectionString)
                .Options;

            return new HospitalAiDbContext(options);
        }

        public async Task SeedHospitalAsync(Guid hospitalId)
        {
            var now = DateTimeOffset.UtcNow;
            await using var context = CreateContext();
            if (await context.Hospitals.AnyAsync(item => item.Id == hospitalId))
            {
                return;
            }

            context.Hospitals.Add(new HospitalRecord
            {
                Id = hospitalId,
                Code = $"HOSP-{hospitalId:N}"[..32],
                Name = "Phase2 Test Hospital",
                Status = "ACTIVE",
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
        }

        public async Task SetCompatibilityLevelAsync(int compatibilityLevel)
        {
            await using var context = CreateContext();
            // SQL Server 的数据库兼容级别语法不支持参数化，这里仅允许测试使用固定的兼容级别。
            var sql = compatibilityLevel switch
            {
                110 => "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 110",
                120 => "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 120",
                130 => "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 130",
                140 => "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 140",
                150 => "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 150",
                160 => "ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 160",
                _ => throw new ArgumentOutOfRangeException(nameof(compatibilityLevel))
            };

            await context.Database.ExecuteSqlRawAsync(sql);
        }

        public async Task<Guid> SeedVisitWithDocumentAsync(Guid hospitalId, string content)
        {
            await SeedHospitalAsync(hospitalId);
            var now = DateTimeOffset.UtcNow;
            var patientId = Guid.NewGuid();
            var visitId = Guid.NewGuid();
            await using var context = CreateContext();
            context.Patients.Add(new PatientRecord
            {
                Id = patientId,
                HospitalId = hospitalId,
                SourceSystem = "TEST",
                SourcePatientId = patientId.ToString("N"),
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
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                VisitId = visitId,
                DocumentType = "discharge",
                ContentReference = content,
                ContentHash = Guid.NewGuid().ToString("N"),
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
            return visitId;
        }

        public async Task<Guid> SeedCodingTaskAsync(Guid hospitalId, Guid visitId)
        {
            var now = DateTimeOffset.UtcNow;
            var taskId = Guid.NewGuid();
            await using var context = CreateContext();
            context.CodingTasks.Add(new CodingTaskRecord
            {
                Id = taskId,
                HospitalId = hospitalId,
                VisitId = visitId,
                PipelineVersion = "phase2-mvp",
                Status = CodingTaskStatus.Running,
                RetryCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
            return taskId;
        }

        public async Task SeedCodeAsync(
            Guid hospitalId,
            string systemCode,
            string codeType,
            string code,
            string title,
            string searchText)
        {
            var now = DateTimeOffset.UtcNow;
            await using var context = CreateContext();
            var system = await context.CodeSystems
                .SingleOrDefaultAsync(item => item.HospitalId == hospitalId && item.Code == systemCode);
            if (system is null)
            {
                system = new CodeSystemRecord
                {
                    Id = Guid.NewGuid(),
                    HospitalId = hospitalId,
                    Code = systemCode,
                    Name = systemCode,
                    Version = "2026",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                context.CodeSystems.Add(system);
            }

            context.MedicalCodes.Add(new MedicalCodeRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                CodeSystemId = system.Id,
                CodeSystemCode = systemCode,
                Code = code,
                Title = title,
                CodeType = codeType,
                SearchText = searchText,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
        }

        public async Task<Guid> SeedRecommendationAsync(Guid hospitalId)
        {
            var visitId = await SeedVisitWithDocumentAsync(hospitalId, "出院诊断：左胫骨平台粉碎性骨折。");
            var taskId = await SeedCodingTaskAsync(hospitalId, visitId);
            var now = DateTimeOffset.UtcNow;
            await using var context = CreateContext();
            context.CodingRecommendations.Add(new CodingRecommendationRecord
            {
                Id = Guid.NewGuid(),
                HospitalId = hospitalId,
                CodingTaskId = taskId,
                RecommendationType = "DIAGNOSIS",
                CodeSystemCode = "ICD-10",
                Code = "S82.142A",
                Title = "左胫骨平台粉碎性骨折",
                Rank = 1,
                RecallScore = 0.95m,
                RuleScore = 0.95m,
                ConfidenceScore = 0.95m,
                ReviewStatus = "PENDING_REVIEW",
                CreatedAt = now,
                UpdatedAt = now
            });
            await context.SaveChangesAsync();
            return taskId;
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
