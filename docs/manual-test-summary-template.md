# Mẫu Ghi Kết Quả Test Tay — Phase 1 đến Phase 10

> **Hướng dẫn:** Điền vào bảng này sau khi hoàn thành test tay theo tài liệu  
> `docs/manual-testing-phase1-to-phase10.md`

---

## 1. Thông Tin Chung

| Mục | Nội dung |
|---|---|
| **Ngày test** | `____-__-__` |
| **Người test** | `Huy` |
| **Branch** | `feature/huy-backend-adaptive` |
| **Commit hash** | `b393f39` |
| **Môi trường** | `Docker / Local` |
| **OS** | `Windows / macOS / Linux` |

---

## 2. Trạng Thái Docker

```
docker compose ps
```

| Container | Status | Port | PASS/FAIL |
|---|---|---|---|
| adaptive-learning-mysql | | 33066 | |
| adaptive-learning-redis | | 6379 | |
| adaptive-learning-kafka | | 9092 | |
| adaptive-learning-grpc-service | | 50051, 50080 | |
| adaptive-learning-api | | 5292 | |
| adaptive-learning-worker | | — | |

**Ghi chú:**
```
(điền ghi chú nếu có container không healthy)
```

---

## 3. Build Result

```
dotnet build
```

| Mục | Kết quả |
|---|---|
| Errors | `0` / `__` |
| Warnings | `0` / `__` |
| Build Status | PASS / FAIL |

---

## 4. Test Result

```
dotnet test
```

| Mục | Kết quả |
|---|---|
| Total tests | `151` |
| Passed | `___` |
| Failed | `___` |
| Test Status | PASS / FAIL |

**Tests bị fail (nếu có):**
```
(liệt kê tên test bị fail và lý do)
```

---

## 5. Docker Smoke Test

```
powershell -ExecutionPolicy Bypass -File scripts/docker-smoke-test.ps1
```

| Mục | Kết quả |
|---|---|
| Total tests | `22` |
| Passed | `___` |
| Failed | `___` |
| Smoke Test Status | PASS / FAIL |

**Tests bị fail (nếu có):**
```
(liệt kê phase bị fail)
```

---

## 6. Kết Quả Test Thủ Công Từng Phase

### Phase 1 — Technical Skeleton
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| `dotnet build` 0 errors | | |
| `dotnet test` 151/151 | | |
| 6 containers healthy | | |
| docker compose config valid | | |

### Phase 2 — Kafka Event Processing
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Kafka topics tồn tại (8 topics) | | |
| Submit quiz → Worker log consumed | | |
| DLQ topic tồn tại | | |
| BackgroundJobExecutions có record | | |

### Phase 3 — gRPC Weakness Analysis
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| gRPC health `{"status":"Healthy"}` | | |
| Worker log có gRPC call sau quiz | | |
| LearnerWeaknessHistories có record | | |

### Phase 4 — Skill Matrix & Weakness History
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Placement → SkillMatrices có record | | |
| Quiz → SkillMatrixHistories tăng | | |
| Idempotency: không duplicate EventId | | |

### Phase 5 — Adaptive Recommendation
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Sau quiz có recommendation | | |
| PriorityScore > 0 | | |
| Reason có text | | |
| Complete lesson → rec Status=Completed | | |
| Không gợi ý bài đã completed | | |

### Phase 6 — Goal Tracking & Achievement
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| GoalProgressHistories tăng | | |
| LearnerBadges có record | | |
| GoalCompletedEvent publish | | |
| BadgeAwardedEvent publish | | |

### Phase 7 — Background Jobs & Notification
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Hangfire UI accessible | | |
| 10+ recurring jobs đăng ký | | |
| Weekly report job chạy OK | | |
| Notifications có record | | |
| NotificationDeliveryAttempts có record | | |

### Phase 8 — Feedback & Redis Cache
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Submit feedback → FeedbackAnalysis cập nhật | | |
| AverageRating đúng | | |
| Redis có `adaptive:v1:*` keys | | |
| API vẫn chạy khi Redis tắt | | |

### Phase 9 — Docker Full System
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| docker compose config valid | | |
| smoke test 22/22 | | |
| Dữ liệu còn sau restart container | | |

### Phase 10 — Remaining Adaptive Features

#### 10.1 Certificate Goal Verification
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| CertificateTestResult saved | | |
| Goal Status → Completed | | |
| GoalProgressHistory: PreviousValue/NewValue đúng | | |
| OutboxMessage GoalCompleted tạo | | |

