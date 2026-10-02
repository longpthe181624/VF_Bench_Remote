# Đóng gói BenchConsole.Api để backend sống qua khởi động lại máy, thay vì
# phải gõ `dotnet run` mỗi phiên làm việc.
#
# Build từ thư mục gốc repo:
#     docker build -t bench-api -f backend/Dockerfile .
# Nhưng bình thường thì để docker-compose lo, xem service `api`.

FROM node:24-alpine AS frontend
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Chép riêng file .csproj rồi restore TRƯỚC khi chép mã nguồn. Docker cache
# theo từng lớp, nên sửa code mà không đụng gói thì lớp restore được dùng lại —
# đỡ tải lại toàn bộ NuGet mỗi lần build.
COPY backend/BenchConsole.Core/BenchConsole.Core.csproj BenchConsole.Core/
COPY backend/BenchConsole.Api/BenchConsole.Api.csproj   BenchConsole.Api/
RUN dotnet restore BenchConsole.Api/BenchConsole.Api.csproj

# Cố ý KHÔNG chép BenchConsole.Core.SmokeTest: nó là bộ kiểm thử chạy tay,
# không thuộc thứ cần có trong ảnh chạy thật.
COPY backend/BenchConsole.Core/ BenchConsole.Core/
COPY backend/BenchConsole.Api/  BenchConsole.Api/
RUN dotnet publish BenchConsole.Api/BenchConsole.Api.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
COPY --from=frontend /web/dist ./wwwroot/app

# Khai tường minh thay vì dựa vào mặc định của ảnh. `+` nghĩa là nghe mọi địa
# chỉ trong container — đây là chỗ sửa cái tật `applicationUrl` trong
# launchSettings.json chỉ nghe localhost, khiến máy khác không gọi được.
# Việc có cho ra ngoài mạng hay không thì do phần `ports` của compose quyết định.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BenchConsole.Api.dll"]
