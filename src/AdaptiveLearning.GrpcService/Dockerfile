FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy projects and restore
COPY ["src/AdaptiveLearning.GrpcService/AdaptiveLearning.GrpcService.csproj", "src/AdaptiveLearning.GrpcService/"]
COPY ["src/AdaptiveLearning.Contracts/AdaptiveLearning.Contracts.csproj", "src/AdaptiveLearning.Contracts/"]

RUN dotnet restore "src/AdaptiveLearning.GrpcService/AdaptiveLearning.GrpcService.csproj"

# Copy all files and publish
COPY . .
WORKDIR "/src/src/AdaptiveLearning.GrpcService"
RUN dotnet publish "AdaptiveLearning.GrpcService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Install curl for health check
USER root
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID

EXPOSE 50051
EXPOSE 50080
ENTRYPOINT ["dotnet", "AdaptiveLearning.GrpcService.dll"]
