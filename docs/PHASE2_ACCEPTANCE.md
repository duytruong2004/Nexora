# Phase 2 – Final Acceptance

**Trạng thái: ĐÃ NGHIỆM THU PHASE 2 VÀ TẠO COMMIT LOCAL.** Admin CRUD, Catalog, luồng compare selection và quyền Customer-only đã được kiểm tra bằng trình duyệt. Regression và secret scan cuối đã đạt. Không push; không bắt đầu Phase 3.

Ngày kiểm tra: 16/10/2026. Môi trường Development: SQL Server `PC01\SQLEXPRESS`, database `NEXORA`, website `http://127.0.0.1:5058`.

## 1. Tài khoản và database

- Truy vấn SQL Server xác nhận user ID `2` thuộc role `Admin`. Database chỉ có một user được gán role Admin. Role được cấp trước lượt nghiệm thu theo xác nhận trực tiếp của chủ dự án; không đổi mật khẩu hoặc tạo tài khoản/mật khẩu Admin mặc định.
- Trình duyệt đang có phiên đã đăng nhập. Phiên mở được `/workspace/Admin` và các trang Admin. Giao diện không hiển thị user ID; vì truy vấn DB chỉ có một user mang role Admin, phiên này được suy ra là tài khoản ID 2. Không nhập, xem hoặc lưu mật khẩu.
- Để kiểm thử Customer-only, đăng ký qua UI tài khoản tạm `NEXORA Phase2 QA Customer` (ID `3`), xác minh SQL chỉ đọc cho thấy duy nhất role `Customer`, đăng nhập thành công, mở Catalog, bị từ chối tại `/Admin/Products`, rồi đăng xuất. Sau kiểm thử, xóa đúng user ID 3 do lượt này tạo; trước đó xác nhận không có order/cart/favorite/inventory transaction/review hoặc dữ liệu Identity phụ thuộc. Truy vấn sau dọn xác nhận không còn tài khoản test. Không dùng tài khoản hoặc mật khẩu của chủ dự án.
- Schema không đổi trong Phase 2. Truy vấn SQL xác nhận 22 bảng nghiệp vụ + 4 bảng kỹ thuật Identity (`AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`) + `__EFMigrationsHistory` = 27 bảng. Migration duy nhất được ghi là `20261015173046_InitialFoundation`.
- Dữ liệu Development sau khi dọn dữ liệu test: 10 categories, 2 brands, 11 products, 0 images, 11 specifications, 18 category specifications và 52 product specification values. Có 38 giá trị NUMBER; cả 38 có `numeric_value` khớp `value`; không có giá trị kiểu khác mang `numeric_value`. Không có active product nào thiếu specification bắt buộc.
- Đã tạo dữ liệu test nhận diện bằng tiền tố `P2ACCEPT` để kiểm thử CRUD và ảnh. Sau kiểm thử, ảnh được xóa qua giao diện; các hàng thử nghiệm riêng đã được dọn trong transaction giới hạn bằng đúng SKU/ID và kiểm tra không có order/cart/favorite/inventory/review/promotion phụ thuộc. Truy vấn sau dọn xác nhận không còn hàng thử nghiệm. Không sửa sản phẩm hoặc người dùng sẵn có.

## 2. Lỗi phát hiện và sửa

1. Area views không kế thừa `Views/_ViewImports.cshtml`, khiến `asp-action`/`asp-for` ở khu vực Admin chưa được render thành `href`, `action` và field HTML. Đã thêm `src/NEXORA.Web/Areas/_ViewImports.cshtml`; browser xác nhận đường dẫn CRUD và form hoạt động.
2. Form Product trả HTTP 500 khi tạo HTML validation cho giới hạn giá theo culture `vi-VN`. Đã cấu hình `RangeAttribute` parse/convert giới hạn decimal theo invariant culture; trang Create/Edit mở bình thường.
3. Khi Specification bắt buộc không hợp lệ, form lỗi mất tên sản phẩm/category/specification. Controller đã nạp lại editor metadata và giữ các giá trị vừa gửi; browser kiểm tra thông báo lỗi cùng các nhãn vẫn hiển thị.
4. Product có thể được tạo ở trạng thái ACTIVE trong Category có Specification bắt buộc nhưng chưa có giá trị. `ProductManagementService` hiện từ chối ACTIVE cho tới khi đủ giá trị. Browser đã xác nhận tạo ACTIVE bị chặn, tạo INACTIVE được, nhập giá trị xong mới chuyển ACTIVE thành công.
5. Help text ảnh đại diện đã sửa để mô tả đúng hành vi: ảnh mới thành ảnh đại diện và ảnh cũ được giữ lại như ảnh chi tiết. Database có filtered unique index cho tối đa một ảnh đại diện mỗi sản phẩm.

Không thay đổi schema/migration hoặc cấu hình database.

## 3. Phân loại kiểm thử và kết quả

### Unit/controller tests

- `dotnet test NEXORA.sln`: 64 passed, 0 failed, 0 skipped. Có policy test cho giá trị Specification, ID Specification không thuộc Category, selection comparison, và controller test đảm bảo form lỗi nạp lại metadata. Đây không phải HTTP integration test.
- Các test authorization xác nhận các Admin catalog controllers yêu cầu role `Admin` và POST action có antiforgery.

