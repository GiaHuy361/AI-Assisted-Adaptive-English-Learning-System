# HƯỚNG DẪN VẬN HÀNH & KỊCH BẢN DEMO BẢO VỆ ĐỒ ÁN
> **Dự án:** AI-Assisted Adaptive English Learning System  
> **Tài liệu:** Hướng dẫn chi tiết dành cho các thành viên trong nhóm để chạy demo trực quan và trả lời câu hỏi của Hội đồng phản biện.

---

## I. CHUẨN BỊ TRƯỚC BUỔI DEMO

### 1. Khởi chạy toàn bộ hệ thống
Mở Terminal tại thư mục gốc của dự án và chạy lệnh sau để kích hoạt toàn bộ các containers ngầm:
```bash
docker compose up -d
```
Đợi khoảng 30 giây để các cơ sở dữ liệu và Kafka Broker chuyển sang trạng thái **Healthy** (Có thể kiểm tra trên Docker Desktop).

### 2. Danh sách các URL cần mở sẵn trên trình duyệt
Trước khi bắt đầu chiếu màn hình cho thầy cô, hãy chuẩn bị sẵn các tab trình duyệt sau:

| STT | Thành phần | Địa chỉ URL | Vai trò khi trình diễn |
| :--- | :--- | :--- | :--- |
| 1 | **Giao diện Học viên (Web Frontend)** | `http://localhost:5173` | Nơi thao tác chính (Đăng ký, Đăng nhập, Làm bài thi, Học bài). |
| 2 | **Tài liệu API (Swagger Backend)** | `http://localhost:5292/swagger` | Nơi chạy thử trực tiếp các API và cấu trúc dữ liệu nếu thầy cô hỏi code. |
| 3 | **Trang quản lý tiến trình (Hangfire)** | `http://localhost:5292/hangfire` | Minh họa kiến trúc chạy ngầm (Recurring Jobs, Outbox Publisher). |
| 4 | **Trang giám sát Kafka (Kafka UI)** | `http://localhost:8085` | Minh họa kiến trúc truyền thông điệp hướng sự kiện (Event-Driven). |
| 5 | **Bộ nhớ đệm & Khóa (Redis Commander)** | `http://localhost:8086` | Minh họa cơ chế Cache và Khóa phân tán (Distributed Lock). |

---

## II. KỊCH BẢN DEMO CHI TIẾT (LIVE ACTION SCRIPT)

### Bước 1: Khởi tạo tài khoản & Làm bài đánh giá năng lực (Placement Test)
* **Thao tác trên Web:**
  1. Vào `http://localhost:5173`, chọn mục đăng ký và tạo một tài khoản học viên hoàn toàn mới (ví dụ: `learner_test1`).
  2. Sau khi đăng nhập, chỉ cho thầy cô thấy trình độ mặc định ban đầu là **`A1 Starter`** ở góc trái.
  3. Bấm vào mục **"Kiểm tra đầu vào" (Placement Test)** và bắt đầu làm bài.
  4. Trả lời đúng **từ 3/5 câu trở lên**, rồi bấm **Nộp bài**.
* **Result:** Trình độ của học viên lập tức được cập nhật thành **`A2 Elementary`**, đồng thời hệ thống tự động tải và sinh ra **Lộ trình học tập thích ứng** được cá nhân hóa bắt đầu từ trình độ **A2**.
* **Lời thoại thuyết trình:** 
  > *"Thưa thầy cô, hệ thống áp dụng cơ chế đánh giá thích ứng. Ngay sau khi học viên nộp bài Placement Test, Backend sẽ tự động chấm điểm, tính toán năng lực tương ứng theo khung CEFR và cập nhật lộ trình học tập phù hợp ngay lập tức mà không cần quản trị viên can thiệp."*

---

### Bước 2: Học bài & Làm Quiz (Biểu diễn tính năng Thích ứng & Kiến trúc xử lý ngầm)
* **Thao tác trên Web:**
  1. Vào mục **"Lộ trình học tập"** hoặc **"Bài học thích ứng"**.
  2. Chọn một bài học cấp độ A2 (ví dụ: *Past Simple Regulars* hoặc *Describing Places*), bấm **"Học ngay"**.
  3. Đọc nội dung lý thuyết bài học, sau đó cuộn xuống cuối trang bấm **"Làm Quiz"**.
  4. Trả lời các câu hỏi kiểm tra của bài học và bấm **Nộp bài**.
