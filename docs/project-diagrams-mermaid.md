# Mermaid Diagrams – AI-Assisted Adaptive English Learning System (Architecture, Sequence, Deployment)

Tài liệu này chứa các biểu đồ Mermaid trực quan phục vụ cho mục **11. Architecture Diagram** trong tài liệu `project_proposal.docx`.

---

## 1. Hướng dẫn dùng:
- **Bước 1**: Copy toàn bộ nội dung của một block mã Mermaid (nằm trong khối ```mermaid ... ```).
- **Bước 2**: Truy cập vào trang web [Mermaid Live Editor](https://mermaid.live).
- **Bước 3**: Dán đoạn mã vừa copy vào khung soạn thảo bên trái (khu vực "Code").
- **Bước 4**: Kiểm tra hình ảnh xem trước hiển thị ở khung bên phải. Chọn nút **Actions** -> **Export** -> tải về định dạng **SVG** (để chèn vào Word sắc nét, không bị vỡ hạt) hoặc **PNG**.
- **Bước 5**: Chèn ảnh đã tải về vào mục **11. Architecture Diagram** trong file `project_proposal.docx`.

---

## 2. System Architecture Diagram
Biểu đồ mô tả kiến trúc tổng quan hệ thống từ Client đến Gateway API, các dịch vụ xử lý nền, gRPC service và các cơ sở dữ liệu/caching/messaging.

```mermaid
flowchart TD
    %% Define Subgraphs
    subgraph ClientLayer ["Client Layer"]
        UserWeb["User Web App (SPA)"]
        AdminWeb["Admin Web App (SPA)"]
    end

    subgraph ApiLayer ["Gateway & API Layer"]
        APIGateway["CoreLearningSystem.API (API Gateway)"]
    end

    subgraph MessagingLayer ["Messaging Layer"]
        Kafka["Kafka Message Broker"]
    end

    subgraph ProcessingLayer ["Background Processing Layer"]
        Worker["AdaptiveLearning.Worker"]
        Hangfire["Hangfire Scheduler (Runs in Worker)"]
    end

    subgraph GrpcLayer ["gRPC Service Layer"]
        GrpcService["AdaptiveLearning.GrpcService"]
    end

    subgraph StorageLayer ["Storage & Caching Layer"]
        MySQL[("MySQL Database")]
        Redis[("Redis Distributed Cache")]
    end

    subgraph ExternalLayer ["External Services"]
        SMTPServer["SMTP Server (Gmail)"]
    end

    %% Define Connections
    UserWeb & AdminWeb -->|HTTP / REST API| APIGateway
    APIGateway -->|Read/Write| MySQL
    APIGateway -->|Cache Lookup / Write| Redis
    APIGateway -->|Publish Business Events| Kafka

    Kafka -->|Consume Events| Worker
    Worker -->|gRPC HTTP2 Call| GrpcService
    GrpcService -->|Read/Write Model Data| MySQL
    Worker -->|Update Learning Path & Profiles| MySQL
    Worker -->|Invalidate Affected Cache Keys| Redis
    Worker -->|Generate Notifications| MySQL
    
    Hangfire -->|Fetch & Run Jobs| MySQL
    Worker & Hangfire -->|Send Emails/Reports| SMTPServer

    %% Styling
    classDef client fill:#e1f5fe,stroke:#0288d1,stroke-width:2px,color:#000;
    classDef api fill:#e8f5e9,stroke:#388e3c,stroke-width:2px,color:#000;
    classDef msg fill:#fff3e0,stroke:#f57c00,stroke-width:2px,color:#000;
    classDef proc fill:#f3e5f5,stroke:#7b1fa2,stroke-width:2px,color:#000;
    classDef grpc fill:#efebe9,stroke:#5d4037,stroke-width:2px,color:#000;
    classDef store fill:#eceff1,stroke:#455a64,stroke-width:2px,color:#000;
    classDef ext fill:#ffebee,stroke:#c62828,stroke-width:2px,color:#000;

    class UserWeb,AdminWeb client;
    class APIGateway api;
    class Kafka msg;
    class Worker,Hangfire proc;
    class GrpcService grpc;
    class MySQL,Redis store;
    class SMTPServer ext;
```

---

## 3. Event Flow / Sequence Diagram (Luồng Quiz Submit)
Biểu đồ tuần tự (Sequence Diagram) mô tả chi tiết luồng xử lý bất đồng bộ dựa trên sự kiện khi học viên hoàn thành một bài kiểm tra ngắn (Quiz).

```mermaid
sequenceDiagram
    autonumber
    actor Learner as Learner (Frontend)
    participant API as CoreLearningSystem.API
    participant DB as MySQL Database
    participant Cache as Redis Cache
    participant Kafka as Kafka Broker
    participant Worker as AdaptiveLearning.Worker
    participant gRPC as AdaptiveLearning.GrpcService

    Learner->>API: POST /api/quizzes/{id}/submit
    activate API
    API->>DB: Save QuizAttempt & Answers (in transaction)
    activate DB
    DB-->>API: Success (AttemptId)
    deactivate DB
    API->>Kafka: Publish QuizSubmittedEvent
    activate Kafka
    Kafka-->>API: Acknowledged
    deactivate Kafka
    API-->>Learner: HTTP 200 OK (AttemptResult)
    deactivate API

    Note over Worker: Worker consumes QuizSubmittedEvent
    Kafka->>Worker: Consume Event
    activate Worker
    Worker->>Cache: Check Idempotency (EventId)
    activate Cache
    Cache-->>Worker: Is New Event (Set Lock)
    deactivate Cache

    Worker->>gRPC: AnalyzeQuizSubmissionAsync(AttemptId)
    activate gRPC
    gRPC->>DB: Read quiz detail and user answers
    gRPC-->>Worker: Return Analysis Result (Weaknesses & Recommendations)
    deactivate gRPC

    Worker->>DB: Update SkillMatrix, LearningPath/Recommendation, Goals, Badges, Notifications (in transaction)
    activate DB
    DB-->>Worker: Transaction Committed
    deactivate DB

    Worker->>Cache: Invalidate cache (SkillMatrix, Recommendations, Goals)
    activate Cache
    Cache-->>Worker: Invalidated
    deactivate Cache
    deactivate Worker

    Note over Learner: Frontend refetches new data
    Learner->>API: GET /api/learningpaths/current & GET /api/notifications
    activate API
    API->>Cache: Read (Cache Miss or Hit)
    API->>DB: Read fresh data (if Cache Miss)
    API-->>Learner: Return updated stats & recommendations
    deactivate API
```

---

## 4. Deployment Diagram
Biểu đồ triển khai mô tả cấu trúc mạng Docker Compose, các container dịch vụ, cổng kết nối (Port) ra ngoài và liên kết phân chia cơ sở dữ liệu thông qua volumes.

```mermaid
flowchart TB
    %% Host and External Browser
    subgraph Host ["Docker Host / Local Machine"]
        Browser["Web Browser / Frontend Dev Server (Port 3000)"]

        subgraph DockerNetwork ["Docker Bridge Network (adaptive-network)"]
            
            %% API Container
            API["adaptive-learning-api<br/>(Container Port: 8080 / Host Port: 5292)"]
            
            %% Worker Container
            Worker["adaptive-learning-worker<br/>(Background Execution)"]
            
            %% gRPC Service Container
            gRPC["adaptive-learning-grpc-service<br/>(Host Port: 50051 / 50080)"]
            
            %% Database Container
            MySQL["adaptive-learning-mysql<br/>(Container Port: 3306 / Host Port: 33066)"]
            
            %% Cache Container
            Redis["adaptive-learning-redis<br/>(Container Port: 6379 / Host Port: 6379)"]
            
            %% Message Broker Container
            Kafka["adaptive-learning-kafka<br/>(Container Port: 9092 / Host Port: 9092)"]
            
        end
        
        %% Named Volumes
        subgraph Volumes ["Docker Named Volumes"]
            mysql_vol[("mysql-data")]
            redis_vol[("redis-data")]
            kafka_vol[("kafka-data")]
        end
    end

    %% External Connections
    Browser -->|HTTP REST Call: Port 5292| API
    
    %% Internal Dependencies & Communication (adaptive-network)
    API -.->|depends_on| MySQL
    API -.->|depends_on| Redis
    API -.->|depends_on| Kafka
    API -.->|depends_on| gRPC
    
    Worker -.->|depends_on| MySQL
    Worker -.->|depends_on| Redis
    Worker -.->|depends_on| Kafka
    Worker -.->|depends_on| gRPC
    
    gRPC -.->|depends_on| MySQL
    
    %% Actual Traffic
    API -->|TCP: 3306| MySQL
    API -->|TCP: 6379| Redis
    API -->|TCP: 9092| Kafka
    API -->|gRPC HTTP2: 50051| gRPC
    
    Worker -->|TCP: 3306| MySQL
    Worker -->|TCP: 6379| Redis
    Worker -->|TCP: 9092| Kafka
    Worker -->|gRPC HTTP2: 50051| gRPC
    
    gRPC -->|TCP: 3306| MySQL
    
    %% Volume Mounts
    MySQL === mysql_vol
    Redis === redis_vol
    Kafka === kafka_vol

    %% Styling
    classDef browser fill:#e3f2fd,stroke:#1e88e5,stroke-width:2px,color:#000;
    classDef container fill:#fffde7,stroke:#fbc02d,stroke-width:2px,color:#000;
    classDef volume fill:#eceff1,stroke:#607d8b,stroke-width:2px,color:#000;
    
    class Browser browser;
    class API,Worker,gRPC,MySQL,Redis,Kafka container;
    class mysql_vol,redis_vol,kafka_vol volume;
```
