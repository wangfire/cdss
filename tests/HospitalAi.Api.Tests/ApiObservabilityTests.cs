using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using HospitalAi.Contracts.CodingTasks;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Visits;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HospitalAi.Api.Tests;

public sealed class ApiObservabilityTests
{
    [Fact]
    public async Task HealthReady_返回SqlRedisRabbit依赖状态()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        using var factory = CreateFactory(database.ConnectionString, new CaptureLoggerProvider());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("sqlServer", body);
        Assert.Contains("redis", body);
        Assert.Contains("rabbitMq", body);
    }

    [Fact]
    public async Task CodingTask_跨医院查询返回NotFound()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var loggerProvider = new CaptureLoggerProvider();
        using var factory = CreateFactory(database.ConnectionString, loggerProvider);
        using var client = factory.CreateClient();

        var hospital = await CreateHospitalAsync(client, loggerProvider);
        var patient = await CreatePatientAsync(client, hospital.Id, "隔离测试患者", loggerProvider);
        var visit = await CreateVisitAsync(client, hospital.Id, patient.Id, loggerProvider);
        var task = await CreateCodingTaskAsync(client, hospital.Id, visit.Id, loggerProvider);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/coding-tasks/{task.Id}");
        request.Headers.Add("X-Hospital-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "api-test-user");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CodeSystemsImport_导入ICD字典返回计数()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var loggerProvider = new CaptureLoggerProvider();
        using var factory = CreateFactory(database.ConnectionString, loggerProvider);
        using var client = factory.CreateClient();
        var hospital = await CreateHospitalAsync(client, loggerProvider);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/code-systems/import")
        {
            Content = JsonContent.Create(new
            {
                codeSystem = "ICD-10",
                version = "2026",
                codes = new[]
                {
                    new
                    {
                        code = "S82.142A",
                        title = "左胫骨平台粉碎性骨折",
                        codeType = "诊断",
                        searchText = "左胫骨平台 粉碎性 骨折",
                        isEnabled = true
                    }
                }
            })
        };
        request.Headers.Add("X-Hospital-Id", hospital.Id.ToString());
        request.Headers.Add("X-User-Id", "api-test-user");

        using var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response, loggerProvider);
        var body = await response.Content.ReadFromJsonAsync<ImportResponse>();

        Assert.Equal("ICD-10", body?.CodeSystem);
        Assert.Equal(1, body?.ImportedCount);
        Assert.Equal(0, body?.UpdatedCount);
    }

    [Fact]
    public async Task RequestLog_记录结构化上下文且不包含患者姓名()
    {
        await using var database = await ApiTestDatabase.CreateAsync();
        var loggerProvider = new CaptureLoggerProvider();
        using var factory = CreateFactory(database.ConnectionString, loggerProvider);
        using var client = factory.CreateClient();
        var patientName = "张三敏感姓名";

        var hospital = await CreateHospitalAsync(client, loggerProvider);
        _ = await CreatePatientAsync(client, hospital.Id, patientName, loggerProvider);

        Assert.Contains(
            loggerProvider.Entries,
            entry => entry.Message.Contains("HTTP request completed", StringComparison.Ordinal)
                && entry.Message.Contains("TraceId=", StringComparison.Ordinal)
                && entry.Message.Contains("HospitalId=", StringComparison.Ordinal)
                && entry.Message.Contains("UserId=", StringComparison.Ordinal)
                && entry.Message.Contains("api-test-user", StringComparison.Ordinal));
        Assert.DoesNotContain(
            loggerProvider.Entries,
            entry => entry.Message.Contains(patientName, StringComparison.Ordinal)
                || entry.Scopes.Any(scope => scope.Contains(patientName, StringComparison.Ordinal)));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        CaptureLoggerProvider loggerProvider)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:HospitalAi", connectionString);
                builder.ConfigureAppConfiguration(configuration =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:HospitalAi"] = connectionString,
                        ["RabbitMq:Enabled"] = "false",
                        ["HealthChecks:Redis:Host"] = "127.0.0.1",
                        ["HealthChecks:Redis:Port"] = "6390",
                        ["HealthChecks:RabbitMq:Host"] = "127.0.0.1",
                        ["HealthChecks:RabbitMq:Port"] = "5673"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ILoggerProvider>(loggerProvider);
                });
                builder.ConfigureLogging(logging =>
                {
                    logging.AddProvider(loggerProvider);
                });
            });
    }

    private static async Task<HospitalResponse> CreateHospitalAsync(
        HttpClient client,
        CaptureLoggerProvider loggerProvider)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/hospitals",
            new CreateHospitalRequest($"H-{Guid.NewGuid():N}"[..16], "API Test Hospital"));
        await EnsureSuccessAsync(response, loggerProvider);
        return (await response.Content.ReadFromJsonAsync<HospitalResponse>())!;
    }

    private static async Task<PatientResponse> CreatePatientAsync(
        HttpClient client,
        Guid hospitalId,
        string displayName,
        CaptureLoggerProvider loggerProvider)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/patients")
        {
            Content = JsonContent.Create(new CreatePatientRequest(
                "TEST",
                Guid.NewGuid().ToString("N"),
                displayName))
        };
        request.Headers.Add("X-Hospital-Id", hospitalId.ToString());
        request.Headers.Add("X-User-Id", "api-test-user");

        using var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response, loggerProvider);
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
    }

    private static async Task<VisitResponse> CreateVisitAsync(
        HttpClient client,
        Guid hospitalId,
        Guid patientId,
        CaptureLoggerProvider loggerProvider)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/visits")
        {
            Content = JsonContent.Create(new CreateVisitRequest(
                patientId,
                DateTimeOffset.UtcNow,
                null))
        };
        request.Headers.Add("X-Hospital-Id", hospitalId.ToString());
        request.Headers.Add("X-User-Id", "api-test-user");

        using var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response, loggerProvider);
        return (await response.Content.ReadFromJsonAsync<VisitResponse>())!;
    }

    private static async Task<CodingTaskResponse> CreateCodingTaskAsync(
        HttpClient client,
        Guid hospitalId,
        Guid visitId,
        CaptureLoggerProvider loggerProvider)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/coding-tasks")
        {
            Content = JsonContent.Create(new CreateCodingTaskRequest(
                visitId,
                "pipeline-v1"))
        };
        request.Headers.Add("X-Hospital-Id", hospitalId.ToString());
        request.Headers.Add("X-User-Id", "api-test-user");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);
        await EnsureSuccessAsync(response, loggerProvider);
        return (await response.Content.ReadFromJsonAsync<CodingTaskResponse>())!;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CaptureLoggerProvider loggerProvider)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var logText = string.Join(
            Environment.NewLine,
            loggerProvider.Entries.Select(entry => entry.Message));
        throw new HttpRequestException(
            $"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}). Body: {body}. Logs: {logText}");
    }

    private sealed class ApiTestDatabase : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly string _masterConnectionString;

        private ApiTestDatabase(
            string databaseName,
            string masterConnectionString,
            string connectionString)
        {
            _databaseName = databaseName;
            _masterConnectionString = masterConnectionString;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ApiTestDatabase> CreateAsync()
        {
            var template = Environment.GetEnvironmentVariable(
                "HOSPITAL_AI_TEST_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new InvalidOperationException(
                    "运行 API 集成测试前必须设置 HOSPITAL_AI_TEST_CONNECTION_STRING。");
            }

            var databaseName = $"HospitalAi_Task8_{Guid.NewGuid():N}";
            var builder = new SqlConnectionStringBuilder(template)
            {
                InitialCatalog = databaseName
            };
            var masterBuilder = new SqlConnectionStringBuilder(builder.ConnectionString)
            {
                InitialCatalog = "master"
            };

            await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{databaseName}];";
                await command.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
                .UseSqlServer(builder.ConnectionString)
                .Options;
            await using var context = new HospitalAiDbContext(options);
            await context.Database.EnsureCreatedAsync();
            await using var verifyConnection = new SqlConnection(builder.ConnectionString);
            await verifyConnection.OpenAsync();

            return new ApiTestDatabase(
                databaseName,
                masterBuilder.ConnectionString,
                builder.ConnectionString);
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

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CaptureLogger(this);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLogger(CaptureLoggerProvider provider) : ILogger
    {
        private readonly AsyncLocal<List<string>> _scopes = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            _scopes.Value ??= [];
            _scopes.Value.Add(state.ToString() ?? string.Empty);
            return new Scope(() => _scopes.Value?.RemoveAt(_scopes.Value.Count - 1));
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            provider.Entries.Add(new LogEntry(
                exception is null
                    ? formatter(state, exception)
                    : $"{formatter(state, exception)} {exception}",
                _scopes.Value?.ToArray() ?? []));
        }

        private sealed class Scope(Action onDispose) : IDisposable
        {
            public void Dispose()
            {
                onDispose();
            }
        }
    }

    private sealed record LogEntry(
        string Message,
        IReadOnlyList<string> Scopes);

    private sealed record ImportResponse(
        string CodeSystem,
        int ImportedCount,
        int UpdatedCount);
}
