# Cấu hình Qwen cho trang tư vấn NEXORA

Trang tư vấn dùng Qwen3 Omni Flash (Free) qua API tương thích OpenAI của xKiro để hỗ trợ hiểu nhu cầu và soạn lời đáp hội thoại. NEXORA tự đọc catalog, chọn sản phẩm và áp dụng giá, tồn kho, thông số bắt buộc cùng quy tắc xếp hạng; Qwen không chọn hoặc tạo sản phẩm. Thẻ sản phẩm và dữ liệu hành động luôn do backend tạo từ catalog hiện hành.

## Cấu hình khi phát triển cục bộ

1. Tạo API key trên trang quản lý API key của xKiro. Không gửi key trong chat, ảnh chụp màn hình hoặc log; không lưu key trong appsettings, source code hay Git.
2. Mở PowerShell ở thư mục gốc repository. Nhập lệnh dưới đây, sau đó dán key mới tại prompt ẩn:

   ```powershell
   $secure = Read-Host "API key xKiro (nội dung sẽ được ẩn)" -AsSecureString
   $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
   try {
       $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
       dotnet user-secrets set "ConsultationAI:Qwen:ApiKey" $plain --project ".\src\NEXORA.Web\NEXORA.Web.csproj"
       if ($LASTEXITCODE -ne 0) { throw "Không lưu được API key." }
   }
   finally {
       [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
       $plain = $null
       $secure.Dispose()
   }
   ```

3. Khởi động lại website để nạp key. User Secrets chỉ thuộc Windows profile hiện tại; mỗi thành viên cần cấu hình key riêng.

Model mặc định là `qwen/qwen3-omni-flash:free`. Endpoint được cố định phía server là `https://api.xkiro.com/v1/`; không thay bằng URL không tin cậy vì key được gửi trong header Bearer.

## Dữ liệu và giới hạn

- Khi key được cấu hình, hội thoại được giới hạn độ dài và gửi từ server NEXORA tới xKiro để hỗ trợ phân loại/soạn lời đáp. Khi soạn lời đáp, server chỉ gửi thêm kết quả tìm kiếm có cấu trúc và tối đa năm sản phẩm đã được backend chọn.
- Nội dung Qwen chỉ là văn bản giải thích; không được dùng làm nguồn sự thật cho sản phẩm, giá, khuyến mãi, tồn kho, thông số hay trạng thái giao dịch. Nếu Qwen lỗi, timeout hoặc trả nội dung không hợp lệ, NEXORA dùng câu trả lời dự phòng.
- Chỉ gửi mô tả nhu cầu cần thiết. Không nhập mật khẩu, thông tin thanh toán hoặc dữ liệu cá nhân nhạy cảm. Nội dung được xử lý bởi một dịch vụ AI bên thứ ba; hãy xem chính sách dữ liệu và giới hạn miễn phí của nhà cung cấp.
- Mã danh mục và mã mục đích trả về được kiểm tra theo danh sách cho phép. Nếu API lỗi, quá thời gian chờ hoặc trả kết quả không hợp lệ, ứng dụng dùng parser cục bộ.
- Giá, tồn kho, thông số cứng, lọc sản phẩm và xếp hạng vẫn do NEXORA quyết định. Cấu hình này không thay đổi database, schema, migration hoặc quy trình đặt hàng.
- Lịch sử dành cho người dùng đã đăng nhập cần migration `AddConsultationChatHistory` được triển khai vào database thành viên sau khi được phê duyệt. Source không tự chạy migration khi khởi động. Khách chưa đăng nhập chỉ giữ ngữ cảnh tạm thời trong trình duyệt.
- Trợ lý hiện trả lời sau khi nhận đủ phản hồi từ provider; chưa bật streaming vì khả năng tương thích streaming của endpoint/model đang cấu hình chưa được xác minh.
- Log thành công: `Qwen consultation classification succeeded`. Nếu lỗi, log chỉ ghi trạng thái HTTP hoặc loại lỗi; không ghi API key hay nội dung yêu cầu.

## Tài liệu nhà cung cấp

- [xKiro Chat Completions API](https://docs.xkiro.com/api/chat-completions/)
- [xKiro Authentication](https://docs.xkiro.com/guides/authentication/)
- [Qwen3 Omni Flash (Free)](https://xkiro.com/models/qwen/qwen3-omni-flash:free)