#### 10.2 Full Skill Matrix Recalculation
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Job chạy OK | | |
| SkillMatrixHistory có SourceType=PeriodicRecalculation | | |
| Idempotency: không duplicate cùng period | | |

#### 10.3 Session Cleanup
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Login → UserSession Status=Active | | |
| Expired session → Status=Expired sau job | | |
| Active session vẫn Active | | |

#### 10.4 Token Revocation
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Token hoạt động trước logout | | |
| Sau logout → token bị reject 401 | | |
| Redis có `token-revoked:*` key | | |

#### 10.5 Direct gRPC GenerateRecommendations
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Response có danh sách lesson | | |
| LessonId > 0 | | |
| PriorityScore > 0 | | |
| Reason có text | | |
| Không chứa lesson đã completed | | |

#### 10.6 Recommendation Effectiveness
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| RecommendationEffectiveness tạo sau job | | |
| ScoreBefore và ScoreAfter đúng | | |
| WasEffective đúng với ngưỡng cải thiện | | |

#### 10.7 Recommendation Regeneration
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| RecommendationHistory Action=Replaced | | |
| Recommendation mới với SourceEventId `regen_*` | | |
| IRecommendationService được gọi | | |

#### 10.8 Recommendation Statistics
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| RecommendationStatisticSnapshots tạo | | |
| EffectivenessRate trong [0, 1] | | |
| AverageImprovement >= 0 | | |

#### 10.9 Outbox Pattern
| Test | PASS/FAIL | Ghi chú |
|---|---|---|
| Business action → OutboxMessage Status=Pending | | |
| Sau OutboxPublisherJob → Status=Published | | |
| ProcessedAt không NULL | | |
| Kafka nhận được message | | |

### Kiểm Tra Tương Thích Frontend Hoàng
| Endpoint | PASS/FAIL | Response format đúng | Ghi chú |
|---|---|---|---|
| POST /api/auth/register | | | |
| POST /api/auth/login | | | |
| POST /api/auth/logout | | | |
| GET /api/lessons | | | |
| GET /api/lessons/{id} | | | |
| POST /api/lessons/{id}/complete | | | |
| GET /api/quizzes | | | |
| GET /api/quizzes/{id} | | | |
| POST /api/quizzes/{id}/submit | | | |
| POST /api/placement/submit | | | |
| GET /api/recommendations | | | |
| POST /api/feedback | | | |
| GET /health | | | |
| CORS từ frontend port | | | |

---

## 7. Bugs Phát Hiện

| # | Phase | Mô tả bug | Mức độ | Trạng thái |
|---|---|---|---|---|
| 1 | | | Critical/Major/Minor | Open/Fixed |
| 2 | | | | |
| 3 | | | | |

*(thêm dòng nếu cần)*

---

## 8. Screenshots Cần Chụp

Chụp và đính kèm (nếu cần gửi cho team):

- [ ] Docker `ps` tất cả healthy
- [ ] Hangfire UI có đủ recurring jobs
- [ ] Swagger UI / Postman Collection chạy được
- [ ] MySQL query kết quả SkillMatrix
- [ ] MySQL query kết quả Recommendations
- [ ] Redis keys `adaptive:v1:*`
- [ ] Log Worker consume event
- [ ] Token revocation 401 response

---

## 9. Tổng Kết

| Mục | Kết quả |
|---|---|
| Tổng số test thủ công | `___` |
| PASS | `___` |
| FAIL | `___` |
| Bugs nghiêm trọng | `___` |
| **Quyết định** | `Ready for Frontend / Need Fix` |

### Ghi chú thêm
```
(điền ghi chú tổng thể, quan sát, hoặc điểm cần lưu ý cho team)
```

---

## 10. Quyết Định Cuối

- [ ] ✅ **READY FOR FRONTEND** — Tất cả test PASS, không có bug nghiêm trọng
- [ ] ⚠️ **CONDITIONAL** — Có một số minor bugs cần fix sau khi bàn giao
- [ ] ❌ **NEED FIX FIRST** — Có bug nghiêm trọng, cần fix trước khi bàn giao

**Người quyết định:** `Huy`  
**Ngày:** `____-__-__`  
**Chữ ký (nếu cần):** `___________`

---

*Template tạo ngày 2026-06-08 | Branch: feature/huy-backend-adaptive | Commit: b393f39*
