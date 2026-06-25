# Frontend Integration Guide: Huy's Adaptive Learning System

Tài liệu này hướng dẫn Frontend Team tích hợp các tính năng thuộc hệ thống Học tập Thích ứng (Adaptive Learning) của Huy vào giao diện người dùng.

> **⚠️ Lưu ý quan trọng:** Frontend **chỉ gọi REST API** tại `http://localhost:5292`. Không gọi trực tiếp Kafka, Redis, MySQL, Worker, gRPC Service hay Hangfire.  
> Frontend **không cần làm UI riêng** cho các tính năng backend nội bộ như Outbox, DLQ, Redis internals, Hangfire jobs, Session cleanup, Recommendation analytics nâng cao.

---

## 1. Thông tin chung cho Frontend

*   **Base URL Local:** `http://localhost:5292`
*   **Swagger UI (OpenAPI):** `http://localhost:5292/swagger`
*   **Khởi chạy hệ thống:** `docker compose up -d`
*   **Cơ chế xác thực:**
    ```http
    Authorization: Bearer <accessToken>
    ```
    Tái sử dụng luồng login/JWT hiện tại của phần Core Learning System (Hoàng).

*   **Dịch vụ nội bộ – Frontend KHÔNG gọi trực tiếp:**
    *   Kafka, Redis, MySQL, Background Worker, gRPC service chạy ngầm phía backend.

---

## 2. Luồng nghiệp vụ tổng quan

1.  **Đăng nhập & Khởi tạo:** User đăng nhập lấy JWT (Auth của Hoàng), hệ thống tự động khởi tạo Learner Profile.
2.  **Xem Dashboard:** Frontend gọi song song các API nhỏ để dựng Adaptive Dashboard (Skill Matrix, Learning Path, Goals, Notifications).
3.  **Học bài & Kiểm tra:** Khi hoàn thành bài học hoặc nộp bài Quiz, backend tự xử lý bất đồng bộ qua Kafka → Worker → gRPC để cập nhật Skill Matrix, Learning Path, Goals, Badge, Notification. Frontend chỉ cần refetch sau hành động.
4.  **Phản hồi & Báo cáo:** Báo cáo tuần được Worker tự động tổng hợp và gửi qua email + Notification. Frontend hiển thị qua Notification API.

---

## 3. Màn hình cần làm & API tương ứng

| Màn hình | API cần gọi | Vai trò | Trạng thái |
| :--- | :--- | :--- | :--- |
| **Adaptive Dashboard** | `GET /api/progress/details` | Learner | ✅ Sẵn sàng |
| **Skill Matrix** | (nằm trong `GET /api/progress/details` → trường `skillProgress`) | Learner | ✅ Sẵn sàng |
| **Learning Path** | `GET /api/learningpaths/current` | Learner | ✅ Sẵn sàng |
| **Recommended Lessons** | `GET /api/learningpaths/{learnerId}` | Learner | ✅ Sẵn sàng |
| **Goals** | `GET /api/goals/{learnerId}`, `POST /api/goals` | Learner | ✅ Sẵn sàng |
| **Notifications** | `GET /api/notifications`, `PUT /api/notifications/{id}/read` | Learner | ✅ Sẵn sàng |
| **Admin Feedback** | `GET /api/feedback`, `POST /api/feedback/{id}/review` | Admin | ✅ Sẵn sàng |

---

## 4. API Skill Matrix & Adaptive Dashboard

### 4.1 Lấy thông tin tiến trình & Skill Matrix

