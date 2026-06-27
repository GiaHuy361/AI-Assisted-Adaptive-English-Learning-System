#!/usr/bin/env bash
# docker-smoke-test.sh
# Automates Phase 9 container startup, health checkpoints, and verification.

set -e

echo -e "\e[32m=== Starting Docker Smoke Test ===\e[0m"

# 1. Reset container state
echo -e "\e[33mCleaning previous containers and volumes...\e[0m"
docker compose down -v

# 2. Build and start services
echo -e "\e[33mBuilding and starting services...\e[0m"
docker compose build --no-cache
docker compose up -d

# 3. Wait for health checks
echo -e "\e[33mWaiting for services to become healthy...\e[0m"
TIMEOUT=120
ELAPSED=0
INTERVAL=5
ALL_HEALTHY=false

REQUIRED_SERVICES=("adaptive-learning-mysql" "adaptive-learning-redis" "adaptive-learning-kafka" "adaptive-learning-grpc-service" "adaptive-learning-api" "adaptive-learning-worker")

while [ $ELAPSED -lt $TIMEOUT ]; do
    sleep $INTERVAL
    ELAPSED=$((ELAPSED + INTERVAL))
    
    ALL_HEALTHY=true
    for service in "${REQUIRED_SERVICES[@]}"; do
        HAS_HEALTH=$(docker inspect --format='{{if .State.Health}}{{json .State.Health.Status}}{{else}}null{{end}}' "$service" 2>/dev/null || echo "null")
        STATE=$(docker inspect --format='{{json .State.Status}}' "$service" 2>/dev/null || echo '"unknown"')
        
        if [ "$HAS_HEALTH" != "null" ]; then
            echo "Service $service Health: $HAS_HEALTH (State: $STATE)"
            if [ "$HAS_HEALTH" != '"healthy"' ]; then
                ALL_HEALTHY=false
            fi
        else
            echo "Service $service State: $STATE"
            if [ "$STATE" != '"running"' ]; then
                ALL_HEALTHY=false
            fi
        fi
    done
    
    if [ "$ALL_HEALTHY" = true ]; then
        echo -e "\e[32mAll core services are healthy!\e[0m"
        break
    fi
done

if [ "$ALL_HEALTHY" = false ]; then
    echo -e "\e[31mTimeout reached. Some services failed to become healthy.\e[0m"
    docker compose ps
    docker compose logs
    exit 1
fi

# 4. Query Health Endpoints
echo -e "\e[33mVerifying API health endpoint...\e[0m"
API_HEALTH=$(curl -sf http://localhost:5292/health || echo "FAILED")
if [ "$API_HEALTH" = "FAILED" ]; then
    echo -e "\e[31mAPI Health Request failed\e[0m"
    exit 1
fi
echo -e "\e[32mAPI Health Response: $API_HEALTH\e[0m"

echo -e "\e[33mVerifying gRPC health endpoint...\e[0m"
GRPC_HEALTH=$(curl -sf http://localhost:50580/health || echo "FAILED")
if [ "$GRPC_HEALTH" = "FAILED" ]; then
    echo -e "\e[31mgRPC Health Request failed\e[0m"
    exit 1
fi
echo -e "\e[32mgRPC Health Response: $GRPC_HEALTH\e[0m"

# 5. Run Authentication Smoke Flow
echo -e "\e[33mRunning Register & Login smoke flow...\e[0m"
RAND=$RANDOM
UNIQUE_USER="smoke_user_$RAND"
REGISTER_BODY="{\"username\":\"$UNIQUE_USER\",\"email\":\"$UNIQUE_USER@test.com\",\"password\":\"SecurePassword123!\",\"fullName\":\"Smoke Test User\"}"

echo "Registering user: $UNIQUE_USER"
REG_RESPONSE=$(curl -s -X POST -H "Content-Type: application/json" -d "$REGISTER_BODY" http://localhost:5292/api/auth/register)
echo "Register Response: $REG_RESPONSE"

LOGIN_BODY="{\"username\":\"$UNIQUE_USER\",\"password\":\"SecurePassword123!\"}"
LOGIN_RESPONSE=$(curl -s -X POST -H "Content-Type: application/json" -d "$LOGIN_BODY" http://localhost:5292/api/auth/login)
echo "Login Response: $LOGIN_RESPONSE"

TOKEN=$(echo "$LOGIN_RESPONSE" | grep -o '"token":"[^"]*' | grep -o '[^"]*$' || true)

if [ -z "$TOKEN" ]; then
    echo -e "\e[31mAuthentication smoke flow failed: No token returned.\e[0m"
    exit 1
fi

echo -e "\e[32mAuthentication success! JWT Token retrieved successfully.\e[0m"
echo -e "\e[32m=== Smoke Test PASSED successfully ===\e[0m"
