FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HospitalAi.slnx ./
COPY src/HospitalAi.Worker/HospitalAi.Worker.csproj src/HospitalAi.Worker/
COPY src/HospitalAi.Application/HospitalAi.Application.csproj src/HospitalAi.Application/
COPY src/HospitalAi.Contracts/HospitalAi.Contracts.csproj src/HospitalAi.Contracts/
COPY src/HospitalAi.Domain/HospitalAi.Domain.csproj src/HospitalAi.Domain/
COPY src/HospitalAi.Infrastructure/HospitalAi.Infrastructure.csproj src/HospitalAi.Infrastructure/
RUN dotnet restore src/HospitalAi.Worker/HospitalAi.Worker.csproj

COPY src/ src/
RUN dotnet publish src/HospitalAi.Worker/HospitalAi.Worker.csproj -c Release -o /app/publish --no-restore

# Worker 当前依赖 OpenTelemetry.Exporter.Prometheus.AspNetCore，
# 发布结果需要 Microsoft.AspNetCore.App 运行时，因此使用 ASP.NET 运行时镜像。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HospitalAi.Worker.dll"]