*   **Endpoint:** `GET /api/progress/details`
*   **Role:** Learner
*   **Headers:** `Authorization: Bearer <accessToken>`
*   **Request:** Không có body.
*   **Response JSON:**
    ```json
    {
      "success": true,
      "message": "Success",
      "data": {
        "userId": 2,
        "learnerProfileId": 1,
        "lessonsCompleted": 3,
        "totalLessons": 12,
        "overallCompletionRate": 25.0,
        "quizzesDone": 2,
        "quizzesPassed": 2,
        "averageQuizScore": 8.5,
        "quizHistory": [
          {
            "attemptId": 15,
            "quizId": 5,
            "quizTitle": "Grammar Intermediate Quiz",
            "attemptedAt": "2026-06-08T07:15:30Z",
            "score": 8.0,
            "maxScore": 10.0,
            "durationMinutes": 15,
            "isPassed": true
          }
        ],
        "skillProgress": [
          {
            "skill": "Grammar",
            "averageScorePercent": 80.0,
            "correctQuestions": 8,
            "totalQuestions": 10
          },
          {
            "skill": "Vocabulary",
            "averageScorePercent": 90.0,
            "correctQuestions": 9,
            "totalQuestions": 10
          }
        ],
        "lessonHistory": [
          {
            "lessonId": 3,
            "lessonTitle": "Present Simple Tense",
            "skill": "Grammar",
            "level": "A1",
            "completedAt": "2026-06-08T06:30:00Z"
          }
        ]
      }
    }
    ```
*   **Frontend Usage:**
    *   `skillProgress` → Radar Chart / Bar Chart năng lực (Skill Matrix).
    *   Kỹ năng có `averageScorePercent` thấp nhất = điểm yếu, hiển thị cảnh báo.
    *   `overallCompletionRate` → Progress bar tổng quát.
    *   `quizHistory`, `lessonHistory` → Danh sách lịch sử hoạt động.

---

## 5. API Weakness Analysis

*   **Trạng thái:** Backend xử lý nội bộ (không có endpoint REST công khai riêng).
*   **Frontend làm gì:** Dùng `skillProgress` từ `GET /api/progress/details`. Kỹ năng có điểm thấp nhất = điểm yếu. Không cần gọi API riêng.

---

## 6. API Recommendations & Learning Path

### 6.1 Lấy lộ trình cấp độ CEFR tổng quát

*   **Endpoint:** `GET /api/learningpaths/current`
*   **Role:** Learner
*   **Response JSON:**
    ```json
    {
      "success": true,
      "data": [
        {
          "id": 1,
          "title": "Cấp độ B1 - Intermediate (Current)",
          "desc": "Mô tả cấp độ B1.",
          "status": "Active",
          "xpReward": 300
        },
        {
          "id": 2,
          "title": "Cấp độ B2 - Upper-Intermediate",
          "desc": "Mô tả cấp độ B2.",
          "status": "Locked",
          "xpReward": 450
        }
      ]
    }
    ```
*   **Frontend Usage:** Vẽ Roadmap chặng CEFR. `status: "Active"` = chặng đang học, `"Locked"` = chưa mở.

### 6.2 Lấy danh sách bài học thích ứng trong chặng hiện tại

*   **Endpoint:** `GET /api/learningpaths/{learnerId}`
*   **Role:** Learner
*   **Response JSON:**
    ```json
    {
      "success": true,
      "data": {
        "pathId": 5,
        "learnerId": 1,
        "status": "InProgress",
        "items": [
          {
            "id": 1,
            "lessonId": 6,
            "lessonTitle": "Conditional Sentences Type 1",
            "sequenceOrder": 1,
            "status": "InProgress"
          },
          {
            "id": 2,
            "lessonId": 7,
            "lessonTitle": "Relative Clauses",
            "sequenceOrder": 2,
            "status": "Locked"
          }
        ]
      }
    }
    ```
*   **Frontend Usage:**
    *   `status: "InProgress"` → Nút **"Học ngay"** điều hướng sang lesson detail (Core System của Hoàng) bằng `lessonId`.
    *   `status: "Locked"` → Hiển thị icon khóa.

---

## 7. API Goals

### 7.1 Lấy danh sách mục tiêu

*   **Endpoint:** `GET /api/goals/{learnerId}`
*   **Role:** Learner
*   **Response JSON:**
    ```json
    {
      "success": true,
      "data": [
        {
          "id": 10,
          "learnerId": 1,
          "target": "Hoàn thành 5 bài học Ngữ pháp",
          "type": "LessonCount",
          "progressPercentage": 60.0,
          "isCompleted": false,
          "deadline": "2026-06-15T00:00:00Z"
        }
      ]
    }
    ```

### 7.2 Tạo mục tiêu mới

