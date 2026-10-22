# Thiết lập NEXORA trên máy thành viên

Hướng dẫn này tạo một database Development riêng trên máy của từng thành viên. Không dùng SQL Server của chủ dự án, database `NEXORA` chính hoặc `NEXORA_IntegrationTests`.

## 1. Cài công cụ

Cài các thành phần sau:

- .NET 10 SDK.
- SQL Server Express với instance `SQLEXPRESS`, hoặc SQL Server LocalDB (`MSSQLLocalDB`).
- PowerShell và Git.
- Visual Studio 2022 có workload ASP.NET and web development nếu muốn chạy bằng IDE.

Clone repository và mở PowerShell trong thư mục source:

```powershell
git clone https://github.com/duytruong2004/Nexora.git
Set-Location .\Nexora
dotnet --list-sdks
```

Trong danh sách SDK phải có phiên bản `10.x`.

Clone chỉ tải source code. SQL Server database không nằm trong Git và mỗi thành viên phải tự tạo database local.

## 2. Cấu hình database local

Mỗi thành viên dùng một tên riêng theo dạng `NEXORA_Dev_<Member>`, ví dụ `NEXORA_Dev_Alice`. Tên chỉ dùng chữ cái ASCII, số, dấu gạch ngang hoặc gạch dưới.

Cấu hình connection string bằng ASP.NET Core User Secrets:

```powershell
.\tools\setup-development.ps1 -ConfigureConnection
```

Script yêu cầu nhập connection string trong chế độ ẩn và lưu vào User Secrets của tài khoản Windows hiện tại. Ví dụ connection string có thể nhập:

```text
Server=(localdb)\MSSQLLocalDB;Database=NEXORA_Dev_Alice;Integrated Security=True;TrustServerCertificate=True
```

Với SQL Server Express:

```text
Server=.\SQLEXPRESS;Database=NEXORA_Dev_Alice;Integrated Security=True;TrustServerCertificate=True
```

Ưu tiên Windows Authentication. Nếu SQL Server local yêu cầu SQL Authentication, nhập connection string có tài khoản riêng qua prompt ẩn; không lưu nó trong file source, email, chat hoặc Git.

Profile được lưu là `PersonalDevelopment`. Kiểm tra cấu hình:

```powershell
.\tools\setup-development.ps1
```

Lệnh kiểm tra dùng đúng policy trong source, chỉ xác thực cấu hình và **không kết nối SQL Server**. Nó chỉ chấp nhận LocalDB hoặc SQL Server local cùng database `NEXORA_Dev_<Member>`. Nó từ chối `NEXORA`, `NEXORA_IntegrationTests`, máy SQL từ xa hoặc failover partner từ xa, database tên khác quy ước, cấu hình thiếu và file database attach.

`appsettings.Development.example.json` là mẫu không chứa credential. Nếu cần file cấu hình, có thể sao chép thành `src/NEXORA.Web/appsettings.Development.json`; file đích đã được `.gitignore` loại trừ. User Secrets và environment variables có độ ưu tiên cao hơn file JSON. Không ghi connection string cá nhân vào `appsettings.json` được commit.

Các thành viên không cần truy cập `PC01`. Database QA của chủ dự án là môi trường riêng, không phải database Development dùng chung. Clone GitHub cũng không tạo kết nối tới SQL Server nào.

## 3. Restore, build và tạo database từ migrations

Restore và build source:

```powershell
dotnet restore .\NEXORA.sln
dotnet build .\NEXORA.sln
```

Cài EF Core CLI nếu máy chưa có:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.11
```

Nếu đã cài phiên bản khác:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.11
```

Tạo database và áp dụng toàn bộ migrations bằng lệnh có kiểm tra target:

```powershell
.\tools\setup-development.ps1 -Migrate
```

Lệnh EF Core tương đương, sau khi cấu hình User Secrets, là:

```powershell
dotnet ef database update `
  --project .\src\NEXORA.Infrastructure\NEXORA.Infrastructure.csproj `
  --startup-project .\src\NEXORA.Web\NEXORA.Web.csproj `
  --context AppDbContext
