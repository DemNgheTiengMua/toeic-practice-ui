# Ticket đang làm dở

> Cập nhật mỗi khi dừng giữa chừng. Session mới đọc file này để biết tiếp từ đâu.

## #139 — XONG

Commit `e2d7148`, đã push lên `origin/main`.

- Bỏ `Jwt:SigningKey` khỏi `appsettings.Development.json`
- CI sinh key ngẫu nhiên mỗi lần chạy
- `crawl-env.sh` provision key vào user-secrets lần đầu, không ghi đè
- `SecretScanTests` chặn tái diễn
- 1384 test xanh

**Việc còn lại cho người dùng:** operator nào đang chạy instance cũ đã lấy key từ file commit **phải set key mới và restart** — nếu không instance đó vẫn tin key đã lộ.

## #140 — ĐANG LÀM

**Trạng thái: mới đọc code, chưa sửa gì.**

Đã xác định:

- `src/WebMVC/Program.cs:19` — `AddHttpClient<ICompanionApiClient, CompanionApiClient>` + `CookieBearerHandler`, **không có timeout, không có resilience**
- `src/Contracts/CompanionApiClient.cs:47` — `ApiException : HttpRequestException`, có `Code`/`Status`
- `src/WebMVC/Infrastructure/CookieBearerHandler.cs` — handler duy nhất trong pipeline
- 2 call site streaming dùng `HttpCompletionOption.ResponseHeadersRead`: `CompanionApiClient.AdminExams.cs:121,146` (export) — không được cắt
- Test host hiện có: `CookieRevalidationTests.cs`, `StaticAssetPipelineTests.cs` dùng `WebApplicationFactory`

Chưa quyết: dùng `Microsoft.Extensions.Http.Resilience` hay viết tay (repo **chưa có package nào** về resilience/Polly).

**Bước tiếp theo:** đọc docs thư viện qua context7, rồi viết test trước theo §Testing Decisions của ticket.

## #146, #147 — CHƯA BẮT ĐẦU

Chờ #139/#140 xong mới cử sub-agent riêng, mỗi cái một session.
