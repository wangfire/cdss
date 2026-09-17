using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HospitalAi.Infrastructure.SqlServer;

/// <summary>
/// 为 EF Core CLI 提供设计时 DbContext，避免迁移生成依赖运行时服务注册。
/// </summary>
public sealed class HospitalAiDbContextFactory
    : IDesignTimeDbContextFactory<HospitalAiDbContext>
{
    public HospitalAiDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=HospitalAi;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<HospitalAiDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new HospitalAiDbContext(options);
    }
}
