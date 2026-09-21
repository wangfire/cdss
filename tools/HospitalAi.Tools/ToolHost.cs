using Microsoft.Extensions.Configuration;

namespace HospitalAi.Tools;

/// <summary>
/// 归档工具入口。统一承载迁移 / 回填 / 评测脚本，替代散落在仓库根目录的临时 SQL。
///
/// 用法：
///   backfill   --mode dry-run|apply|verify --connection "..." [--hospital <guid>]
///   evaluate   --connection "..." [--golden <dir>] [--hospital <guid>]
///   golden     --validate [--golden <dir>]
///
/// 回填遵循“不做 destructive migration”：不删除旧列、不删除旧表，
/// 旧推荐只打标记（LEGACY_READ_ONLY + phase2-mvp-legacy），已确认 Final Coding 不动。
/// </summary>
public static class ToolHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables("HOSPITAL_AI_")
            .AddCommandLine(args)
            .Build();

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        try
        {
            switch (command)
            {
                case "backfill":
                    return await BackfillCommand.RunAsync(configuration);
                case "evaluate":
                    return await EvaluationCommand.RunAsync(configuration);
                case "golden":
                    return GoldenDatasetCommand.Run(configuration);
                case "help":
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"未知命令：{command}");
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"工具执行失败：{exception.Message}");
            return 1;
        }
    }

    internal static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration["Connection"]
            ?? configuration.GetConnectionString("HospitalAi")
            ?? Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "缺少连接字符串。使用 --connection 或设置 HOSPITAL_AI_CONNECTION_STRING。");
        }

        return connectionString;
    }

    internal static Guid? ResolveHospitalId(IConfiguration configuration)
    {
        var value = configuration["Hospital"];
        return Guid.TryParse(value, out var hospitalId) ? hospitalId : null;
    }

    internal static void Print(string message)
    {
        Console.WriteLine(message);
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            用法：
              backfill --mode dry-run|apply|verify --connection "<conn>" [--hospital <guid>]
              evaluate --connection "<conn>" [--golden <dir>] [--hospital <guid>]
              golden --validate [--golden <dir>]
            """);
    }
}
