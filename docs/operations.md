# Operations and Maintenance Documentation

This guide covers operational commands, environment management, backup strategies, and troubleshooting steps for the AI-Assisted Adaptive English Learning System.

---

## 1. Quick Start / Stop / Rebuild

To manage the Docker Compose environment, run these commands from the root directory of the project:

### 1.1. Start Core Services
Starts MySQL, Kafka, Redis, the REST API, Grpc Service, and the Worker in the background:
```bash
docker compose up -d
```

### 1.2. Start with Development Tools
Starts the core services along with Kafka UI (`localhost:8085`) and Redis Commander (`localhost:8086`):
```bash
docker compose --profile dev-tools up -d
```

### 1.3. Rebuild Services
Builds and starts all containers from scratch:
```bash
docker compose build --no-cache
docker compose up -d
```

### 1.4. Stop Services
Stops all containers without deleting data volumes:
```bash
docker compose down
```

### 1.5. Clean Stop (Delete Volumes)
Stops containers and wipes database tables/cache keys for a clean start:
```bash
docker compose down -v
```

---

## 2. Viewing and Inspecting Container Logs

### 2.1. Follow All Logs
```bash
docker compose logs -f
```

### 2.2. Follow Specific Service Logs
```bash
docker compose logs -f api
docker compose logs -f worker
docker compose logs -f grpc-service
```

---

## 3. Storage and Cache Inspection

### 3.1. Redis Inspection
Connect to the Redis command-line interface inside the container:
```bash
docker exec -it adaptive-learning-redis redis-cli
```
Useful commands:
- `PING`: Verifies the server is active.
- `KEYS *`: List all keys (avoid in production!).
- `SMEMBERS adaptive:v1:lessons:detail-set`: View all cached lesson detail key tracks.

### 3.2. Kafka Inspection
Query available topics inside the Kafka container:
```bash
docker exec -it adaptive-learning-kafka kafka-topics --bootstrap-server localhost:9092 --list
```
Consume messages from the beginning of a specific topic:
```bash
docker exec -it adaptive-learning-kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic quiz-submitted-topic --from-beginning
```

---

## 4. Database Schema Migrations

### 4.1. Apply Migrations Locally (Host Machine)
Run this command from the project root if you need to apply migrations manually on your host SQL server:
```bash
dotnet ef database update --project src/CoreLearningSystem.Infrastructure --startup-project src/CoreLearningSystem.API
```

### 4.2. Create a New Migration
```bash
dotnet ef migrations add Phase9_AddYourChanges --project src/CoreLearningSystem.Infrastructure --startup-project src/CoreLearningSystem.API
```

---

## 5. MySQL Volume Backup & Reset

### 5.1. Backup Local Volume
To create a raw SQL backup of the local MySQL instance while it is running:
```bash
docker exec -it adaptive-learning-mysql mysqldump -u root -pchange_this_root_password_in_prod AdaptiveEnglishLearningDb > backup.sql
```

### 5.2. Restore from SQL Backup
```bash
docker exec -i adaptive-learning-mysql mysql -u root -pchange_this_root_password_in_prod AdaptiveEnglishLearningDb < backup.sql
```

### 5.3. Reset Development Environment
To completely wipe MySQL tables, Hangfire logs, Redis cache, and Kafka topics:
```bash
docker compose down -v
docker compose up -d
```

---

## 6. Port Conflicts & Troubleshooting

### 6.1. Common Port Conflicts
If a service fails to start, verify if another application is binding the host ports:
- **MySQL**: `3306` (check if a local MySQL or MariaDB service is running)
- **Redis**: `6379` (check if local Redis server is active)
- **Kafka**: `9092`
- **API**: `5292`
- **gRPC health**: `50080`
- **gRPC HTTP/2**: `50051`

### 6.2. Verify Host Ports (Windows PowerShell)
```powershell
Get-NetTCPConnection -LocalPort 3306, 6379, 9092, 5292 -ErrorAction SilentlyContinue
```

### 6.3. Check Container Health Statuses
```bash
docker compose ps
```
Look for `(healthy)` next to the container state. If a container shows `(unhealthy)` or `Exit 137`:
- Inspect logs: `docker compose logs [service_name]`
- Verify resource limits (Memory limit or disk space issues).
- Ensure the `.env` file is present in the root directory.
