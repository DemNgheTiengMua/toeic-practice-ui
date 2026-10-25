# Các lỗi phát hiện khi làm #75/#76 (chưa sửa, thuộc phần khác)

Kiểm tra lại trên `main` @ `d7cca74` (19/10/2026). Mỗi mục có chỗ trong code và cách tái hiện.

## 1. Nút "Start speaking / writing" trên dashboard bị 404 — **lỗi chắc chắn**

- **Ở đâu:** `src/Services/LearnerService.Mission.cs:422`
  ```csharp
  ActionUrl: $"/{skillPrefix}/instructions/{publishedSkillExam.ExamId}",
  ```
- **Sai vì:** route thật là `/speaking/{id}/instructions` và `/writing/{id}/instructions`, nên link sinh ra `/speaking/instructions/4` trả 404.
  `LearningPathService.cs` và `Views/Speaking/Review.cshtml` tạo link đúng.
- **Tái hiện:** đăng nhập `learner@demo.local` (hoặc `learner.fresh@demo.local`) → Dashboard → khối *Today's Mission*, bước *Skill practice* → bấm *Start speaking/writing* → 404.
- **Sửa:** `ActionUrl: $"/{skillPrefix}/{publishedSkillExam.ExamId}/instructions"` và thêm test trong `LearnerServiceTests.Mission.cs` để chặn lỗi quay lại.

## 2. Studio: thời lượng audio upload gần như luôn lưu là 300 giây

- **Ở đâu:** `src/WebMVC/Views/Admin/ExamStudio.cshtml`, hàm `uploadAudioFile` (khoảng dòng 1216).
- **Sai vì:** `durationSec = 300` được ghi đè trong `loadedmetadata`, nhưng sự kiện này chạy bất đồng bộ. Request upload thường gửi đi trước khi metadata đọc xong, nên `ExamSectionAudio.DurationSec` thường là 300, không phải độ dài thật.
- **Sửa:** chờ `loadedmetadata` (bọc trong Promise) rồi mới gửi `POST /admin/exams/{id}/audio`.

## 3. Trang admin có link sang trang của learner → bị chuyển tới `/auth/denied`

- **Tái hiện:** đăng nhập `admin@demo.local`, rồi đi theo các link trong giao diện admin. Có link tới `/dashboard` và `/checkout?packageId=1|2|3`, bấm vào thì bị chuyển tới `/auth/denied`.
- **Nghi là:** layout dùng chung hoặc trang Pricing hiện link của learner cho cả admin. Chưa xác định chính xác view nào.

## 4. Tên model Claude có vẻ sai — **cần kiểm tra**

- **Ở đâu:** `src/WebAPI/appsettings.json` → `"Claude": { "Model": "claude-sonnet-3.7" }`
- Định dạng này khác model ID chính thức của Anthropic. Có thể proxy HH-tech chấp nhận tên riêng này; nếu không thì chấm điểm AI thật sẽ lỗi. Chưa gọi thử.

## 5. `DemoSeedTests` ghi cứng tên database test

- **Ở đâu:** `tests/Services.Tests/DemoSeedTests.cs:24`
  ```csharp
  fixture.ConnectionString.Replace("Database=ExtraEfficiencyServicesTest", ...)
  ```
- **Sai vì:** khi chạy test với `TEST_DB_NAME=...` (để chạy song song nhiều worktree), chuỗi trên không khớp nên test đọc nhầm database và fail.
- **Sửa:** dựng connection string mới từ base thay vì `Replace` tên cố định.

## Đang được sửa (không cần ai nhận)

- Audio ở trang luyện tập và trang câu đã lưu vẫn đọc `Group.AudioPath`, nên chưa phát audio master hay đoạn cắt upload từ Studio.
  Đã sửa trên branch `fix/practice-saved-audio` (commit `dba3bba`) nhưng chưa mở PR; branch cần rebase lên `main`.

## Đã sửa, bỏ qua

- Mật khẩu demo trong `CLAUDE.md` (đã ghi `Demo1234!`).
- E2E trên CI (team sửa ở `d7cca74`).
