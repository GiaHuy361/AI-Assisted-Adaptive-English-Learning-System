# Huy Adaptive Feature Flow Analysis

> **Lưu ý:** Tài liệu này không thay thế [API Handoff](./frontend-api-handoff-huy-adaptive.md).  
> Dùng để team hiểu **Huy bổ sung gì** vào Core Learning của Hoàng, **tác dụng từng phần**, và **frontend sẽ nhìn thấy kết quả ở đâu trên UI**.

---

## 1. Mục đích tài liệu

Phần Huy là một lớp **Adaptive (thích ứng)** chạy phía sau các flow có sẵn của Hoàng.  
Huy không viết lại Auth, Lesson, Quiz hay bất kỳ màn hình cũ nào. Huy chỉ lắng nghe các hành động của learner và **tự động tính toán + cập nhật** Skill Matrix, Learning Path, Goals, Badge và Notification phía sau.

Frontend chỉ cần gọi REST API để lấy kết quả cuối cùng và hiển thị.

---

## 2. Phạm vi kết nối với phần Hoàng

### Hoàng giữ nguyên các flow chính:

| Flow | Vai trò |
|---|---|
| Login / Register / JWT | Xác thực người dùng |
| Lesson | Xem và học bài |
| Quiz / Test | Nộp bài và nhận điểm |
| Placement Test | Xác định trình độ ban đầu |
| Progress cơ bản | Xem tiến độ học |
| Feedback | Học viên gửi đánh giá |
| Admin Dashboard | Quản trị cơ bản |

### Huy bổ sung xử lý Adaptive sau các hành động:

| Hành động của learner | Huy làm gì phía sau |
|---|---|
| Làm Placement Test | Tạo Skill Matrix, xác định điểm yếu, tạo Learning Path ban đầu |
| Nộp Quiz | Phân tích câu sai, cập nhật Skill Matrix, tạo Recommendation |
| Hoàn thành Lesson | Cập nhật goal progress, kiểm tra badge, cập nhật trạng thái recommendation |
| Gửi Feedback | Phân tích rating, cảnh báo admin nếu nội dung bị đánh giá thấp |
| Không hoạt động | Background job phát hiện, gửi nhắc học qua Notification + Email |
| Cuối tuần (hàng tuần) | Background job tổng hợp báo cáo học tập tuần |

---

## 3. Luồng tổng quan từ góc nhìn người dùng

```
 1. Learner đăng nhập
 2. Learner làm Placement Test hoặc nộp Quiz
 3. Core Learning (Hoàng) lưu kết quả vào database
 4. Backend publish sự kiện lên Kafka
 5. Huy Worker nhận sự kiện bất đồng bộ
 6. Worker gọi gRPC Service để phân tích điểm yếu và kỹ năng yếu
 7. Backend cập nhật Skill Matrix (điểm từng kỹ năng: Grammar, Vocabulary, Reading, Listening...)
 8. Backend tạo / cập nhật Learning Path và danh sách Recommended Lessons
 9. Backend cập nhật Goals, trao Badge và tạo Notification nếu đủ điều kiện
10. Frontend refetch REST API sau hành động
11. Learner thấy: Skill Matrix mới, lộ trình bài học phù hợp, tiến độ mục tiêu, thông báo badge
```

> Toàn bộ bước 4–9 chạy **bất đồng bộ phía backend**. Frontend chỉ tham gia bước 10–11: gọi API và hiển thị kết quả.

---

## 4. Phân tích từng flow – Huy làm thêm gì & UI hiện ra sao

