# Cấp quyền Admin trong môi trường Development

NEXORA không tạo tài khoản quản trị hoặc mật khẩu mặc định. Quyền Admin chỉ được cấp cho tài khoản đã đăng ký, qua lệnh console tương tác trong môi trường Development.

## Các bước thực hiện

1. Đảm bảo SQL Server Development `NEXORA` đã sẵn sàng và tài khoản cần cấp quyền đã đăng ký, đang hoạt động.
2. Dừng phiên NEXORA.Web đang chạy để tránh nhầm lẫn giữa phiên web và thao tác quản trị.
3. Mở PowerShell tại thư mục solution `D:\Projects\NEXORA`.
4. Xác nhận môi trường là `Development`:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   ```

5. Chạy lệnh console:

   ```powershell
   dotnet run --project .\src\NEXORA.Web\NEXORA.Web.csproj -- --provision-admin
   ```

6. Nhập email của tài khoản đã tồn tại. Công cụ tra cứu tài khoản trong Identity; nếu không tìm thấy hoặc tài khoản không hoạt động thì không thay đổi dữ liệu.
7. Kiểm tra tên hiển thị và ID tài khoản, sau đó nhập chính xác `GRANT ADMIN <ID>` như lệnh hiển thị. Nếu chuỗi xác nhận không khớp, thao tác bị hủy.
8. Đăng nhập bằng thông tin của chính tài khoản đó và kiểm tra quyền Admin.

Lệnh chỉ chấp nhận lời gọi đứng riêng, cần console tương tác và từ chối môi trường khác Development. Không có endpoint HTTP tự nâng quyền; biểu mẫu đăng ký luôn gán role Customer. Log chỉ ghi ID người dùng và kết quả thao tác, không ghi email, mật khẩu hay token. Cấp lại cho tài khoản đã là Admin là thao tác idempotent và không thêm liên kết role trùng.