*   **Endpoint:** `POST /api/goals`
*   **Role:** Learner
*   **Request JSON:**
    ```json
    {
      "learnerId": 1,
      "target": "Đạt trung bình 8.0 điểm Quiz",
      "type": 1,
      "deadline": "2026-06-20T23:59:59Z"
    }
    ```
    *GoalType enum: `0=TOEIC, 1=IELTS, 2=VSTEP, 3=General, 4=LessonsPerWeek, 5=QuizzesPerWeek, 6=LearningStreak, 7=SkillScore, 8=TargetLevel`*

*   **Frontend Usage:**
    *   Progress bar theo `progressPercentage`.
    *   `isCompleted: true` → Hiển thị hoàn thành.
    *   Quá `deadline` mà chưa xong → Hiển thị **"Quá hạn"**.

---

## 8. API Achievements / Badges

*   **Trạng thái:** Backend xử lý nội bộ (Achievement Engine tự động cấp huy hiệu).
*   **Frontend làm gì:** Không cần gọi API riêng. Khi nhận badge, backend tạo Notification. Frontend đọc qua `GET /api/notifications`.

---

## 9. API Notifications

### 9.1 Lấy danh sách thông báo

*   **Endpoint:** `GET /api/notifications`
*   **Role:** Learner
*   **Response JSON:**
    ```json
    {
      "success": true,
      "data": [
        {
          "id": 101,
          "userId": 2,
          "title": "Huy hiệu mới đạt được!",
          "message": "Chúc mừng bạn đã đạt huy hiệu 'Goal Achiever'.",
          "isRead": false,
          "createdAt": "2026-06-08T07:20:00Z"
        },
        {
          "id": 100,
          "userId": 2,
          "title": "Nhắc nhở học tập",
          "message": "Bạn đã không học bài mới trong 3 ngày qua.",
          "isRead": true,
          "createdAt": "2026-06-07T09:00:00Z"
        }
      ]
    }
    ```

### 9.2 Đánh dấu đã đọc

*   **Endpoint:** `PUT /api/notifications/{id}/read`
*   **Response:** `{ "success": true, "data": true }`

### 9.3 Đánh dấu tất cả đã đọc

*   **Endpoint:** `POST /api/notifications/read-all`

### 9.4 Xóa toàn bộ thông báo

*   **Endpoint:** `DELETE /api/notifications/clear-all`

*   **Frontend Usage:**
    *   Badge count chuông = số phần tử có `isRead == false`.
    *   Click vào thông báo → gọi `PUT /api/notifications/{id}/read`.

---

## 10. API Weekly Report

*   **Trạng thái:** Backend xử lý nội bộ (`WeeklyLearningReportJob` tự động).
*   **Frontend làm gì:** Không cần gọi API riêng. Báo cáo tuần được chuyển thành email + Notification. Frontend đọc qua `GET /api/notifications`.

---

## 11. API Feedback Analysis – Admin

### 11.1 Xem toàn bộ phản hồi

*   **Endpoint:** `GET /api/feedback`
*   **Role:** Admin

### 11.2 Duyệt phản hồi (Review)

*   **Endpoint:** `POST /api/feedback/{id}/review`
*   **Role:** Admin
*   **Request JSON:**
    ```json
    {
      "feedbackId": 4,
      "adminNotes": "Đã ghi nhận, chuyển nhóm phân tích kiểm tra độ khó."
    }
    ```

### 11.3 Xác nhận giải quyết (Resolve)

*   **Endpoint:** `PUT /api/feedback/{id}/resolve`
*   **Role:** Admin

---

## 12. API Session / Logout

*   **Logout:** `POST /api/auth/logout`
*   **Frontend Action:**
    1. Gọi `POST /api/auth/logout` khi nhấn Đăng xuất.
    2. Xóa token khỏi `localStorage` / `sessionStorage`.
    3. Nếu bất kỳ API nào trả về `401` → tự động redirect về `/login`.

> **Ghi chú:** Cơ chế blacklist token (Session revocation) chạy hoàn toàn phía backend bằng Redis. Frontend không cần biết chi tiết, chỉ cần gọi logout endpoint và xóa token local.

---

