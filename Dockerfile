# Dockerfile for PayFlow API (.NET 10)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["PayFlow.slnx", "./"]
COPY ["src/PayFlow.Domain/PayFlow.Domain.csproj", "src/PayFlow.Domain/"]
COPY ["src/PayFlow.Application/PayFlow.Application.csproj", "src/PayFlow.Application/"]
COPY ["src/PayFlow.Infrastructure/PayFlow.Infrastructure.csproj", "src/PayFlow.Infrastructure/"]
COPY ["src/PayFlow.Api/PayFlow.Api.csproj", "src/PayFlow.Api/"]
COPY ["tests/PayFlow.UnitTests/PayFlow.UnitTests.csproj", "tests/PayFlow.UnitTests/"]
COPY ["tests/PayFlow.IntegrationTests/PayFlow.IntegrationTests.csproj", "tests/PayFlow.IntegrationTests/"]

RUN dotnet restore "PayFlow.slnx"

# Copy full source and build
COPY . .
WORKDIR "/src/src/PayFlow.Api"
RUN dotnet build "PayFlow.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PayFlow.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PayFlow.Api.dll"]
