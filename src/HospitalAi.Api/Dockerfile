FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HospitalAi.slnx ./
COPY src/HospitalAi.Api/HospitalAi.Api.csproj src/HospitalAi.Api/
COPY src/HospitalAi.Application/HospitalAi.Application.csproj src/HospitalAi.Application/
COPY src/HospitalAi.Contracts/HospitalAi.Contracts.csproj src/HospitalAi.Contracts/
COPY src/HospitalAi.Domain/HospitalAi.Domain.csproj src/HospitalAi.Domain/
COPY src/HospitalAi.Infrastructure/HospitalAi.Infrastructure.csproj src/HospitalAi.Infrastructure/
RUN dotnet restore src/HospitalAi.Api/HospitalAi.Api.csproj

COPY src/ src/
RUN dotnet publish src/HospitalAi.Api/HospitalAi.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HospitalAi.Api.dll"]
