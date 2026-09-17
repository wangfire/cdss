using System.Text.Json;
using HospitalAi.Contracts.CodingTasks;

namespace HospitalAi.Application.Tests;

public sealed class ContractDtoTests
{
    [Fact]
    public void Serialize_编码任务请求使用camelCase字段()
    {
        var visitId = Guid.NewGuid();
        var request = new CreateCodingTaskRequest(visitId, "pipeline-v1");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request));
        var root = document.RootElement;

        Assert.Equal(visitId, root.GetProperty("visitId").GetGuid());
        Assert.Equal("pipeline-v1", root.GetProperty("pipelineVersion").GetString());
        Assert.False(root.TryGetProperty("VisitId", out _));
        Assert.False(root.TryGetProperty("PipelineVersion", out _));
    }
}
