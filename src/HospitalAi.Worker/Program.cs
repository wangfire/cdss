using HospitalAi.Worker;
using HospitalAi.Infrastructure.SqlServer;
using HospitalAi.Worker.Pipeline;
using HospitalAi.Worker.Retry;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var configuredConnectionString = builder.Configuration.GetConnectionString("HospitalAi");
var connectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
    ? configuredConnectionString
    : Environment.GetEnvironmentVariable("HOSPITAL_AI_CONNECTION_STRING")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=HospitalAi;Trusted_Connection=True;TrustServerCertificate=True;";

builder.Services.AddDbContext<HospitalAiDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<ICodingTaskPipelineRunner, PlaceholderCodingTaskPipelineRunner>();
builder.Services.AddScoped<IRetryDelay, TaskRetryDelay>();
builder.Services.AddCodingTaskConsumer(builder.Configuration);

var host = builder.Build();
host.Run();