## 13. Định dạng phản hồi lỗi

```json
// 400 Bad Request
{ "success": false, "message": "Mismatched Goal ID.", "data": null }

// 401 Unauthorized
{ "success": false, "message": "Unauthorized access.", "data": null }

// 403 Forbidden
{ "success": false, "message": "Access denied.", "data": null }
```

---

## 14. Đề xuất cấu trúc Router Frontend

### Learner
*   `/adaptive` – Adaptive Dashboard (Skill Matrix + tóm tắt tiến độ)
*   `/adaptive/path` – Learning Path / Recommended Lessons
*   `/goals` – Goals page
*   `/notifications` – Notifications page

### Admin
*   `/admin/feedback` – Feedback Analysis & Review

---

## 15. Components cần xây dựng

1.  `SkillMatrixRadarChart` – Radar Chart từ `skillProgress` của `GET /api/progress/details`
2.  `AdaptiveRoadmapSteps` – Lộ trình CEFR từ `GET /api/learningpaths/current`
3.  `RecommendedLessonList` – Danh sách bài học đề xuất, nút "Học ngay" / icon khóa
4.  `GoalProgressBarCard` – Card mục tiêu + progress bar + deadline
5.  `NotificationBellDropdown` – Chuông thông báo + count badge chưa đọc

---

## 16. UI State Management

*   **Loading:** Skeleton cho Skill Matrix chart và danh sách bài học khi gọi API.
*   **Empty State:** Hình minh họa thân thiện khi chưa có thông báo / chưa có mục tiêu.
*   **Refetch sau hành động:**
    *   Hoàn thành bài học / nộp Quiz → refetch `GET /api/progress/details` + `GET /api/learningpaths/{learnerId}`
    *   Tạo mục tiêu mới → refetch `GET /api/goals/{learnerId}`

---

## 17. cURL Testing Samples

```bash
# Skill Matrix & tiến trình
curl -X GET "http://localhost:5292/api/progress/details" -H "Authorization: Bearer <TOKEN>"

# Lộ trình CEFR
curl -X GET "http://localhost:5292/api/learningpaths/current" -H "Authorization: Bearer <TOKEN>"

# Bài học thích ứng
curl -X GET "http://localhost:5292/api/learningpaths/<LEARNER_ID>" -H "Authorization: Bearer <TOKEN>"

# Mục tiêu
curl -X GET "http://localhost:5292/api/goals/<LEARNER_ID>" -H "Authorization: Bearer <TOKEN>"

# Tạo mục tiêu
curl -X POST "http://localhost:5292/api/goals" \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"learnerId": 1, "target": "Study Grammar", "type": 4, "deadline": "2026-06-30T00:00:00Z"}'

# Thông báo
curl -X GET "http://localhost:5292/api/notifications" -H "Authorization: Bearer <TOKEN>"
```

---

## 18. Frontend Implementation Scope – Phạm vi chính xác

### 18.1 Frontend làm được ngay bằng REST API hiện có

| # | Màn hình | API | Role |
|---|---|---|---|
| 1 | **Adaptive Dashboard** | `GET /api/progress/details` | Learner |
| 2 | **Skill Matrix Chart** | `skillProgress` trong `GET /api/progress/details` | Learner |
| 3 | **Learning Path / Recommended Lessons** | `GET /api/learningpaths/current`, `GET /api/learningpaths/{learnerId}` | Learner |
| 4 | **Goals Page** | `GET /api/goals/{learnerId}`, `POST /api/goals` | Learner |
| 5 | **Notifications** | `GET /api/notifications`, `PUT /api/notifications/{id}/read`, `POST /api/notifications/read-all`, `DELETE /api/notifications/clear-all` | Learner |
| 6 | **Admin Feedback** | `GET /api/feedback`, `POST /api/feedback/{id}/review`, `PUT /api/feedback/{id}/resolve` | Admin |
| 7 | **Logout** | `POST /api/auth/logout` | All |

### 18.2 Frontend hiển thị gián tiếp (không có endpoint REST riêng)

