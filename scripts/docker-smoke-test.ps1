# docker-smoke-test.ps1
# Automates Phase 9 container startup, health checkpoints, and verification.

$ErrorActionPreference = "Stop"

Write-Host "=== Starting Docker Smoke Test ===" -ForegroundColor Green

# 1. Reset container state
Write-Host "Cleaning previous containers and volumes..." -ForegroundColor Yellow
docker compose down -v

# 2. Build and start services
Write-Host "Building and starting services..." -ForegroundColor Yellow
docker compose build --no-cache
docker compose up -d

# 3. Wait for health checks
Write-Host "Waiting for services to become healthy..." -ForegroundColor Yellow
$timeoutSeconds = 120
$elapsed = 0
$interval = 5
$allHealthy = $false

$requiredServices = @("adaptive-learning-mysql", "adaptive-learning-redis", "adaptive-learning-kafka", "adaptive-learning-grpc-service", "adaptive-learning-api", "adaptive-learning-worker")

while ($elapsed -lt $timeoutSeconds) {
    Start-Sleep -Seconds $interval
    $elapsed += $interval
    
    $statuses = docker compose ps --format json | ConvertFrom-Json
    $allHealthy = $true
    
    foreach ($serviceName in $requiredServices) {
        $service = $statuses | Where-Object { $_.Name -eq $serviceName }
        if ($null -eq $service) {
            $allHealthy = $false
            Write-Host "Service $serviceName is not created yet." -ForegroundColor Gray
            continue
        }
        
        $hasHealthcheck = ($null -ne $service.Health) -and ($service.Health -ne "")
        if ($hasHealthcheck) {
            Write-Host "Service $serviceName Health: $($service.Health) (State: $($service.State))"
            if ($service.Health -ne "healthy") {
                $allHealthy = $false
            }
        } else {
            Write-Host "Service $serviceName State: $($service.State)"
            if ($service.State -ne "running") {
                $allHealthy = $false
            }
        }
    }
    
    if ($allHealthy) {
        Write-Host "All core services are healthy!" -ForegroundColor Green
        break
    }
}

if (-not $allHealthy) {
    Write-Host "Timeout reached. Some services failed to become healthy." -ForegroundColor Red
    docker compose ps
    docker compose logs
    Exit 1
}

# 4. Query Health Endpoints
Write-Host "Verifying API health endpoint..." -ForegroundColor Yellow
try {
    $apiResponse = Invoke-RestMethod -Uri "http://localhost:5292/health" -Method Get
    Write-Host "API Health Response: $apiResponse" -ForegroundColor Green
} catch {
    Write-Host "API Health Request failed: $_" -ForegroundColor Red
    Exit 1
}

Write-Host "Verifying gRPC health endpoint..." -ForegroundColor Yellow
try {
    $grpcResponse = Invoke-RestMethod -Uri "http://localhost:50580/health" -Method Get
    Write-Host "gRPC Health Response: $grpcResponse" -ForegroundColor Green
} catch {
    Write-Host "gRPC Health Request failed: $_" -ForegroundColor Red
    Exit 1
}

# 5. Run Authentication Smoke Flow
Write-Host "Running Register & Login smoke flow..." -ForegroundColor Yellow
$uniqueUser = "smoke_user_" + (Get-Random)
$registerBody = @{
    username = $uniqueUser
    email = "$uniqueUser@test.com"
    password = "SecurePassword123!"
    fullName = "Smoke Test User"
} | ConvertTo-Json

try {
    $regResponse = Invoke-RestMethod -Uri "http://localhost:5292/api/auth/register" -Method Post -Body $registerBody -ContentType "application/json"
    Write-Host "Register Success: Username=$uniqueUser" -ForegroundColor Green
    
    $loginBody = @{
        username = $uniqueUser
        password = "SecurePassword123!"
    } | ConvertTo-Json
    
    $loginResponse = Invoke-RestMethod -Uri "http://localhost:5292/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
    $token = $loginResponse.data.token
    if ($token) {
        Write-Host "Login Success. JWT Token retrieved!" -ForegroundColor Green
    } else {
        Write-Host "Login failed: No token returned." -ForegroundColor Red
        Exit 1
    }
} catch {
    Write-Host "Authentication smoke flow failed: $_" -ForegroundColor Red
    Exit 1
}

Write-Host "=== Smoke Test PASSED successfully ===" -ForegroundColor Green