### HTTP smoke test

Gửi GET bằng `HttpClient` không có cookie tới website đang chạy: `/`, `/Catalog`, `/account/register`, `/account/login` trả HTTP 200; `/Admin/Products` trả HTTP 302 tới login. Đây là anonymous HTTP smoke, không xác nhận đăng nhập Customer.

### Browser test – phiên Admin và Catalog

- Admin dashboard, Category, Brand, Product, Specification và ProductSpecification editor mở được trong phiên có quyền Admin.
- Category: create, read, edit; từ chối tên trùng; thao tác Remove với sản phẩm liên quan chuyển Category sang inactive.
- Brand: create, read, edit; từ chối tên trùng; thao tác Remove khi đang được Product tham chiếu chuyển Brand sang inactive.
- Product: create, details/read, edit giá/mô tả; từ chối giá khuyến mại cao hơn giá; cho phép deactivation. Product được tạo trong Category có required Specification không thể ACTIVE trước khi nhập đủ thông số.
- ProductImage: PNG 1×1 hợp lệ được tải và trình duyệt xác nhận ảnh hiển thị; `.txt` bị từ chối theo chữ ký nội dung; PNG 5 MiB + 1 byte bị từ chối theo giới hạn 5 MiB. Tải primary mới chỉ để lại một primary, ảnh cũ thành ảnh chi tiết; xóa ảnh chi tiết và ảnh chính thành công. Không kiểm thử từng codec JPEG/WebP riêng.
- Specification: create, edit, từ chối tên trùng; gán NUMBER/GB cho Category, đánh dấu lọc/bắt buộc; nhập dữ liệu 16/32/64 GB; required value rỗng bị từ chối và form giữ context. Wrong-category ID không thể chọn qua UI; policy test xác nhận từ chối ID ngoài tập Category hiện tại. Specification thử được deactivate sau khi gỡ assignment/test values.
- Trang chủ và Catalog đọc dữ liệu từ SQL Server; tìm kiếm trả đúng Product; filter Category, Brand, giá và numeric Specification trả đúng hàng; chi tiết hiển thị image và Specification; search không khớp hiển thị empty state.
- Route so sánh mở bằng browser GET cho 3 sản phẩm cùng Category và hiển thị thông số khác nhau; request 4 ID bị từ chối. Sau đó đã kiểm thử selection UI: tick hai checkbox Laptop Alpha/Beta, xác nhận trạng thái `2/3` và nút gửi được bật, submit form thành công; URL nhận đúng `ids=1&ids=2` và trang hiển thị hai sản phẩm cùng danh mục. Việc chọn và submit checkbox được xác nhận bằng trình duyệt.
- Responsive: tại viewport 390×844, Catalog có `documentElement.scrollWidth=375`, không tràn ngang. Viewport override đã được reset.

### Chưa kiểm thử qua browser

- Không đăng nhập tài khoản cá nhân của chủ dự án; đăng ký, đăng nhập và đăng xuất được kiểm tra với Customer tạm do QA tạo và sau đó dọn khỏi Development database.
- Không thử mọi action Admin bằng Customer; quyền truy cập bị từ chối đã được kiểm tra end-to-end tại `/Admin/Products`, cùng với authorization unit/controller tests cho Admin catalog endpoints.
- Chưa kiểm thử mọi loại ảnh JPEG/WebP, trình duyệt/thiết bị vật lý khác, hoặc mục tiêu hiệu năng NFR01.

## 4. Build và model/migration

- `dotnet restore NEXORA.sln`: thành công.
- `dotnet build NEXORA.sln`: thành công, 0 warning, 0 error.
- `dotnet test NEXORA.sln`: 64/64 passed.
- `dotnet ef migrations has-pending-model-changes --no-build --project src/NEXORA.Infrastructure --startup-project src/NEXORA.Web --context AppDbContext`: không có thay đổi model kể từ migration cuối.
- Không chạy `database update`, không tạo migration mới, không chạy script schema.

## 5. Git, tài liệu và điều kiện chốt

- Checkpoint Phase 1 `fcd1153` vẫn tồn tại. Đã tạo commit local `feat: complete Phase 2 product catalog`; không push.
- `.gitignore` loại `bin/`, `obj/`, local Development config, `.env`, secrets, certificate/private-key files, Data Protection keys và upload media. Quét secrets cuối cần được ghi nhận sau khi hoàn tất xác nhận thủ công.
- Regression cuối: `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln` thành công với 0 warning/0 error; `dotnet test NEXORA.sln` đạt 64/64.
- Secret scan trên 70 file text thuộc danh sách commit: không có dấu hiệu password, token, key hoặc connection string nhạy cảm. `.gitignore` xác nhận loại bỏ Development config và upload media. Staged diff đã được kiểm tra trước commit; chỉ commit local, không push.
- Không còn kiểm thử bắt buộc nào đang chờ. Không đo mục tiêu hiệu năng NFR01; xem đó là giới hạn đã ghi nhận, không phải kết quả đạt.

Tư vấn lựa chọn sản phẩm bằng ngôn ngữ tự nhiên vẫn là chức năng bắt buộc của khóa luận và thuộc Phase 3. Phase 3 chưa bắt đầu.