| Tính năng | Cách frontend hiển thị |
|---|---|
| Weakness Analysis | Dùng kỹ năng thấp nhất trong `skillProgress` |
| Achievements / Badges | Qua Notification API |
| Weekly Report | Qua Notification API + email tự động |
| Certificate Goal | Qua Goals API + Notifications |

### 18.3 Frontend KHÔNG làm UI riêng cho

*   Outbox Pattern
*   Redis cache internals
*   Redis distributed idempotency
*   Session cleanup
*   Direct gRPC calls
*   Recommendation effectiveness analytics
*   Recommendation regeneration / statistics nâng cao
*   Hangfire job management
*   Kafka / DLQ management
*   Worker internals
*   Docker infrastructure

### 18.4 Frontend KHÔNG rewrite phần của Hoàng

*   Auth (Login / Register)
*   Lesson CRUD
*   Quiz & Question
*   Placement Test
*   Progress cơ bản
*   Feedback cơ bản (submit)
*   Admin Dashboard cơ bản

---

## 19. Backend Internal / Enhancement

> Các tính năng sau đây **đã được backend triển khai** để tăng độ ổn định, bảo mật và khả năng mở rộng, nhưng **không thuộc checklist UI chính**. Frontend không cần làm màn hình riêng cho các mục này trừ khi có yêu cầu bổ sung rõ ràng.

| Tính năng | Mô tả |
|---|---|
| **Outbox Pattern** | Đảm bảo event publish không mất nếu API crash giữa chừng |
| **Redis Distributed Idempotency** | Ngăn Worker xử lý trùng event bằng distributed lock |
| **Session / Token Revocation** | Blacklist JWT token trên Redis sau logout |
| **Session Cleanup Job** | Hangfire tự xóa session hết hạn định kỳ |
| **Direct gRPC GenerateRecommendations** | Worker gọi gRPC service nội bộ phân tích điểm yếu |
| **Recommendation Effectiveness Analytics** | Lưu `RecommendationEffectiveness`, `RecommendationStatisticSnapshot` để đánh giá thuật toán |
| **Recommendation Regeneration** | Worker tự regenerate recommendations theo trigger |
| **Recommendation Statistics** | Snapshot thống kê recommendation theo kỳ |
| **Full Skill Matrix Recalculation** | Hangfire job tính lại toàn bộ Skill Matrix định kỳ |
| **Hangfire Job Internals** | Chi tiết lịch job: Reminder, Weekly Report, Goal Check, Skill Decay, Log Cleanup |
| **Kafka Retry / DLQ** | 3 lần retry → dead-letter-topic nếu thất bại |
| **Redis Cache Internals** | Cache key scheme, TTL, invalidation logic |
| **Docker Infrastructure** | Container networking, volume mounts, healthcheck |

---

## 20. Hướng dẫn chạy backend cho frontend

### Cách A – Tự chạy local

```bash
git checkout main
git pull origin main
docker compose up -d
```

*   **API:** `http://localhost:5292`
*   **Swagger:** `http://localhost:5292/swagger`

### Cách B – Dùng backend từ máy khác

```http
http://<BACKEND_MACHINE_IP>:5292
```

> Ổn định nhất là mỗi frontend dev tự chạy backend local bằng Docker.

---

## 21. Checklist trước khi frontend bắt đầu code

- [ ] Pull `main` mới nhất
- [ ] `docker compose up -d` chạy healthy
- [ ] Swagger mở được: `http://localhost:5292/swagger`
- [ ] Đăng nhập → lấy được JWT token
- [ ] Token gắn vào `Authorization: Bearer <token>` header
- [ ] `GET /api/progress/details` → trả về data hợp lệ
- [ ] `GET /api/learningpaths/current` → trả về data hợp lệ
- [ ] `GET /api/notifications` → trả về data hợp lệ
- [ ] API base URL cấu hình đúng trong `.env` frontend

---

## 22. Thứ tự đề xuất làm frontend phần Huy

1. Kết nối API client + JWT interceptor (auto 401 redirect)
2. Adaptive Dashboard
3. Skill Matrix chart (Radar / Bar)
4. Learning Path / Recommended Lessons
5. Goals page
6. Notification bell + Notification page
7. Admin Feedback page
8. Polish: loading skeleton, empty state, error state