| Flow của Hoàng | Huy làm thêm | Tác dụng | UI hiển thị như nào | Frontend cần làm |
|---|---|---|---|---|
| **Placement Test completed** | Tạo Skill Matrix ban đầu, xác định trình độ và điểm yếu, tạo Learning Path cá nhân hóa | Learner có lộ trình học riêng ngay sau test đầu vào | Skill Matrix chart hiện điểm từng kỹ năng; Roadmap CEFR hiện cấp độ; Learning Path hiện danh sách bài đầu tiên | Refetch `GET /api/progress/details` + `GET /api/learningpaths/current` |
| **Quiz submitted** | Phân tích câu trả lời sai, cập nhật Skill Matrix, phát hiện kỹ năng yếu, tạo Recommended Lessons mới | Bài học gợi ý dựa trên lỗi sai thực tế, không phải cố định | Kỹ năng yếu hiện cảnh báo trên Skill Matrix; danh sách bài học gợi ý thay đổi | Sau submit quiz refetch `GET /api/progress/details` + `GET /api/learningpaths/{learnerId}` |
| **Lesson completed** | Cập nhật recommendation status (bài đã học → không gợi ý lại), cập nhật goal progress, kiểm tra điều kiện badge | Hệ thống biết learner đã học theo gợi ý, tránh lặp bài; mục tiêu tiến lên | Lesson hiện đã hoàn thành; goal progress bar tăng; notification xuất hiện nếu đạt badge hoặc goal | Refetch `GET /api/learningpaths/{learnerId}` + `GET /api/goals/{learnerId}` + `GET /api/notifications` |
| **Feedback submitted** | Tổng hợp rating theo bài học / quiz / gợi ý; phát hiện nội dung bị đánh giá thấp; gửi cảnh báo admin | Admin biết nội dung nào cần xem lại để cải thiện chất lượng | Trang Admin Feedback hiện danh sách feedback + trạng thái; alert nếu có nội dung rating thấp | Admin gọi `GET /api/feedback` → review → resolve |
| **Learner không hoạt động** | Background job phát hiện learner không học trong N ngày; tạo Notification và gửi Email nhắc học | Nhắc học tự động, không cần admin can thiệp | Notification bell hiện badge mới; trang Notifications hiện nhắc học | Gọi `GET /api/notifications` |
| **Weekly report (cuối tuần)** | Background job tổng hợp số bài học, điểm quiz, streak, kỹ năng mạnh/yếu tuần vừa qua; gửi email + Notification | Learner có cái nhìn tổng thể về tiến độ học hàng tuần | Notification bell; nội dung notification là tóm tắt học tập tuần | Hiển thị qua `GET /api/notifications` |
| **Goal completed** | Kiểm tra điều kiện mục tiêu; publish GoalCompletedEvent; kiểm tra badge Achievement; tạo Notification chúc mừng | Tăng động lực học tập, learner được ghi nhận tiến bộ | Goal card hiện trạng thái "Đã hoàn thành"; notification badge chúc mừng | Refetch `GET /api/goals/{learnerId}` + `GET /api/notifications` |

---

## 5. Các module Huy làm thêm – tác dụng & UI

| Module | Tác dụng chính | Có UI riêng không? | Xuất hiện qua đâu |
|---|---|---|---|
| **Kafka / Event Processing** | Xử lý bất đồng bộ sau quiz/lesson/feedback, đảm bảo không bị mất dữ liệu | Không | Backend internal – frontend không cần biết |
| **gRPC Weakness Analysis** | Phân tích điểm yếu từng kỹ năng, topic hay sai | Không trực tiếp | Kết quả phản ánh qua Skill Matrix |
| **Skill Matrix** | Đo lường và lưu điểm năng lực từng kỹ năng (Grammar, Vocabulary, Reading, Listening...) | **Có** | Adaptive Dashboard – biểu đồ kỹ năng |
| **Adaptive Recommendation / Learning Path** | Gợi ý bài học phù hợp với điểm yếu và trình độ; ưu tiên bài chưa học | **Có** | Learning Path page – danh sách bài học thích ứng |
| **Goal Tracking** | Theo dõi tiến độ mục tiêu học tập; cập nhật tự động khi learner học/quiz | **Có** | Goals page – progress bar từng mục tiêu |
| **Achievement Engine** | Kiểm tra và tự động cấp badge khi đạt đủ điều kiện; tính streak, bài hoàn thành, quiz điểm cao | Gián tiếp | Notifications (khi nhận badge) |
| **Background Jobs** | Nhắc học khi inactive; tạo báo cáo tuần; kiểm tra goal hết hạn; giảm điểm kỹ năng nếu lâu không ôn | Gián tiếp | Notifications / Email |
| **Notification** | Lưu và phân phối thông báo: nhắc học, badge, goal, báo cáo tuần | **Có** | Notification bell + Notification page |
| **Feedback Analysis** | Tổng hợp và phân loại feedback; phát hiện nội dung rating thấp; cảnh báo admin | **Có** | Admin Feedback Analysis page |
| **Redis / Cache** | Tăng tốc đọc dữ liệu; chống xử lý event trùng lặp | Không | Backend internal – frontend không cần biết |
| **Docker / Infrastructure** | Chạy toàn bộ hệ thống trong container: MySQL, Kafka, Redis, API, Worker, gRPC | Không | DevOps / local `docker compose up -d` |

