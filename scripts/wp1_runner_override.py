# -*- coding: utf-8 -*-
"""WP1：runner 每次执行时从 DB 读运行时覆盖项合并 IOptions。

改动：
  1. runner 注入 IDbContextFactory 不行——runner 已有 _dbContext（scoped）。
     直接在 ExecuteAsync 开头查 PipelineRuntimeConfigs 表读 v22lite 行，
     反序列化 JSON → 用 System.Text.Json 手动把覆盖字段写回 settings 副本。
  2. settings 是 IOptions.Value（单例），不能改——做一个浅拷贝再覆盖。
"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"src\HospitalAi.Worker\Pipeline\V22LiteCodingPipelineRunner.cs"
c = open(f, encoding="utf-8").read()

# 1. ExecuteAsync 开头插入运行时覆盖读取
old = """    public async Task ExecuteAsync(
        CodingTaskCreatedMessage message,
        IReadOnlyList<Guid>? diagnosisInputIds,
        CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;"""
new = """    public async Task ExecuteAsync(
        CodingTaskCreatedMessage message,
        IReadOnlyList<Guid>? diagnosisInputIds,
        CancellationToken cancellationToken = default)
    {
        // 运行时覆盖项（工作台设置抽屉）：每次执行读一次 DB 单行配置，
        // 合并到 options 副本上——API 更新配置后下一次任务即生效，无需重启 Worker。
        var settings = await LoadEffectiveOptionsAsync(cancellationToken);"""
assert old in c, "execute anchor"
c = c.replace(old, new, 1)

# 2. 文件尾部追加 LoadEffectiveOptionsAsync（放在类的最后一个方法后，即文件末尾 "}" 前）
# 找类结束位置：文件以 "}" 结尾（namespace file-scoped）
tail_anchor = c.rstrip("\n")
assert tail_anchor.endswith("}"), "file tail"
method = """
    /// <summary>
    /// 读取运行时配置覆盖项：pipeline_runtime_config 单行（config_key='v22lite'），
    /// JSON 字段逐个覆盖 options 副本（System.Text.Json 手动映射，避免反射依赖）。
    /// 无配置行或解析失败时原样返回默认值（appsettings 基线）。
    /// </summary>
    private async Task<V22PipelineOptions> LoadEffectiveOptionsAsync(
        CancellationToken cancellationToken)
    {
        var settings = new V22PipelineOptions();
        _options.Value.CopyTo(settings);
        try
        {
            var row = await _dbContext.PipelineRuntimeConfigs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.ConfigKey == "v22lite", cancellationToken);
            if (row is null)
            {
                return settings;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(row.ConfigJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "IcdTitleDirectEnabled":
                        settings.IcdTitleDirectEnabled = prop.Value.GetBoolean();
                        break;
                    case "Cm3TitleDirectEnabled":
                        settings.Cm3TitleDirectEnabled = prop.Value.GetBoolean();
                        break;
                    case "IcdTitleReverseEnabled":
                        settings.IcdTitleReverseEnabled = prop.Value.GetBoolean();
                        break;
                    case "VectorEnabled":
                        settings.VectorEnabled = prop.Value.GetBoolean();
                        break;
                    case "RerankEnabled":
                        settings.RerankEnabled = prop.Value.GetBoolean();
                        break;
                    case "RuleEngineEnabled":
                        settings.RuleEngineEnabled = prop.Value.GetBoolean();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "流水线运行时配置读取失败，使用 appsettings 基线。TaskId={TaskId}", message.TaskId);
        }

        return settings;
    }
"""
c = tail_anchor[: len(tail_anchor) - 1].rstrip("\n") + "\n" + method + "}\n"
open(f, "w", encoding="utf-8").write(c)
print("runner runtime override added")
