FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy projects and restore
COPY ["src/CoreLearningSystem.API/CoreLearningSystem.API.csproj", "src/CoreLearningSystem.API/"]
COPY ["src/CoreLearningSystem.Application/CoreLearningSystem.Application.csproj", "src/CoreLearningSystem.Application/"]
COPY ["src/CoreLearningSystem.Domain/CoreLearningSystem.Domain.csproj", "src/CoreLearningSystem.Domain/"]
COPY ["src/CoreLearningSystem.Infrastructure/CoreLearningSystem.Infrastructure.csproj", "src/CoreLearningSystem.Infrastructure/"]
COPY ["src/AdaptiveLearning.Contracts/AdaptiveLearning.Contracts.csproj", "src/AdaptiveLearning.Contracts/"]

RUN dotnet restore "src/CoreLearningSystem.API/CoreLearningSystem.API.csproj"

# Copy all files and publish
COPY . .
WORKDIR "/src/src/CoreLearningSystem.API"
RUN dotnet publish "CoreLearningSystem.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Install curl for health check
USER root
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "CoreLearningSystem.API.dll"]
