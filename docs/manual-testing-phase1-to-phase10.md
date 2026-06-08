# Hướng Dẫn Test Tay End-to-End — Phase 1 đến Phase 10

> **Repository:** AI-Assisted-Adaptive-English-Learning-System  
> **Branch:** `feature/huy-backend-adaptive`  
> **Latest Commit:** `b393f39 — feat(adaptive): complete remaining adaptive features`  
> **Ngôn ngữ:** Tiếng Việt  
> **Mục đích:** Hướng dẫn Huy tự test tay toàn bộ hệ thống trước khi bàn giao frontend/team  

---

## 🔗 Quick Links — Ấn Vào Là Nhảy Qua Luôn

> ⚡ **Yêu cầu:** Docker phải đang chạy (`docker compose up -d`) trước khi mở các link dưới đây.

### 🟢 API & Tools

| Link | Mô tả |
|---|---|
| [http://localhost:5292/swagger](http://localhost:5292/swagger) | **Swagger UI** — Test REST API tại đây |
| [http://localhost:5292/hangfire](http://localhost:5292/hangfire) | **Hangfire Dashboard** — Xem & trigger background jobs |
| [http://localhost:5292/health](http://localhost:5292/health) | **API Health Check** — Kiểm tra API có healthy không |
| [http://localhost:50080/health](http://localhost:50080/health) | **gRPC Health Check** — Kiểm tra gRPC service |

### 🗄️ Database & Cache

| Link | Mô tả |
|---|---|
| [http://localhost:33066](http://localhost:33066) | **MySQL** — Kết nối bằng MySQL Workbench/DBeaver (port 33066) |
| [http://localhost:6379](http://localhost:6379) | **Redis** — Kết nối bằng Redis Desktop Manager hoặc redis-cli |
| [http://localhost:9092](http://localhost:9092) | **Kafka** — Bootstrap server (dùng trong code, không mở browser) |

### 📋 Thông tin kết nối MySQL (dùng trong Workbench/DBeaver)

```
Host:     localhost
Port:     33066
Database: AdaptiveEnglishLearningDb
Username: root
Password: 12345
```

### 📋 Thông tin kết nối Redis (dùng trong Redis Desktop Manager)

```
Host: localhost
Port: 6379
(không cần password trong dev)
```

### 🧪 Test nhanh bằng browser (không cần Postman)

| Bước | Link | Ghi chú |
|---|---|---|
| 1. Kiểm tra API chạy | [http://localhost:5292/health](http://localhost:5292/health) | Phải thấy chữ `Healthy` |
| 2. Xem Swagger | [http://localhost:5292/swagger](http://localhost:5292/swagger) | Test Register/Login ở đây |
| 3. Xem Hangfire jobs | [http://localhost:5292/hangfire](http://localhost:5292/hangfire) | Click Trigger để chạy job |
| 4. Kiểm tra gRPC | [http://localhost:50080/health](http://localhost:50080/health) | Phải thấy `{"status":"Healthy"}` |

### 🚀 Cách test API nhanh bằng Swagger

1. Mở [http://localhost:5292/swagger](http://localhost:5292/swagger)
2. Tìm **POST /api/auth/register** → click → **Try it out** → điền thông tin → **Execute**
3. Tìm **POST /api/auth/login** → login → copy `accessToken` từ response
4. Click nút **Authorize** 🔒 ở góc trên phải Swagger → dán token vào ô `Bearer [token]`
5. Từ đó tất cả endpoint đã được authenticate, test thoải mái

### 📋 Bảng mẫu JSON Copy-Paste nhanh trên Swagger UI

Dưới đây là các payload JSON viết sẵn, bạn chỉ việc copy-paste thẳng vào Swagger UI để thực hiện các bài test:

#### 1. Đăng ký tài khoản (`POST /api/auth/register`)
```json
{
  "username": "testuser01",
  "email": "testuser01@mail.com",
  "password": "Test@1234",
  "fullName": "Nguyen Van A"
}
```

#### 2. Đăng nhập (`POST /api/auth/login`)
```json
{
  "email": "testuser01@mail.com",
  "password": "Test@1234"
}
```
*(Sau khi Execute, hãy copy chuỗi `accessToken` ở phần response, bấm nút **Authorize 🔒** ở góc trên bên phải Swagger, gõ `Bearer <chuỗi_token>` rồi bấm Authorize).*

#### 3. Nộp bài đánh giá năng lực đầu vào (`POST /api/placement/submit`)
```json
[
  {
    "questionId": 1,
    "selectedOptionIndex": 0
  },
  {
    "questionId": 2,
    "selectedOptionIndex": 1
  },
  {
    "questionId": 3,
    "selectedOptionIndex": 2
  }
]
```
*(Lưu ý: Các câu hỏi có thể thay đổi ID tùy theo dữ liệu database của bạn. Bạn hãy chạy `GET /api/placement/start` trước để lấy danh sách Question ID chính xác).*

#### 4. Nộp bài trắc nghiệm thông thường (`POST /api/quizzes/{id}/submit`)
```json
{
  "quizId": 1,
  "answers": [
    {
      "questionId": 4,
      "selectedAnswerOptionId": 13
    },
    {
      "questionId": 5,
      "selectedAnswerOptionId": 18
    }
  ]
}
```
*(Bạn có thể lấy Quiz ID và Question/Option ID tương ứng từ endpoint `GET /api/quizzes/{id}`)*

#### 5. Tạo mục tiêu học tập (`POST /api/goals`)
```json
{
  "learnerId": 1,
  "target": "TOEIC 750",
  "type": 0,
  "deadline": "2026-12-31T23:59:59.000Z"
}
```
*(Phần `type` là enum học tập: `0` = TOEIC, `1` = IELTS, `2` = VSTEP, `3` = General, `4` = LessonsPerWeek, `5` = QuizzesPerWeek)*

#### 6. Cập nhật tiến độ mục tiêu (`PUT /api/goals/{id}/progress`)
```json
{
  "goalId": 1,
  "progressPercentage": 100
}
```

#### 7. Gửi feedback bài học (`POST /api/feedback`)
```json
{
  "subject": "Phản hồi bài học",
  "content": "Bài học TOEIC Listening này rất bổ ích, phát âm rất chuẩn.",
  "rating": 5
}
```

---

## A. Chuẩn Bị Môi Trường

### Yêu cầu
| Công cụ | Phiên bản tối thiểu | Ghi chú |
|---|---|---|
| Docker Desktop | 4.x | Phải đang chạy |
| .NET 8 SDK | 8.0 | Chỉ cần nếu test local không dùng Docker |
| PowerShell | 5.1+ / 7+ | Để chạy smoke script |
| curl hoặc Postman | Bất kỳ | Để test REST API |
| MySQL client (tùy chọn) | Bất kỳ | Để chạy SQL query kiểm tra |

### Kiểm tra branch trước khi test

```powershell
git branch --show-current
# Phải trả về: feature/huy-backend-adaptive

git status --short
# Phải không có file staged lạ
```

### File .env local (KHÔNG COMMIT)
Nếu test local (không qua Docker), tạo file `.env` ở thư mục gốc:

```
Email__Provider=Smtp
Email__Host=smtp.gmail.com
Email__Port=587
Email__Username=your_email@gmail.com
Email__Password=your_app_password
Email__FromName=INTER-VIET
Email__FromAddress=your_email@gmail.com
```

> ⚠️ **KHÔNG commit file `.env`** — đã có trong `.gitignore`

---

## B. Chạy Toàn Hệ Thống Docker

### Khởi động từ đầu (clean)

```powershell
# Dừng và xóa volume cũ
docker compose down -v

# Build lại tất cả image
docker compose build --no-cache

# Khởi động ở chế độ nền
docker compose up -d

# Kiểm tra trạng thái
docker compose ps
```

### Kỳ vọng — tất cả container phải `healthy`

```
NAME                             STATUS
adaptive-learning-mysql          Up (healthy)   port 33066
adaptive-learning-redis          Up (healthy)   port 6379
adaptive-learning-kafka          Up (healthy)   port 9092
adaptive-learning-grpc-service   Up (healthy)   port 50051, 50080
adaptive-learning-api            Up (healthy)   port 5292
adaptive-learning-worker         Up (healthy)
```

> ✅ **PASS:** Tất cả 6 container đều `healthy`  
> ❌ **FAIL:** Bất kỳ container nào `unhealthy` hoặc `Exiting`  
> 🔍 **Debug:** `docker compose logs [tên-container]`

---

## C. Health Check Tổng Quát

### API Health
```powershell
curl http://localhost:5292/health
# Kỳ vọng: Healthy
```

### gRPC Health
```powershell
curl http://localhost:50080/health
# Kỳ vọng: {"status":"Healthy"}
```

### Worker Health
```powershell
docker compose exec worker cat /tmp/adaptive-worker-health.txt
# Kỳ vọng: overall=Healthy
```

### Redis Ping
```powershell
docker compose exec redis redis-cli ping
# Kỳ vọng: PONG
```

### Kafka Topics
```powershell
docker compose exec kafka kafka-topics.sh --list --bootstrap-server localhost:9092
# Kỳ vọng: thấy các topic quiz-submitted, lesson-completed, v.v.
```

### MySQL kết nối
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "SHOW TABLES;"
# Kỳ vọng: danh sách bảng (Users, LearnerProfiles, Quizzes, ...)
```

---

## D. Test Phase 1 — Technical Skeleton

**Mục tiêu:** Xác nhận toàn bộ skeleton dự án hoạt động.

### D1. Kiểm tra solution build
```powershell
dotnet build
# Kỳ vọng: Build succeeded. 0 Error(s)
```

### D2. Kiểm tra các project tồn tại
```powershell
dir src/
# Kỳ vọng:
# AdaptiveLearning.Contracts/
# AdaptiveLearning.GrpcService/
# AdaptiveLearning.Worker/
# AdaptiveLearning.Tests/
# CoreLearningSystem.API/
# CoreLearningSystem.Domain/
# CoreLearningSystem.Application/
# CoreLearningSystem.Infrastructure/
```

### D3. Kiểm tra Docker có đủ service
```powershell
docker compose config --services
# Kỳ vọng: mysql, redis, kafka, grpc-service, api, worker
```

### D4. Chạy unit tests
```powershell
dotnet test
# Kỳ vọng: Passed! Failed: 0, Passed: 151, Total: 151
```

**✅ PASS Phase 1:** Build OK + 6 services chạy + 151 tests pass  

---

## E. Test Phase 2 — Kafka Event Processing

**Mục tiêu:** Xác nhận Kafka nhận và xử lý events đúng.

### E1. Kiểm tra Kafka topics tồn tại
```powershell
docker compose exec kafka kafka-topics.sh --list --bootstrap-server localhost:9092
```
Kỳ vọng thấy các topic:
- `quiz-submitted`
- `lesson-completed`
- `feedback-submitted`
- `placement-test-completed`
- `goal-completed`
- `badge-awarded`
- `notification-created`
- `adaptive-events-dlq` (Dead Letter Queue)

### E2. Register và Login để lấy JWT
```powershell
# Register
$body = '{"username":"testuser01","email":"testuser01@mail.com","password":"Test@1234"}'
$reg = Invoke-RestMethod -Method Post -Uri "http://localhost:5292/api/auth/register" `
       -Body $body -ContentType "application/json"
Write-Host "Register OK"

# Login
$login = Invoke-RestMethod -Method Post -Uri "http://localhost:5292/api/auth/login" `
         -Body '{"email":"testuser01@mail.com","password":"Test@1234"}' `
         -ContentType "application/json"
$token = $login.data.accessToken
Write-Host "Token: $($token.Substring(0,30))..."
```

### E3. Submit Quiz → tạo Kafka event
```powershell
# Lấy danh sách quiz
$quizList = Invoke-RestMethod -Uri "http://localhost:5292/api/quizzes" `
            -Headers @{Authorization="Bearer $token"}
$quizId = $quizList.data[0].id

# Submit quiz (giả sử quiz có questionId)
$submitBody = @{
    answers = @(@{questionId=1; selectedAnswerOptionId=1})
} | ConvertTo-Json -Depth 3

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/quizzes/$quizId/submit" `
  -Headers @{Authorization="Bearer $token"} `
  -Body $submitBody -ContentType "application/json"
```

### E4. Xác nhận Kafka consumer đã xử lý
```powershell
# Xem log Worker
docker compose logs worker --since 2m | Select-String "quiz-submitted|QuizSubmitted|Consumed"
# Kỳ vọng: thấy log "Consumed event" hoặc "Processing QuizSubmittedEvent"
```

### E5. Kiểm tra Retry & DLQ
```powershell
# Xem BackgroundJobExecutions trong DB
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT JobName, Status, ProcessedCount, FailedCount, CreatedAt
FROM BackgroundJobExecutions
ORDER BY Id DESC LIMIT 10;"
```

**✅ PASS Phase 2:** Topic tồn tại + Worker log thấy consumed + DB có execution records  

---

## F. Test Phase 3 — gRPC Weakness Analysis

**Mục tiêu:** gRPC service nhận request, phân tích điểm yếu đúng.

### F1. Kiểm tra gRPC health
```powershell
curl http://localhost:50080/health
# Kỳ vọng: {"status":"Healthy"}
```

### F2. Xem log Worker gọi gRPC sau khi submit quiz
```powershell
docker compose logs worker --since 3m | Select-String "grpc|gRPC|weakness|Weakness|50051"
# Kỳ vọng: log "Calling gRPC AnalyzeQuizSubmission" hoặc "Weakness analyzed"
```

### F3. Xem LearnerWeaknessHistories sau submit quiz
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT lp.Id, lwh.Skill, lwh.Topic, lwh.Status, lwh.LastOccurredAt
FROM LearnerWeaknessHistories lwh
JOIN LearnerProfiles lp ON lp.Id = lwh.LearnerProfileId
ORDER BY lwh.Id DESC LIMIT 10;"
```
Kỳ vọng: có record với Skill và Topic tương ứng với quiz đã submit.

**✅ PASS Phase 3:** gRPC healthy + Worker log thấy gRPC call + DB có weakness records  

---

## G. Test Phase 4 — Skill Matrix & Weakness History

**Mục tiêu:** SkillMatrix được tạo và cập nhật đúng theo kết quả quiz/placement.

### G1. Làm Placement Test
```powershell
$ptBody = @{
    answers = @(@{questionId=1; selectedAnswerOptionId=1})
} | ConvertTo-Json -Depth 3

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/placement/submit" `
  -Headers @{Authorization="Bearer $token"} `
  -Body $ptBody -ContentType "application/json"
```

### G2. Kiểm tra SkillMatrices
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT sm.LearnerProfileId, sm.Skill, sm.CurrentScore, sm.MasteryLevel, sm.LastUpdatedAt
FROM SkillMatrices sm
ORDER BY sm.Id DESC LIMIT 10;"
```

### G3. Kiểm tra SkillMatrixHistories
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT smh.LearnerProfileId, smh.Skill, smh.PreviousScore, smh.NewScore,
       smh.SourceType, smh.RecordedAt
FROM SkillMatrixHistories smh
ORDER BY smh.Id DESC LIMIT 10;"
```

### G4. Test Idempotency — Submit cùng event hai lần
```powershell
# Submit quiz lần 2 với cùng data
# Kiểm tra SkillMatrixHistories không tăng thêm record trùng EventId
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT EventId, COUNT(*) as cnt
FROM SkillMatrixHistories
GROUP BY EventId HAVING cnt > 1;"
# Kỳ vọng: không có row nào (không duplicate)
```

**✅ PASS Phase 4:** SkillMatrix có record + History tăng + Idempotency không duplicate  

---

## H. Test Phase 5 — Adaptive Recommendation

**Mục tiêu:** Hệ thống tự động gợi ý bài học phù hợp sau quiz/placement.

### H1. Xem Recommendations sau submit quiz
```powershell
$recs = Invoke-RestMethod -Uri "http://localhost:5292/api/recommendations" `
        -Headers @{Authorization="Bearer $token"}
$recs.data | Format-Table id, lessonId, skill, topic, priorityScore, status
```

### H2. Kiểm tra SQL
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT r.Id, r.LearnerProfileId, r.LessonId, r.Skill, r.Topic,
       r.PriorityScore, r.Status, r.Reason
FROM Recommendations r
ORDER BY r.Id DESC LIMIT 10;"
```

### H3. Kiểm tra không gợi ý bài đã hoàn thành
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
-- Xác nhận không có completed recommendation trong Active recs
SELECT r.Id, r.Status, r.LessonId
FROM Recommendations r
WHERE r.Status = 'Active'
  AND r.LessonId IN (
    SELECT lp2.LessonId FROM LearnerProgress lp2
    WHERE lp2.LearnerProfileId = r.LearnerProfileId AND lp2.IsCompleted = 1
  );"
# Kỳ vọng: không có row nào
```

### H4. Complete một lesson → Recommendation chuyển Completed
```powershell
$lessonId = $recs.data[0].lessonId
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/lessons/$lessonId/complete" `
  -Headers @{Authorization="Bearer $token"}

# Kiểm tra sau đó
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT r.Id, r.LessonId, r.Status, r.CompletedAt
FROM Recommendations r WHERE r.LessonId = $lessonId ORDER BY r.Id DESC LIMIT 5;"
```

**✅ PASS Phase 5:** Có recommendation sau quiz + PriorityScore > 0 + Reason có text + Completed sau complete lesson  

---

## I. Test Phase 6 — Goal Tracking & Achievement

**Mục tiêu:** Goal tracking và badge award hoạt động đúng.

### I1. Xem Goals hiện có
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT gs.Id, gs.LearnerProfileId, gs.Type, gs.Target, gs.TargetValue,
       gs.CurrentValue, gs.Status, gs.Deadline
FROM GoalSettings gs ORDER BY gs.Id DESC LIMIT 10;"
```

### I2. Kiểm tra GoalProgressHistories
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT gph.GoalId, gph.PreviousValue, gph.AddedValue, gph.NewValue,
       gph.Reason, gph.RecordedAt
FROM GoalProgressHistories gph ORDER BY gph.Id DESC LIMIT 10;"
```

### I3. Xem Badges đã được award
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT lb.Id, lb.LearnerProfileId, ab.Name, ab.Description,
       ab.TriggerType, lb.EarnedAt
FROM LearnerBadges lb
JOIN AchievementBadges ab ON ab.Id = lb.BadgeId
ORDER BY lb.Id DESC LIMIT 10;"
```

### I4. Kafka events Goal/Badge
```powershell
docker compose logs worker --since 5m | Select-String "GoalCompleted|BadgeAwarded|goal-completed|badge-awarded"
# Kỳ vọng: thấy log publish event
```

**✅ PASS Phase 6:** GoalProgress tăng đúng + LearnerBadges có record + Events được publish  

---

## J. Test Phase 7 — Background Jobs & Notification

**Mục tiêu:** Các Hangfire jobs chạy đúng, notification được tạo.

### J1. Kiểm tra Hangfire Dashboard
```
Mở trình duyệt: http://localhost:5292/hangfire
```
Kỳ vọng: thấy giao diện Hangfire với danh sách Recurring Jobs.

Các job phải có trong Recurring Jobs:
- `outbox-publisher`
- `user-session-cleanup`
- `skill-matrix-recalculation`
- `recommendation-effectiveness`
- `recommendation-regeneration`
- `recommendation-statistics`
- `skill-decay`
- `weekly-learning-report`
- `goal-status-tracking`
- `achievement-checking`

### J2. Trigger job thủ công qua Hangfire UI
1. Vào `http://localhost:5292/hangfire`
2. Click tab **Recurring Jobs**
3. Click **Trigger** bên cạnh `weekly-learning-report`
4. Xem tab **Succeeded** để confirm job chạy xong

### J3. Trigger job qua PowerShell (nếu có endpoint)
```powershell
# Trigger outbox publisher
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/outbox-publisher" `
  -Headers @{Authorization="Bearer $token"}
```

### J4. Kiểm tra BackgroundJobExecutions
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT JobName, Status, ProcessedCount, SuccessCount, FailedCount,
       DurationMilliseconds, StartedAt
FROM BackgroundJobExecutions
ORDER BY Id DESC LIMIT 20;"
```

### J5. Kiểm tra Notifications
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT n.Id, n.UserId, n.Type, n.Title, n.IsRead, n.CreatedAt
FROM Notifications n ORDER BY n.Id DESC LIMIT 10;"
```

### J6. Kiểm tra NotificationDeliveryAttempts
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT nda.Id, nda.NotificationId, nda.Channel, nda.Status,
       nda.ErrorMessage, nda.AttemptedAt
FROM NotificationDeliveryAttempts nda ORDER BY nda.Id DESC LIMIT 10;"
```

**✅ PASS Phase 7:** Hangfire UI có đủ recurring jobs + BackgroundJobExecutions có record Status=Succeeded + Notifications tạo được  

---

## K. Test Phase 8 — Feedback Analysis & Redis Cache

**Mục tiêu:** Feedback được phân tích đúng, Redis cache hoạt động.

### K1. Submit Feedback Lesson
```powershell
$fbBody = @{
    targetId   = 1          # lessonId
    targetType = "Lesson"
    Rating     = 4
    Comment    = "Bài học rất hay và dễ hiểu!"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/feedback" `
  -Headers @{Authorization="Bearer $token"} `
  -Body $fbBody -ContentType "application/json"
```

### K2. Submit Feedback Quiz
```powershell
$fbBody2 = @{
    targetId   = 1          # quizId
    targetType = "Quiz"
    Rating     = 2
    Comment    = "Quiz quá khó!"
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/feedback" `
  -Headers @{Authorization="Bearer $token"} `
  -Body $fbBody2 -ContentType "application/json"
```

### K3. Kiểm tra FeedbackAnalyses
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT fa.TargetId, fa.TargetType, fa.AverageRating,
       fa.TotalFeedbackCount, fa.PositiveFeedbackCount,
       fa.NegativeFeedbackCount, fa.LastAnalyzedAt
FROM FeedbackAnalyses fa ORDER BY fa.Id DESC LIMIT 10;"
```

### K4. Kiểm tra Alert Admin khi rating thấp
```powershell
docker compose logs worker --since 2m | Select-String "alert|Alert|low rating|LowRating"
# Kỳ vọng: thấy log cảnh báo nếu rating <= 2
```

### K5. Redis Cache keys

> ⚠️ Lệnh `KEYS` chỉ dùng trong **dev/manual test**, không dùng trong production.

```powershell
docker compose exec redis redis-cli keys "adaptive:v1:*"
# Kỳ vọng: thấy các key như:
# adaptive:v1:lesson:1
# adaptive:v1:recommendation:10
# adaptive:v1:skill-matrix:10
# adaptive:v1:progress:10
```

### K6. Xem một cache value
```powershell
docker compose exec redis redis-cli get "adaptive:v1:lesson:1"
# Kỳ vọng: JSON string của lesson
```

### K7. Test Redis Fallback (Optional)
```powershell
# Dừng Redis
docker compose stop redis

# Gọi API — phải vẫn trả về kết quả (từ DB)
Invoke-RestMethod -Uri "http://localhost:5292/api/lessons/1" `
  -Headers @{Authorization="Bearer $token"}
# Kỳ vọng: vẫn trả về data (không crash)

# Khởi động lại Redis
docker compose start redis
Start-Sleep -Seconds 5
```

**✅ PASS Phase 8:** FeedbackAnalysis có AverageRating đúng + Redis có cache keys + API vẫn chạy khi Redis tắt  

---

## L. Test Phase 9 — Docker Full System Deployment

**Mục tiêu:** Toàn bộ hệ thống chạy được qua Docker Compose.

### L1. Validate docker compose config
```powershell
docker compose config
# Kỳ vọng: in ra full config không có lỗi
# Warning về `version` obsolete là bình thường, không phải lỗi
```

### L2. Chạy smoke test script
```powershell
powershell -ExecutionPolicy Bypass -File scripts/docker-smoke-test.ps1
# Kỳ vọng:
# PHASE 1: Infrastructure Health — PASS
# PHASE 2: Authentication — PASS
# PHASE 3: Core Learning Endpoints — PASS
# PHASE 4: Adaptive Features — PASS
# PHASE 5: Background Jobs — PASS
# PHASE 6: gRPC Direct — PASS
# All smoke tests PASSED!
```

### L3. Test dữ liệu còn sau restart container
```powershell
# Restart API container
docker compose restart api
Start-Sleep -Seconds 15

# Gọi lại API
Invoke-RestMethod -Uri "http://localhost:5292/api/lessons" `
  -Headers @{Authorization="Bearer $token"}
# Kỳ vọng: vẫn trả về dữ liệu (DB volume persistent)
```

**✅ PASS Phase 9:** docker compose config valid + smoke test 22/22 pass + dữ liệu còn sau restart  

---

## M. Test Phase 10 — Remaining Adaptive Features

### M1. Certificate Goal Verification

**Setup:** Cần có GoalSetting với Type=TOEIC đang Active.

```powershell
# Kiểm tra goal TOEIC đang active
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT gs.Id, gs.LearnerProfileId, gs.Type, gs.TargetValue,
       gs.CurrentValue, gs.Status
FROM GoalSettings gs WHERE gs.Type='TOEIC' AND gs.Status='Active' LIMIT 5;"

# Nếu chưa có, insert test data (chỉ dùng khi test tay):
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
INSERT INTO GoalSettings (LearnerProfileId, Target, Type, TargetValue, CurrentValue,
  Status, StartDate, Deadline, CreatedAt)
SELECT Id, 'Pass TOEIC 700', 'TOEIC', 700, 0, 'Active',
  NOW() - INTERVAL 1 DAY, NOW() + INTERVAL 30 DAY, NOW()
FROM LearnerProfiles LIMIT 1;"
```

```powershell
# Submit CertificateTestResult với score >= target
$certBody = @{
    certificateType     = "TOEIC"
    score               = 750
    targetScore         = 700
    sourceQuizAttemptId = 1
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/certificates/record" `
  -Headers @{Authorization="Bearer $token"} `
  -Body $certBody -ContentType "application/json"
```

**Kiểm tra kết quả:**
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT gs.Id, gs.Type, gs.Status, gs.CurrentValue, gs.IsCompleted
FROM GoalSettings gs WHERE gs.Type='TOEIC' ORDER BY gs.Id DESC LIMIT 3;

SELECT gph.GoalId, gph.PreviousValue, gph.AddedValue, gph.NewValue
FROM GoalProgressHistories gph ORDER BY gph.Id DESC LIMIT 3;

SELECT om.EventType, om.Status, om.CreatedAt
FROM OutboxMessages om WHERE om.EventType LIKE '%GoalCompleted%' ORDER BY om.Id DESC LIMIT 3;"
```

**✅ PASS M1:** Goal.Status = Completed + GoalProgressHistory có record + OutboxMessage có GoalCompletedEvent

---

### M2. Full Skill Matrix Recalculation

```powershell
# Trigger job qua Hangfire UI: http://localhost:5292/hangfire
# Hoặc trigger endpoint nếu có:
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/skill-matrix-recalculation" `
  -Headers @{Authorization="Bearer $token"}
```

**Kiểm tra kết quả:**
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT smh.LearnerProfileId, smh.Skill, smh.PreviousScore, smh.NewScore,
       smh.SourceType, smh.DecayPeriodKey, smh.RecordedAt
FROM SkillMatrixHistories smh
WHERE smh.SourceType = 'PeriodicRecalculation'
ORDER BY smh.Id DESC LIMIT 10;"
```

**Test idempotency — trigger 2 lần cùng period:**
```powershell
# Trigger lần 2
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/skill-matrix-recalculation" `
  -Headers @{Authorization="Bearer $token"}

# Kiểm tra không có duplicate DecayPeriodKey
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT DecayPeriodKey, COUNT(*) cnt
FROM SkillMatrixHistories
WHERE SourceType='PeriodicRecalculation'
GROUP BY LearnerProfileId, Skill, DecayPeriodKey HAVING cnt > 1;"
# Kỳ vọng: không có row nào
```

**✅ PASS M2:** SkillMatrixHistory có SourceType=PeriodicRecalculation + Không duplicate trong cùng period

---

### M3. Session Cleanup

```powershell
# Login để tạo UserSession
$loginRes = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/auth/login" `
  -Body '{"email":"testuser01@mail.com","password":"Test@1234"}' `
  -ContentType "application/json"
$jwtId = $loginRes.data.jwtId  # hoặc decode JWT để lấy jti claim

# Kiểm tra UserSession được tạo
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT us.Id, us.UserId, us.JwtId, us.Status, us.ExpiresAt, us.CreatedAt
FROM UserSessions us ORDER BY us.Id DESC LIMIT 5;"
```

```powershell
# Insert session đã hết hạn để test cleanup (chỉ dùng khi test tay):
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
INSERT INTO UserSessions (UserId, JwtId, SessionTokenHash, Status, ExpiresAt, CreatedAt)
SELECT u.Id, UUID(), 'test-hash', 'Active',
  NOW() - INTERVAL 10 MINUTE, NOW() - INTERVAL 2 HOUR
FROM Users u LIMIT 1;"

# Trigger cleanup job
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/user-session-cleanup" `
  -Headers @{Authorization="Bearer $token"}

# Kiểm tra session hết hạn đã chuyển Expired
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT us.JwtId, us.Status, us.ExpiresAt
FROM UserSessions us WHERE us.Status = 'Expired' ORDER BY us.Id DESC LIMIT 5;"
```

**✅ PASS M3:** Session hết hạn chuyển Status=Expired + Session còn hạn vẫn Active

---

### M4. Token Revocation

```powershell
# Bước 1: Login lấy token
$loginRes = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/auth/login" `
  -Body '{"email":"testuser01@mail.com","password":"Test@1234"}' `
  -ContentType "application/json"
$tokenToRevoke = $loginRes.data.accessToken

# Bước 2: Xác nhận token hoạt động
Invoke-RestMethod -Uri "http://localhost:5292/api/profile" `
  -Headers @{Authorization="Bearer $tokenToRevoke"}
# Kỳ vọng: 200 OK

# Bước 3: Logout (revoke token)
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/auth/logout" `
  -Headers @{Authorization="Bearer $tokenToRevoke"}

# Bước 4: Gọi lại endpoint với token cũ
try {
    Invoke-RestMethod -Uri "http://localhost:5292/api/profile" `
      -Headers @{Authorization="Bearer $tokenToRevoke"}
    Write-Host "FAIL: Token vẫn hợp lệ sau revoke!"
} catch {
    Write-Host "PASS: Token đã bị reject ($($_.Exception.Response.StatusCode))"
    # Kỳ vọng: 401 Unauthorized
}

# Bước 5: Kiểm tra Redis có revoke key
docker compose exec redis redis-cli keys "adaptive:v1:token-revoked:*"
# Kỳ vọng: thấy key của JTI vừa revoke
```

**✅ PASS M4:** Token cũ bị reject 401 + Redis có token-revoked key + UserSession Status=Revoked

---

### M5. Direct gRPC GenerateRecommendations

```powershell
# Qua REST API (gRPC được gọi bên trong)
$recReq = @{
    learnerProfileId  = 1
    weakestSkill      = "Grammar"
    weakTopics        = @("Verbs", "Tenses")
    currentLevel      = "B1"
    maxRecommendations = 5
} | ConvertTo-Json

$recRes = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/recommendations/generate" `
  -Headers @{Authorization="Bearer $token"} `
  -Body $recReq -ContentType "application/json"

$recRes.data | Format-Table lessonId, title, priorityScore, reason
```

**Kiểm tra kết quả:**
- `lessonId` > 0
- `priorityScore` > 0
- `reason` có text mô tả
- Không có lesson đã Completed trong danh sách

```powershell
# Xem log gRPC service
docker compose logs grpc-service --since 2m | Select-String "GenerateRecommendations|Recommendation"
```

**✅ PASS M5:** Response có danh sách lessons với priorityScore và reason

---

### M6. Recommendation Effectiveness

```powershell
# Bước 1: Complete một recommended lesson
$recs = Invoke-RestMethod -Uri "http://localhost:5292/api/recommendations" `
        -Headers @{Authorization="Bearer $token"}
$lessonId = $recs.data[0].lessonId

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/lessons/$lessonId/complete" `
  -Headers @{Authorization="Bearer $token"}

# Bước 2: Submit quiz để tạo SkillMatrixHistory mới
# (làm tương tự E3)

# Bước 3: Trigger effectiveness job
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/recommendation-effectiveness" `
  -Headers @{Authorization="Bearer $token"}

# Bước 4: Kiểm tra RecommendationEffectivenesses
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT re.RecommendationId, re.LessonId, re.Skill, re.Topic,
       re.ScoreBefore, re.ScoreAfter, re.Improvement, re.WasEffective,
       re.EvaluatedAt
FROM RecommendationEffectivenesses re ORDER BY re.Id DESC LIMIT 10;"
```

**✅ PASS M6:** RecommendationEffectiveness có record với ScoreBefore, ScoreAfter, WasEffective đúng

---

### M7. Recommendation Regeneration

```powershell
# Prerequisite: Cần có RecommendationEffectiveness với WasEffective=0
# Và LearnerWeaknessHistory tương ứng vẫn Active

# Trigger regeneration job
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/recommendation-regeneration" `
  -Headers @{Authorization="Bearer $token"}

# Kiểm tra RecommendationHistories có Replaced action
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT rh.RecommendationId, rh.Action, rh.PreviousStatus, rh.NewStatus,
       rh.Reason, rh.RecordedAt
FROM RecommendationHistories rh
WHERE rh.Action = 'Replaced'
ORDER BY rh.Id DESC LIMIT 5;"

# Kiểm tra Recommendation mới được tạo
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT r.Id, r.LearnerProfileId, r.Skill, r.Topic, r.Status,
       r.SourceEventId, r.GeneratedAt
FROM Recommendations r
WHERE r.SourceEventId LIKE 'regen_%'
ORDER BY r.Id DESC LIMIT 5;"
```

**✅ PASS M7:** RecommendationHistory có Action=Replaced + Recommendation mới với SourceEventId bắt đầu bằng `regen_`

---

### M8. Recommendation Statistics

```powershell
# Trigger statistics job
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/recommendation-statistics" `
  -Headers @{Authorization="Bearer $token"}

# Kiểm tra RecommendationStatisticSnapshots
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT rss.LessonId, rss.Skill, rss.Topic,
       rss.RecommendationCount, rss.CompletionCount, rss.EffectiveCount,
       rss.EffectivenessRate, rss.AverageImprovement, rss.GeneratedAt
FROM RecommendationStatisticSnapshots rss
ORDER BY rss.EffectivenessRate DESC, rss.AverageImprovement DESC LIMIT 10;"
```

**✅ PASS M8:** RecommendationStatisticSnapshots có data với EffectivenessRate và AverageImprovement hợp lệ

---

### M9. Outbox Pattern

```powershell
# Bước 1: Thực hiện action tạo event (complete lesson)
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/lessons/1/complete" `
  -Headers @{Authorization="Bearer $token"}

# Bước 2: Kiểm tra OutboxMessages ở trạng thái Pending
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT om.Id, om.EventType, om.AggregateType, om.Status,
       om.RetryCount, om.CreatedAt, om.ProcessedAt
FROM OutboxMessages om ORDER BY om.Id DESC LIMIT 10;"
```

```powershell
# Bước 3: Trigger OutboxPublisherJob
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/jobs/trigger/outbox-publisher" `
  -Headers @{Authorization="Bearer $token"}

# Bước 4: Kiểm tra Status chuyển Published
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "
SELECT om.Id, om.EventType, om.Status, om.ProcessedAt, om.RetryCount
FROM OutboxMessages om WHERE om.Status = 'Published' ORDER BY om.Id DESC LIMIT 10;"
# Kỳ vọng: Status = Published, ProcessedAt không NULL
```

```powershell
# Bước 5: Kiểm tra Kafka nhận được message
docker compose exec kafka kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 \
  --topic lesson-completed --from-beginning --max-messages 5
# Kỳ vọng: thấy JSON message của LessonCompletedEvent
```

**✅ PASS M9:** OutboxMessage Status=Published + ProcessedAt không NULL + Kafka nhận được message

---

## N. Test Tương Thích Frontend của Hoàng

**Mục tiêu:** Xác nhận các endpoint cũ không bị phá vỡ.

### N1. Danh sách endpoints cần kiểm tra

| Endpoint | Method | Mục đích |
|---|---|---|
| `/api/auth/register` | POST | Đăng ký |
| `/api/auth/login` | POST | Đăng nhập |
| `/api/auth/logout` | POST | Đăng xuất |
| `/api/auth/refresh` | POST | Refresh token |
| `/api/lessons` | GET | Danh sách bài học |
| `/api/lessons/{id}` | GET | Chi tiết bài học |
| `/api/lessons/{id}/complete` | POST | Hoàn thành bài |
| `/api/quizzes` | GET | Danh sách quiz |
| `/api/quizzes/{id}` | GET | Chi tiết quiz |
| `/api/quizzes/{id}/submit` | POST | Submit quiz |
| `/api/placement/submit` | POST | Placement test |
| `/api/recommendations` | GET | Danh sách recommendation |
| `/api/feedback` | POST | Submit feedback |
| `/health` | GET | Health check |

### N2. Test nhanh các endpoint cơ bản

```powershell
# Login
$res = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5292/api/auth/login" `
  -Body '{"email":"testuser01@mail.com","password":"Test@1234"}' `
  -ContentType "application/json"
$t = $res.data.accessToken

# Lesson list
$lessons = Invoke-RestMethod -Uri "http://localhost:5292/api/lessons" `
           -Headers @{Authorization="Bearer $t"}
Write-Host "Lessons count: $($lessons.data.Count)"

# Quiz list
$quizzes = Invoke-RestMethod -Uri "http://localhost:5292/api/quizzes" `
           -Headers @{Authorization="Bearer $t"}
Write-Host "Quizzes count: $($quizzes.data.Count)"

# Profile
$profile = Invoke-RestMethod -Uri "http://localhost:5292/api/profile" `
           -Headers @{Authorization="Bearer $t"}
Write-Host "Profile: $($profile.data.level)"
```

### N3. Kiểm tra CORS không bị phá
```powershell
# Gửi request với Origin header của frontend cũ
$headers = @{
    Authorization = "Bearer $token"
    Origin        = "http://localhost:3000"  # hoặc port frontend của Hoàng
}
$res = Invoke-RestMethod -Uri "http://localhost:5292/api/lessons" -Headers $headers
# Kỳ vọng: không bị reject do CORS
```

### N4. Xác nhận response format không thay đổi
Kiểm tra response vẫn có structure:
```json
{
  "success": true,
  "data": { ... },
  "message": "..."
}
```

**✅ PASS N:** Tất cả endpoint cũ trả về 200 + Response format không đổi + CORS không bị phá

---

## O. Final PASS/FAIL Checklist

Điền kết quả vào bảng sau sau khi test xong:

| Phase | Module | Test | Expected | PASS/FAIL | Note |
|---|---|---|---|---|---|
| 1 | Skeleton | `dotnet build` | 0 errors | | |
| 1 | Skeleton | `dotnet test` | 151/151 | | |
| 1 | Skeleton | 6 containers healthy | All healthy | | |
| 2 | Kafka | Topics tồn tại | 8 topics | | |
| 2 | Kafka | Submit quiz → event | Worker log consumed | | |
| 2 | Kafka | DLQ tồn tại | `adaptive-events-dlq` | | |
| 3 | gRPC | gRPC health | `{"status":"Healthy"}` | | |
| 3 | gRPC | Weakness analyzed sau quiz | DB có records | | |
| 4 | SkillMatrix | Placement → SkillMatrix | Records tạo | | |
| 4 | SkillMatrix | Idempotency | Không duplicate | | |
| 5 | Recommendation | Sau quiz có recs | PriorityScore > 0 | | |
| 5 | Recommendation | Complete lesson → Completed | Status = Completed | | |
| 5 | Recommendation | Không gợi ý bài đã làm | Query empty | | |
| 6 | Goal/Badge | Goal progress tăng | GoalProgressHistory++ | | |
| 6 | Goal/Badge | Badge được award | LearnerBadges có record | | |
| 7 | Hangfire | UI accessible | 200 OK | | |
| 7 | Hangfire | 10 recurring jobs | Tất cả đăng ký | | |
| 7 | Notification | Notification tạo | Notifications table | | |
| 8 | Feedback | Submit feedback | FeedbackAnalysis cập nhật | | |
| 8 | Redis | Cache keys tồn tại | `adaptive:v1:*` keys | | |
| 8 | Redis | Fallback khi Redis tắt | API vẫn trả về | | |
| 9 | Docker | smoke test | 22/22 PASS | | |
| 9 | Docker | Dữ liệu còn sau restart | DB persistent | | |
| 10.1 | Certificate | Goal complete khi cert pass | Status=Completed | | |
| 10.1 | Certificate | GoalProgressHistory | PreviousValue/NewValue đúng | | |
| 10.1 | Certificate | OutboxMessage GoalCompleted | Status=Pending→Published | | |
| 10.2 | SkillRecalc | PeriodicRecalculation job | SourceType=PeriodicRecalculation | | |
| 10.2 | SkillRecalc | Idempotency period | Không duplicate key | | |
| 10.3 | Session | Login tạo UserSession | Status=Active | | |
| 10.3 | Session | Cleanup expired | Status=Expired | | |
| 10.4 | TokenRevoke | Token cũ bị reject | 401 Unauthorized | | |
| 10.4 | TokenRevoke | Redis có revoke key | `token-revoked:*` key | | |
| 10.5 | gRPC Recs | GenerateRecommendations | Lessons với score/reason | | |
| 10.6 | Effectiveness | WasEffective đúng | Re: ScoreBefore vs After | | |
| 10.7 | Regeneration | Replaced history | RecommendationHistory Action=Replaced | | |
| 10.7 | Regeneration | Recs mới được tạo | SourceEventId~=`regen_` | | |
| 10.8 | Statistics | Snapshot tạo | EffectivenessRate hợp lệ | | |
| 10.9 | Outbox | Pending → Published | ProcessedAt không NULL | | |
| 10.9 | Outbox | Kafka nhận message | kafka consumer thấy event | | |
| N | Compat | `/api/lessons` 200 OK | Response format không đổi | | |
| N | Compat | CORS hoạt động | Không reject frontend | | |
| N | Compat | JWT vẫn hợp lệ | Bearer token hoạt động | | |

---

## P. Các Lệnh Hữu Ích Nhanh

### Xem logs tất cả container
```powershell
docker compose logs --follow
```

### Xem logs một container cụ thể
```powershell
docker compose logs api --since 5m --follow
docker compose logs worker --since 5m --follow
docker compose logs grpc-service --since 5m --follow
```

### Restart một service
```powershell
docker compose restart api
docker compose restart worker
```

### Xóa sạch và chạy lại từ đầu
```powershell
docker compose down -v
docker compose build --no-cache
docker compose up -d
```

### Kiểm tra tất cả bảng trong DB
```powershell
docker compose exec mysql mysql -u root -p12345 AdaptiveEnglishLearningDb -e "SHOW TABLES;"
```

### Xem Hangfire jobs
```
Trình duyệt: http://localhost:5292/hangfire
```

---

*Tài liệu tạo ngày 2026-06-08 | Branch: feature/huy-backend-adaptive | Commit: b393f39*
