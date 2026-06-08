FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy projects and restore
COPY ["src/AdaptiveLearning.Worker/AdaptiveLearning.Worker.csproj", "src/AdaptiveLearning.Worker/"]
COPY ["src/AdaptiveLearning.Contracts/AdaptiveLearning.Contracts.csproj", "src/AdaptiveLearning.Contracts/"]
COPY ["src/CoreLearningSystem.Application/CoreLearningSystem.Application.csproj", "src/CoreLearningSystem.Application/"]
COPY ["src/CoreLearningSystem.Domain/CoreLearningSystem.Domain.csproj", "src/CoreLearningSystem.Domain/"]
COPY ["src/CoreLearningSystem.Infrastructure/CoreLearningSystem.Infrastructure.csproj", "src/CoreLearningSystem.Infrastructure/"]

RUN dotnet restore "src/AdaptiveLearning.Worker/AdaptiveLearning.Worker.csproj"

# Copy all files and publish
COPY . .
WORKDIR "/src/src/AdaptiveLearning.Worker"
RUN dotnet publish "AdaptiveLearning.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Worker requires ASP.NET runtime because infrastructure references Hangfire.AspNetCore
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root user
USER $APP_UID

ENTRYPOINT ["dotnet", "AdaptiveLearning.Worker.dll"]
