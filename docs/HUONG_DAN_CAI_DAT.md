# Hướng dẫn cài đặt NEXORA trên máy Windows

> Tài liệu này dành cho môi trường Development của từng thành viên. Mỗi người dùng database local riêng `NEXORA_Dev_<Ten>`. Không kết nối database `NEXORA` chính hoặc `NEXORA_IntegrationTests`.

## 1. Cài công cụ cần thiết

Cài các thành phần sau trên Windows:

- Git.
- .NET 10 SDK.
- SQL Server Express với instance `SQLEXPRESS`, hoặc SQL Server LocalDB (`MSSQLLocalDB`).
- PowerShell (có sẵn trên Windows).
- Tùy chọn: Visual Studio 2022 với workload **ASP.NET and web development**.

Mở PowerShell và xác nhận SDK:

```powershell
dotnet --list-sdks
```

Danh sách phải có SDK phiên bản `10.x`.

## 2. Tải source

Clone repository rồi chuyển vào thư mục dự án:

```powershell
git clone https://github.com/duytruong2004/Nexora.git
Set-Location .\Nexora
```

Nếu nhóm cấp cho bạn repository riêng, dùng URL clone mà nhóm cung cấp. Source không chứa database; clone repository không tự kết nối tới SQL Server.

## 3. Cấu hình database Development riêng

Chạy trình cấu hình để nhập connection string qua prompt ẩn:

```powershell
.\tools\setup-development.ps1 -ConfigureConnection
```

Chọn một SQL Server local và đặt tên database riêng, ví dụ `NEXORA_Dev_Alice`:

```text
Server=(localdb)\MSSQLLocalDB;Database=NEXORA_Dev_Alice;Integrated Security=True;TrustServerCertificate=True
```

Hoặc dùng SQL Server Express local:

```text
Server=.\SQLEXPRESS;Database=NEXORA_Dev_Alice;Integrated Security=True;TrustServerCertificate=True
```

Thay `Alice` bằng tên riêng của bạn. Ưu tiên Windows Authentication. Nếu phải dùng SQL Authentication, nhập connection string tại prompt ẩn; không gửi qua chat/email, không ghi vào source hoặc Git.

Kiểm tra profile và target đã cấu hình:

```powershell
.\tools\setup-development.ps1
```

Lệnh kiểm tra cấu hình không kết nối SQL Server. Nếu policy từ chối target, dừng và sửa cấu hình local trước khi tiếp tục; không đổi target sang QA hoặc database chính.

## 4. Restore và build

Từ thư mục gốc repository:

```powershell
dotnet restore .\NEXORA.sln
dotnet build .\NEXORA.sln
```

Nếu lệnh build kết thúc với lỗi, xử lý lỗi đó trước khi tạo database hoặc chạy website.

## 5. Tạo schema và dữ liệu demo

Các bước dưới đây ghi dữ liệu vào database Development local đã cấu hình. Chỉ chạy khi bạn xác nhận đúng tên database `NEXORA_Dev_<Ten>` của mình.

Áp dụng migrations:

```powershell
.\tools\setup-development.ps1 -Migrate
```

Khởi tạo role, danh mục, thương hiệu, thông số, sản phẩm và inventory demo:

```powershell
.\tools\setup-development.ps1 -SeedData
```

Các lệnh migrate và seed là thao tác riêng, tường minh; website không tự chạy chúng khi khởi động. Seed dữ liệu demo không nhập dữ liệu từ QA hoặc Production.

## 6. Tạo tài khoản Development

Đặt email `@nexora.test`, tên hiển thị và mật khẩu riêng cho các tài khoản demo bằng prompt tương tác:

```powershell
.\tools\setup-development.ps1 -ConfigureDemoAccounts
```

Sau đó tạo tài khoản và role mapping trong database Development:

```powershell
.\tools\setup-development.ps1 -SeedAccounts
```

Mật khẩu được nhập ẩn và lưu trong User Secrets trên máy hiện tại. Không dùng mật khẩu tài khoản thật. Lệnh không tự đặt lại mật khẩu, kích hoạt lại hay thay role của tài khoản đã tồn tại nhưng không khớp cấu hình.

## 7. Chạy website

Khởi động trên địa chỉ local mặc định `http://127.0.0.1:5047`:

```powershell
.\tools\setup-development.ps1 -Run
```

Mở URL trên bằng Chrome. Giữ cửa sổ PowerShell đang chạy; nhấn **Ctrl+C** trong cửa sổ đó để dừng website.

Nếu cổng mặc định đang bận, chọn cổng local khác:

```powershell
.\tools\setup-development.ps1 -Run -Url http://127.0.0.1:5088
```

## 8. Nếu muốn chạy bằng Visual Studio

1. Mở `NEXORA.sln`.
2. Chọn `NEXORA.Web` làm Startup Project.
3. Chọn profile `http` hoặc `https`.
4. Kiểm tra cấu hình Development đang trỏ tới database local `NEXORA_Dev_<Ten>`.
5. Nhấn **Run**.

Không dùng `appsettings.Development.json` chứa thông tin kết nối của máy khác. File mẫu an toàn nằm tại `src/NEXORA.Web/appsettings.Development.example.json`; file cấu hình cá nhân `appsettings.Development.json` đã được Git bỏ qua.

## 9. Lỗi thường gặp và an toàn dữ liệu

- **Không có .NET 10 SDK:** cài .NET 10 SDK rồi mở lại PowerShell.
- **Không tìm thấy SQL Server:** xác nhận đã cài và khởi động LocalDB hoặc SQL Server Express local.
- **Connection bị policy từ chối:** kiểm tra lại server local, profile `PersonalDevelopment` và tên database `NEXORA_Dev_<Ten>`.
- **Cổng đã sử dụng:** chạy với một cổng localhost khác bằng tham số `-Url`.
- **Windows Code Integrity chặn ứng dụng:** dừng tại đó và nhờ quản trị viên xử lý bằng quy trình ký/allowlist được phê duyệt. Không tắt hoặc né chính sách bảo vệ Windows.

Không dùng các lệnh migrate, seed hoặc provisioning với database QA hay database chính. Không đưa connection string, mật khẩu, User Secrets, dữ liệu QA hoặc file `appsettings.Development.json` cá nhân vào Git.

## Hướng dẫn chi tiết

Các quy tắc policy, giới hạn của seeders, kiểm thử và xử lý sự cố được ghi trong [Thiết lập NEXORA trên máy thành viên](TEAM_DEVELOPMENT_SETUP.md).
