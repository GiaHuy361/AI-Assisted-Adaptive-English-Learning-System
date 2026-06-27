# AI-Assisted Adaptive English Learning System

## Overview

The AI-Assisted Adaptive English Learning System is a software engineering graduation project designed to support personalized English learning. The system enables learners to take placement tests, follow adaptive learning paths, complete lessons and quizzes, track progress, receive recommendations, and improve weak skills through AI-assisted analysis.

---

## 1. Main Features

### 1.1. Learner Features
- Register and login (JWT Authentication)
- Take Placement Test to initialize starting level
- Follow personalized, dynamically updating learning paths
- Study lessons by skill and level
- Take quizzes and receive instant results
- Track progress metrics and view Skill Matrix
- Receive recommended lessons based on quiz performance and feedback
- Set learning goals and unlock badges/achievements
- Receive email and in-app learning reminders or notifications
- Submit targeted feedback on lessons, quizzes, and recommendations

### 1.2. Admin Features
- Manage users, learners, and system configurations
- Manage lessons, quizzes, and questions
- Inspect learner progress and review low-rating feedback aggregates
- Receive automated low-rating system alerts for degraded content

---

## 2. Technology Stack

| Component | Technology | Version / Configuration |
|---|---|---|
| **Backend API** | .NET 8 / ASP.NET Core | Port `5292` (host) / `8080` (container) |
| **Worker Service** | .NET 8 Background Service / Hangfire | Port-free worker service |
| **gRPC Service** | .NET 8 / ASP.NET Core gRPC | Port `50551` (gRPC HTTP/2), `50580` (HTTP/1.1 Health) |
| **Database** | MySQL | Port `3306` (host) / `3306` (container) |
| **Message Broker** | Apache Kafka | Port `9092` (host) / `29092` (container) |
| **Cache** | Redis | Port `6379` (host) / `6379` (container) |
| **Authentication** | JWT Bearer Tokens | HS256 Signing |
| **Orchestration** | Docker / Docker Compose | Pinned versions, bridge network, named volumes |

---

## 3. Prerequisites

