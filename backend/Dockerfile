# Đóng gói BenchConsole.Api để backend sống qua khởi động lại máy, thay vì
# phải gõ `dotnet run` mỗi phiên làm việc.
#
# Build từ thư mục `backend/`:
#     docker build -t bench-api ./backend
# Nhưng bình thường thì để docker-compose lo, xem service `api`.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Chép riêng file .csproj rồi restore TRƯỚC khi chép mã nguồn. Docker cache
# theo từng lớp, nên sửa code mà không đụng gói thì lớp restore được dùng lại —
# đỡ tải lại toàn bộ NuGet mỗi lần build.
COPY BenchConsole.Core/BenchConsole.Core.csproj BenchConsole.Core/
COPY BenchConsole.Api/BenchConsole.Api.csproj   BenchConsole.Api/
RUN dotnet restore BenchConsole.Api/BenchConsole.Api.csproj

# Cố ý KHÔNG chép BenchConsole.Core.SmokeTest: nó là bộ kiểm thử chạy tay,
# không thuộc thứ cần có trong ảnh chạy thật.
COPY BenchConsole.Core/ BenchConsole.Core/
COPY BenchConsole.Api/  BenchConsole.Api/
RUN dotnet publish BenchConsole.Api/BenchConsole.Api.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Khai tường minh thay vì dựa vào mặc định của ảnh. `+` nghĩa là nghe mọi địa
# chỉ trong container — đây là chỗ sửa cái tật `applicationUrl` trong
# launchSettings.json chỉ nghe localhost, khiến máy khác không gọi được.
# Việc có cho ra ngoài mạng hay không thì do phần `ports` của compose quyết định.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BenchConsole.Api.dll"]