---

## 6. Frontend cần làm những màn nào

| Màn hình | Dữ liệu từ đâu |
|---|---|
| **Adaptive Dashboard** | `GET /api/progress/details` – tóm tắt tiến độ, skill progress |
| **Skill Matrix Chart** | Trường `skillProgress` trong response trên (Radar / Bar chart) |
| **Learning Path / Recommended Lessons** | `GET /api/learningpaths/current` + `GET /api/learningpaths/{learnerId}` |
| **Goals Page** | `GET /api/goals/{learnerId}`, `POST /api/goals` |
| **Notifications** | `GET /api/notifications`, `PUT /api/notifications/{id}/read` |
| **Admin Feedback Analysis** | `GET /api/feedback`, `POST /api/feedback/{id}/review`, `PUT /api/feedback/{id}/resolve` |

---

## 7. Frontend KHÔNG cần làm UI riêng cho

| Thứ không cần làm | Lý do |
|---|---|
| Kafka / DLQ management | Backend internal, DevOps quản lý qua Kafka UI (dev-tools profile) |
| Redis / cache management | Backend internal, DevOps quản lý qua Redis Commander |
| Worker management | Chạy ngầm, không có màn quản lý cho user |
| gRPC direct call | Worker gọi nội bộ, không expose ra ngoài |
| Hangfire job dashboard | Backend internal, không hiển thị cho user |
| Outbox UI | Backend reliability pattern, hoàn toàn nội bộ |
| Session cleanup UI | Backend tự dọn dẹp theo Hangfire job |
| Recommendation analytics / regeneration / statistics nâng cao | Backend lưu vào `RecommendationEffectiveness` – chưa có REST public |
| Docker infrastructure UI | DevOps / local run |

---

## 8. Kết luận cho Hoàng / Frontend

**Phần Huy không thay thế Core Learning của Hoàng.**

Huy chỉ thêm **một lớp adaptive chạy phía sau** các flow đã có:

```
Hoàng: Learner học bài → nộp quiz → hệ thống lưu kết quả
Huy:   Sau đó          → phân tích điểm yếu → cập nhật Skill Matrix
                        → tạo lộ trình bài học phù hợp hơn
                        → cập nhật mục tiêu và trao badge nếu đủ điều kiện
                        → tạo thông báo cho learner
```

Frontend chỉ cần:
1. **Refetch đúng API** sau các hành động học tập (quiz submit / lesson complete).
2. **Hiển thị kết quả cuối** qua 5 màn chính: Adaptive Dashboard, Learning Path, Goals, Notifications, Admin Feedback.
3. **Không gọi gì khác** ngoài REST API tại `http://localhost:5292`.

> Chi tiết API contract (endpoint, request, response mẫu): xem [Frontend API Handoff](./frontend-api-handoff-huy-adaptive.md).