```

Design-time factory đọc cùng file cấu hình Development, User Secrets và environment variables như ứng dụng. Factory từ chối mọi target EF không phải profile `PersonalDevelopment` trên database local `NEXORA_Dev_<Member>`. Vì vậy các lệnh EF không thể dùng profile QA/Production hoặc tự rơi về database chính. Website không chạy `Migrate`, `EnsureCreated` hay tạo database khi khởi động.

Trong source hiện có ba migrations. Chúng được áp dụng tuần tự để tạo schema rỗng; không có dữ liệu QA/Production được sao chép.

## 4. Khởi tạo role và dữ liệu demo

Chỉ sau khi `-Migrate` thành công, chủ động chạy:

```powershell
.\tools\setup-development.ps1 -SeedData
```

Lệnh này chạy trên database cá nhân đã được policy xác nhận và bổ sung bốn role chuẩn, danh mục, thương hiệu, specification, sản phẩm demo, quy tắc tư vấn và inventory demo còn thiếu. Các seeder hiện có chạy theo kiểu bổ sung, có thể chạy lại; chúng không tạo đơn hàng, khách hàng, thanh toán hoặc ảnh giả. Tồn kho chỉ được tạo cho SKU demo chưa có hàng `Inventory`; inventory đã tồn tại và sổ giao dịch không bị cập nhật.

Seeder catalog hiện cung cấp 11 sản phẩm mô phỏng và không có ảnh sản phẩm đi kèm. Không có file danh sách 15 sản phẩm đã xác minh trong source hiện tại; vì vậy hướng dẫn không tuyên bố đã seed 15 sản phẩm thực tế. Có thể tải ảnh qua giao diện quản trị sau khi đăng nhập.

Không seeding tự động khi website khởi động. RoleSeeder và demo seeders chỉ chạy khi dùng lệnh tường minh trên database Development cá nhân. QA và Production không được tự tạo role, user, sản phẩm hoặc dữ liệu demo lúc startup.

## 5. Tạo bốn tài khoản demo bằng ASP.NET Core Identity

Đặt thông tin cho bốn tài khoản riêng trên máy của bạn:

```powershell
.\tools\setup-development.ps1 -ConfigureDemoAccounts
```

Script hỏi email, tên hiển thị và mật khẩu ẩn cho từng role `Customer`, `Sales`, `Warehouse`, `Admin`. Email phải dùng domain không định tuyến `nexora.test`; mật khẩu nằm trong User Secrets của tài khoản Windows local. Không dùng email/mật khẩu thật của thành viên.

Sau khi chạy `-SeedData`, tạo tài khoản:

```powershell
.\tools\setup-development.ps1 -SeedAccounts
```

Ứng dụng tạo user và role mapping qua `UserManager`/`RoleManager` trong một transaction có execution strategy. Mỗi tài khoản mới nhận đúng một role. Nếu tài khoản đã tồn tại và đang active với đúng một role dự kiến, lệnh giữ nguyên. Nếu tài khoản có role khác, nhiều role, hoặc inactive, lệnh dừng và không reset mật khẩu, kích hoạt lại hay đổi quyền tài khoản hiện có.

Địa chỉ `.test` chỉ dành cho local demo; tài khoản được xác nhận email trong môi trường Development và không gửi email thật. User Secrets là kho bí mật cục bộ thuận tiện cho phát triển, không phải kho mã hóa cho secret production.

Đăng ký thông thường trên website chỉ tạo Customer. Không dùng lệnh cấp Admin cũ để thêm role vào user đã là Customer/Sales/Warehouse: policy single-role sẽ từ chối nhiều role.

## 6. Chạy NEXORA

Chạy bằng PowerShell:

```powershell
dotnet run --project .\src\NEXORA.Web\NEXORA.Web.csproj
```

Hoặc chạy bằng script sau khi thiết lập và migrate:

```powershell
.\tools\setup-development.ps1 -Run
```

Script mặc định dùng `http://127.0.0.1:5047`. Mở URL được `dotnet run` in ra trong Chrome. Không cần truy cập máy chủ của thành viên khác.

Trong Visual Studio:

1. Mở `NEXORA.sln`.
2. Chọn `NEXORA.Web` làm Startup Project.
3. Chọn profile `http` hoặc `https` trong `src/NEXORA.Web/Properties/launchSettings.json`.
4. Xác nhận User Secrets hoặc environment variables của máy đang chọn `PersonalDevelopment` và database riêng trước khi nhấn Run.

Đổi cổng khi bị trùng:

```powershell
.\tools\setup-development.ps1 -Run -Url http://127.0.0.1:5088
```

Hoặc:

```powershell
dotnet run --project .\src\NEXORA.Web\NEXORA.Web.csproj --no-launch-profile -- --urls http://127.0.0.1:5088
```