Before running the application, make sure you have the following installed:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running Linux containers mode)
- [Git](https://git-scm.com/)

---

## 4. Local Environment Setup

1. Clone the repository and switch to the main branch:
   ```bash
   git checkout main
   git pull origin main
   ```
2. Create the `.env` file in the root directory by copying the template:
   ```bash
   cp .env.example .env
   ```
3. Open `.env` and configure the root/user passwords, JWT secret, and environment values.

---

## 5. Running with Docker Compose

The environment contains a default production-ready profile and a `dev-tools` profile containing administration interfaces.

### 5.1. Run Core System Only
Starts MySQL, Kafka, Redis, gRPC Service, API, and the Background Worker:
```bash
docker compose up -d
```

### 5.2. Run with Development Tools (Kafka UI & Redis Commander)
```bash
docker compose --profile dev-tools up -d
```
- **REST API Swagger**: `http://localhost:5292/swagger`
- **Kafka UI**: `http://localhost:8085`
- **Redis Commander**: `http://localhost:8086`

### 5.3. Rebuild Containers (Clean State)
```bash
docker compose down -v
docker compose build --no-cache
docker compose up -d
```

---

## 6. Running Locally (Without Docker)

If you wish to run services locally on your host environment:

1. Start local infrastructure containers (databases and broker):
   ```bash
   docker compose up -d mysql kafka redis
   ```
2. Apply database migrations to local MySQL:
   ```bash
   dotnet ef database update --project src/CoreLearningSystem.Infrastructure --startup-project src/CoreLearningSystem.API
   ```
3. Run the services from project directories:
   - **gRPC Service**:
     ```bash
     dotnet run --project src/AdaptiveLearning.GrpcService
     ```
   - **REST API**:
     ```bash
     dotnet run --project src/CoreLearningSystem.API
     ```
   - **Background Worker**:
     ```bash
     dotnet run --project src/AdaptiveLearning.Worker
     ```

---

## 7. Architecture Diagrams & Details

For complete Mermaid sequence flow charts, database transaction boundaries, and component maps, see the [Architecture Document](file:///d:/PRN232/AI-Assisted-Adaptive-English-Learning-System/docs/architecture.md).

For troubleshooting logs, backups, and common port conflicts, see the [Operations Guide](file:///d:/PRN232/AI-Assisted-Adaptive-English-Learning-System/docs/operations.md).

### 7.1. Kafka Topics (Backend Internal)

> These topics are managed internally by the Worker service. **Frontend does not interact with Kafka directly.**

- `placement-test-completed-topic`: Placement test details published on submission.
- `quiz-submitted-topic`: Raw quiz answers and scores for worker consumption.
- `lesson-completed-topic`: Triggered when a learner completes studying a lesson.
- `goal-completed-topic`: Published when a learning target is achieved.
- `badge-awarded-topic`: Broadcasts earned/unlocked badge info.
- `feedback-submitted-topic`: Dispatched when learner submits reviews.
- `notification-created-topic`: Triggers email/in-app delivery worker loops.
- `dead-letter-topic`: Topic where poison/unhandled failure messages are routed.

### 7.2. Redis Cache Keys (Backend Internal)

> These cache keys are managed internally by the API and Worker services. **Frontend does not interact with Redis directly.**

- `adaptive:v1:lessons:list-version`: Key keeping track of the latest version of the lesson list.
- `adaptive:v1:lessons:list:v{N}:{skill}:{level}:{role}`: Versioned cache for lesson list queries.
- `adaptive:v1:lessons:detail:{id}`: Cached lesson detail metadata.
- `adaptive:v1:skill-matrix:{profileId}`: Serialized learner skill matrices.
- `adaptive:v1:recommendations:active:{profileId}`: Active lesson recommendations.
- `adaptive:v1:progress:summary:{profileId}`: Core progress tracking caches.
- `adaptive:v1:processed-event:{eventId}`: Key preventing duplicate event execution (distributed idempotency).

---

## 8. Health Checks

Health checkpoints are registered on all core services:
- **API `/health`**: Returns `Healthy` if MySQL connection is open. Degraded if Redis is disconnected.
- **gRPC `/health`**: Accessible via HTTP/1.1 on port `50580` returning JSON `{ "Status": "Healthy" }`.
- **Worker Health**: Periodically writes status parameters to `/tmp/adaptive-worker-health.txt` containing Kafka connection state, Redis connection state, gRPC reachability, and Hangfire MySQL storage connection. Checked natively by Docker healthcheck commands.

---

## 9. Testing

### 9.1. Run Unit & Integration Tests (Local)
To execute all 142 automated tests:
```bash
dotnet test
```

### 9.2. Enforcing Infrastructure Dependency Checks
By default, integration tests return early (pass) if Redis or Kafka are unavailable. To enforce check-failure when these services are missing:
```bash
# PowerShell
$env:REQUIRE_INFRASTRUCTURE_TESTS="true"
dotnet test

# Bash / Linux
REQUIRE_INFRASTRUCTURE_TESTS=true dotnet test
```

### 9.3. Run Docker Compose Smoke Test
To verify the E2E health of the containerized stack:
- **PowerShell**:
  ```powershell
  ./scripts/docker-smoke-test.ps1
  ```
- **Bash**:
  ```bash
  chmod +x ./scripts/docker-smoke-test.sh
  ./scripts/docker-smoke-test.sh
  ```

---

## 10. Frontend Scope Summary

Frontend only needs to call REST API at `http://localhost:5292`. See [Frontend Integration Guide](./docs/frontend-api-handoff-huy-adaptive.md) for full details.

**Frontend screens to build (Huy's scope):**
- Adaptive Dashboard (Skill Matrix + progress overview)
- Learning Path / Recommended Lessons
- Goals page
- Notifications
- Admin Feedback Analysis

**Frontend does NOT build UI for:**
- Kafka / DLQ management
- Redis cache internals
- Hangfire job management
- Worker / gRPC internals
- Outbox Pattern
- Session cleanup
- Recommendation effectiveness analytics
- Docker infrastructure

**Frontend does NOT rewrite Hoang's scope:**
- Auth, Lesson, Quiz, Placement Test, Progress (basic), Feedback (submit), Admin Dashboard (basic)

---

## 11. Known Limitations

- **Outbox Pattern**: Not fully implemented. Event publish errors during database transaction commits are resolved by consumer-side retry loops.
- **Email Gateway**: Weekly report emails default to `DevelopmentEmailSender` (logs outbound emails to standard log files) unless a valid SMTP host is configured.
- **Certificate Verification**: Goals like IELTS/TOEIC estimate completion progress but require manual certificate confirmation (PARTIAL state).
