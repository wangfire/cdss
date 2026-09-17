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

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HospitalAi.Worker.dll"]