* **Lời thoại thuyết trình (Ăn điểm kiến trúc lớn):**
  > *"Tại bước nộp bài Quiz này, để hệ thống không bị nghẽn (Bottleneck) khi có hàng ngàn học viên nộp bài cùng lúc, chúng em áp dụng kiến trúc **Event-Driven Architecture** kết hợp mẫu thiết kế **Transactional Outbox Pattern**:"*
  > 1. *API chấm bài thi xong sẽ lưu kết quả vào MySQL, đồng thời ghi nhận một sự kiện `QuizSubmittedEvent` vào bảng `OutboxMessages` trong cùng một Transaction để đảm bảo tính nhất quán (Atomicity).*
  > 2. *Một tiến trình ngầm sẽ quét bảng Outbox này và đẩy thông điệp sự kiện sang Apache Kafka Broker.*
  > 3. *Worker Service ngầm sẽ tiêu thụ thông điệp đó từ Kafka và gọi dịch vụ **gRPC Service** để phân tích các kỹ năng yếu (Listening, Grammar...) rồi cập nhật lại Skill Matrix cho học viên.*

---

### Bước 3: Chứng minh kiến trúc trên các công cụ quản trị (Show bằng chứng kỹ thuật)

#### 1. Show hàng đợi chạy ngầm trên Hangfire (`http://localhost:5292/hangfire`)
* **Thao tác:** Bấm sang tab Hangfire -> Chọn **Recurring Jobs**.
* **Điểm cần nhấn mạnh:**
  * Chỉ vào job **`outbox-publisher`**: Đây là job quét bảng Outbox để bắn tin nhắn sang Kafka.
  * Chỉ vào job **`decay-old-scores`**: Đây là tính năng mô phỏng hiện tượng quên kiến thức của con người. Nếu học viên bỏ bê không ôn luyện một kỹ năng nào đó quá 30 ngày, hệ thống sẽ tự động trừ điểm năng lực của kỹ năng đó để bắt học lại bài cũ.

#### 2. Show sự kiện chạy thời gian thực trên Kafka UI (`http://localhost:8085`)
* **Thao tác:** Vào Kafka UI -> Chọn **Topics** -> Chọn topic **`quiz-submitted`** -> Chọn tab **Messages**.
* **Điểm cần nhấn mạnh:** Chỉ cho thầy cô thấy payload JSON của bài Quiz học viên vừa nộp ở Bước 2. Chứng minh các microservice đang giao tiếp với nhau thời gian thực qua Kafka Broker.

#### 3. Show bộ nhớ đệm và cơ chế chống trùng lặp trên Redis Commander (`http://localhost:8086`)
* **Thao tác:** Mở Redis Commander -> Chỉ vào các key cache.
* **Điểm cần nhấn mạnh:**
  * **Cache-Aside Pattern:** Hệ thống lưu cache các bài học và tiến độ học tập trên Redis để giảm tải tối đa cho MySQL Database, giúp tốc độ phản hồi API gần như tức thời.
  * **Distributed Lock (Khóa phân tán):** Khi Worker nhận tin nhắn từ Kafka, nó sẽ dùng Redis tạo một khóa phân tán để đảm bảo tính **Idempotency** (một bài Quiz nộp lên chỉ được xử lý đúng một lần duy nhất, tránh lỗi trùng lặp dữ liệu khi hệ thống bị mất kết nối mạng và gửi lại tin nhắn).

---

## III. MỘT SỐ CÂU HỎI PHẢN BIỆN THƯỜNG GẶP & CÁCH TRẢ LỜI

**Q1: Vì sao học viên trình độ A2 vẫn xem được bài học cấp độ A1 nhưng không xem được bài B1?**
> **Trả lời:** Hệ thống áp dụng cơ chế phân quyền kiểm tra thích ứng ở Backend. Chúng em chặn không cho học viên cấp thấp học vượt cấp lên bài cấp cao (ví dụ: A1 Starter không thể xem bài A2/B1). Tuy nhiên, học viên cấp cao (A2) vẫn được quyền xem lại bài cấp thấp (A1) để phục vụ cho việc ôn tập lại kiến thức cũ.

**Q2: Nếu mạng bị lag hoặc mất kết nối giữa API và Kafka khi học viên đang nộp bài thì dữ liệu có bị mất không?**
> **Trả lời:** Không bị mất dữ liệu nhờ mẫu thiết kế **Transactional Outbox Pattern**. Kết quả bài làm của học viên đã được lưu an toàn trong MySQL Database cùng với bản ghi Outbox. Khi Kafka kết nối lại, tiến trình ngầm `outbox-publisher` trên Hangfire sẽ tự động quét và gửi bù lại tất cả các sự kiện chưa được xử lý.

**Q3: gRPC Service trong hệ thống dùng để làm gì? Tại sao không dùng REST API thông thường?**
> **Trả lời:** gRPC Service đóng vai trò là công cụ phân tích năng lực học tập không lưu trạng thái (Stateless). Chúng em dùng gRPC thay vì REST API vì gRPC chạy trên giao thức HTTP/2, truyền dữ liệu nhị phân (Protocol Buffers) giúp tối ưu hóa băng thông và đạt tốc độ giao tiếp cực kỳ nhanh giữa các dịch vụ nội bộ (API <-> Worker).