Website không tự seed khi chạy bình thường. Mọi thao tác nghiệp vụ trên website chỉ ảnh hưởng database Development local đang được cấu hình.

## 7. Lệnh kiểm thử

Restore/build và chạy allowlist SQL-free trong GitHub Actions bằng workflow `NEXORA safe unit and HTTP tests`. Workflow không có credential SQL và không chạy SQL integration tests. Trên máy thành viên, không chạy test SQL cho tới khi đã tạo database test local riêng và xác minh fixture.

Không dùng database chính hay `NEXORA_IntegrationTests` để test. Migrations, seeders và provisioning được policy chặn nếu target sai.

## 8. Secrets và file cấu hình

- `.gitignore` loại trừ `appsettings.Development.json`, file secrets, `.env`, user files, upload runtime, `bin`, `obj` và test results.
- Không commit hoặc gửi connection string, mật khẩu demo, API key, User Secrets JSON hay dữ liệu QA.
- Nếu environment variable `ConnectionStrings__DefaultConnection` đã được đặt ở Windows, nó có thể ghi đè User Secrets. Khi kiểm tra báo target không hợp lệ, rà soát biến môi trường mà không in giá trị connection ra log.
- Không chia sẻ database file hoặc password hash giữa các thành viên.

## 9. Reset database cá nhân có kiểm soát

Không có script setup nào tự xóa database. Trước khi reset, dừng ứng dụng, kiểm tra `Database:Profile=PersonalDevelopment`, xác nhận tên là database local `NEXORA_Dev_<Member>`, và backup dữ liệu cần giữ.

EF drop sau đó sẽ hỏi xác nhận; không thêm `--force`:

```powershell
dotnet ef database drop `
  --project .\src\NEXORA.Infrastructure\NEXORA.Infrastructure.csproj `
  --startup-project .\src\NEXORA.Web\NEXORA.Web.csproj `
  --context AppDbContext
```

Sau reset, tạo schema lại bằng `.\tools\setup-development.ps1 -Migrate`, rồi seed tường minh nếu cần. Chỉ thực hiện trên database máy mình. Nếu có bất kỳ nghi ngờ nào về profile hoặc tên database, không chạy lệnh drop; tạo một database Development mới với tên khác.

## 10. Windows Smart App Control

Một số máy Windows 11 có thể chặn DLL tự build theo chính sách Code Integrity. Không tắt Smart App Control, AppLocker hay Code Integrity và không di chuyển binary để né policy. Hãy dùng build/test runner được chính sách cho phép, hoặc nhờ quản trị viên áp dụng quy trình signing/allowlisting hợp lệ. Microsoft mô tả [cách ký ứng dụng để tương thích Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control); chứng thư phải đến từ nhà cung cấp được tin cậy, chứng thư tự ký không tự động đáp ứng điều kiện đó.

## 11. Lỗi thường gặp

| Lỗi | Cách xử lý |
|---|---|
| Policy từ chối connection | Chạy script kiểm tra; xác nhận profile `PersonalDevelopment`, server local và database `NEXORA_Dev_<Member>`. Không đổi target thành QA/main. |
| LocalDB không tìm thấy | Cài SQL Server Express LocalDB hoặc dùng instance Express local; kiểm tra instance đang hoạt động. |
| `dotnet ef` không nhận project | Cài/cập nhật `dotnet-ef` phiên bản 10.0.11 và chạy lệnh từ repository root. |
| Seed báo thiếu bảng | Chạy `-Migrate` trên database cá nhân trước; không gọi `EnsureCreated`. |
| Account seed dừng vì account đã tồn tại | Kiểm tra role và active state cục bộ. Command cố ý không tự sửa account không khớp. |
| Port đã được dùng | Chọn cổng localhost khác bằng `-Run -Url http://127.0.0.1:<port>`. |
| Code Integrity chặn app/test | Dừng thử nghiệm tại đó và xin hỗ trợ signing/allowlisting theo quy trình; không bypass policy. |

## Thành phần hiện không được tự động cấu hình

- Không có email provider thật; email Development nằm trong mailbox bộ nhớ và biến mất khi ứng dụng dừng.
- Dữ liệu sản phẩm demo là mô phỏng; ảnh không có sẵn.
- Tài khoản demo chỉ tạo khi thành viên cấu hình secrets và chạy seed account command.
- Không có deployment hay connection tới database Production trong hướng dẫn local này.
