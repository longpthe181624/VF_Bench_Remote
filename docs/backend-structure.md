# Tổ chức backend

Các module dữ liệu chung, DBC và request test dùng một controller với một service:

| Controller | Service | Contract đầu vào |
| --- | --- | --- |
| `DuLieuChungController` | `DuLieuChungService` | `Contracts/SharedFiles.cs` |
| `DatabaseController` | `DatabaseService` | `Contracts/DatabaseFiles.cs` |
| `RequestsController` | `RequestService` | `Contracts/TestRequests.cs` |

Controller giữ route, model binding, phân quyền, mã HTTP và header tải file. Service xử lý truy vấn, validation nghiệp vụ, Draft/Release, revision, snapshot và lưu dữ liệu. Service trả dữ liệu hoặc ném `ApiException`; không trả `IActionResult`.

`ICurrentCaller` cung cấp danh tính và quyền cho service; `HttpCurrentCaller` đọc từ principal đã được xác thực. Quyền endpoint vẫn được kiểm tại controller. RequestService tiếp tục kiểm chủ sở hữu và quyền chọn/tải file.

`ApiExceptionMiddleware` xử lý lỗi nghiệp vụ và `JobFlowException` tập trung. Lỗi có thông báo giữ response `{ error }`. Lỗi 400/404 không thông báo dùng ProblemDetails như MVC; Forbidden giữ response rỗng. Lỗi hệ thống trả thông báo chung cùng `traceId`, chi tiết exception nằm trong log.

Các service dùng cùng scoped `AppDbContext`. Transaction upload DBC vẫn bao gồm bản ghi file và outbox. Kho file, khóa, revision và snapshot giữ cơ chế hiện có. Không thêm repository CRUD chỉ để bọc EF Core.

Các controller khác vẫn giữ cấu trúc hiện tại. Khi tách tiếp, ưu tiên service theo controller và giữ hợp đồng HTTP cùng test hồi quy. Chỉ tách thêm service khi có nghiệp vụ dùng chung rõ ràng.

Kiểm tra:

```powershell
dotnet build backend/BenchConsole.Api.Tests --no-restore
dotnet run --project backend/BenchConsole.Api.Tests --no-build --no-restore
dotnet run --project backend/BenchConsole.Core.SmokeTest --no-restore
```

Test API dùng EF InMemory và kho file tạm; kiểm chứng transaction, constraint và migration cần môi trường SQL Server thật.
