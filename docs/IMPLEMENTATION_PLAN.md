# Kế hoạch triển khai NEXORA

## 1. Mục đích và phạm vi

Tài liệu này chuyển các yêu cầu đã mô tả trong khóa luận thành kế hoạch triển khai website thương mại điện tử NEXORA bằng ASP.NET Core MVC, .NET 10, C#, Entity Framework Core và SQL Server. Phase 1 đã tạo nền tảng ứng dụng, áp dụng migration đầu tiên lên database Development đã được chủ dự án duyệt, và kiểm tra các luồng nền tảng. Các chức năng nghiệp vụ ngoài đăng ký, xác thực và phân quyền nền tảng vẫn nằm trong các Phase tiếp theo.

Phạm vi nghiệp vụ căn cứ Chương 2 của tài liệu khóa luận, cùng phần tổng hợp phạm vi ở mục 1.6. Tài liệu khóa luận nằm tại [`NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx`](./NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx).

## 2. Kết quả Phase 0 – Audit

### 2.1. Hiện trạng workspace

- Khi bắt đầu Phase 0, workspace chỉ có thư mục `docs` và tài liệu Word khóa luận; chưa có `.sln`, `.csproj`, mã ứng dụng hoặc test project. Phase 1 đã tạo solution và các project như mô tả ở mục 4.
- `git` có sẵn, phiên bản 2.55.0.windows.5. Lệnh kiểm tra trạng thái xác nhận thư mục hiện tại chưa được khởi tạo thành Git repository.
- Tài liệu Word đọc được. Chương 2 xác định 50 yêu cầu chức năng, 10 yêu cầu phi chức năng, 19 quy tắc nghiệp vụ, 4 tác nhân, 46 Use Case và 22 bảng dữ liệu chính thức.
- Bảng `product_price_histories` chỉ là phần mở rộng tùy chọn, không nằm trong 22 bảng chính thức. `orders.completed_at` là trường bắt buộc của bảng `orders`.
- Tài liệu Word gốc không bị sửa. Không có source code, migration hoặc database nào được tạo trong Phase 0; migration nền tảng được tạo trong Phase 1.

### 2.2. Môi trường phát triển

| Thành phần | Kết quả kiểm tra |
|---|---|
| Windows | Windows 11, môi trường Windows x64 |
| .NET SDK | 10.0.400; runtime ASP.NET Core 10.0.11 có sẵn |
| EF Core CLI | `dotnet ef` 10.0.11 chạy được |
| EF Core/Identity packages | Microsoft.EntityFrameworkCore.SqlServer, Microsoft.AspNetCore.Identity.EntityFrameworkCore và Design 10.0.11 |
| Git | 2.55.0.windows.5; workspace chưa phải Git repository |
| SQL command line | `sqlcmd` có sẵn |
| SQL Server | SQL Server 2025 Express, instance `PC01\SQLEXPRESS`, service đang chạy |
| Kết nối SQL Server | Kết nối Windows Integrated Authentication thành công; chỉ chạy truy vấn đọc thông tin máy chủ/phiên bản |
| Database và migration | Database `NEXORA` được xác minh chưa tồn tại trước khi áp dụng; migration `20261015173046_InitialFoundation` hiện đã được áp dụng |
| Gói có cảnh báo bảo mật | Dependency `System.Security.Cryptography.Pkcs` được ghim ở 9.0.21; `dotnet list package --vulnerable --include-transitive` không báo package dễ tổn thương |

SQL Server Browser đang dừng và bị vô hiệu hóa; kết nối cục bộ trực tiếp tới instance có tên vẫn thành công. Khả năng kết nối từ máy khác chưa được kiểm tra.

Migration đã được áp dụng bằng EF Core tới `PC01\SQLEXPRESS`, database Development `NEXORA`, sau khi truy vấn chỉ đọc xác minh database chưa tồn tại và đối chiếu cấu hình Web. SQL review script nằm ở [`INITIAL_FOUNDATION_REVIEW.sql`](./INITIAL_FOUNDATION_REVIEW.sql) và không được chạy trên server. Catalog xác nhận hiện có 27 bảng: 22 bảng nghiệp vụ, bốn bảng hỗ trợ Identity (`AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`) và `__EFMigrationsHistory`. Identity cũng thêm các cột xác thực tiêu chuẩn vào `users`, `roles`, `user_roles`; các phần mở rộng này không làm tăng số bảng nghiệp vụ.

## 3. Yêu cầu nền tảng từ khóa luận

### 3.1. Bốn vai trò

1. **Khách hàng (Customer):** khám phá và tư vấn sản phẩm, so sánh, mua hàng, theo dõi đơn và đánh giá.
2. **Nhân viên bán hàng (Sales):** kiểm tra, xác nhận và xử lý đơn; chuyển đơn cho kho.
3. **Nhân viên kho (Warehouse):** kiểm tra tồn, chuẩn bị và xuất hàng, ghi nhận giao dịch kho.
4. **Quản trị viên (Admin):** quản lý tài khoản, phân quyền, danh mục, sản phẩm, quy tắc tư vấn, khuyến mại và thống kê.

### 3.2. Tư vấn sản phẩm bằng ngôn ngữ tự nhiên là bắt buộc

FR08–FR10 và UC-C08 xác định tư vấn bằng câu tiếng Việt là chức năng bắt buộc. Luồng phải nhận câu mô tả, trích xuất và chuẩn hóa danh mục/ngân sách/mục đích/ưu tiên, hỏi thêm khi thiếu thông tin cần thiết, lọc điều kiện cứng trước khi xếp hạng, rồi giải thích kết quả bằng dữ liệu thông số thực tế. Khi không nhận diện được yêu cầu hoặc không có sản phẩm thỏa điều kiện, hệ thống phải hỏi lại/báo không có kết quả thay vì tự tạo tiêu chí hay sản phẩm.

Khóa luận cho phép hiện thực phân tích câu ban đầu bằng từ điển tiếng Việt, từ đồng nghĩa, biểu thức quy tắc và chuẩn hóa đơn vị. Mô hình/API bên ngoài là lựa chọn hỗ trợ phân tích, không phải điều kiện để chức năng được xem là sử dụng được. Dù dùng parser nào, bước lọc, xếp hạng và giải thích phải dựa trên dữ liệu sản phẩm cùng quy tắc đã cấu hình. Phạm vi nghiệm thu ban đầu ưu tiên laptop, điện thoại và máy tính bảng.

### 3.3. Hai mươi hai bảng dữ liệu chính thức

| Nhóm | Bảng |
|---|---|
| Người dùng và phân quyền | `roles`, `users`, `user_roles` |
| Danh mục và sản phẩm | `categories`, `brands`, `products`, `product_images` |
| Thông số kỹ thuật động | `specifications`, `category_specifications`, `product_specifications` |
| Giỏ hàng và đơn hàng | `carts`, `cart_items`, `orders`, `order_details` |
| Kho | `inventories`, `inventory_transactions` |
| Tương tác | `favorites`, `reviews` |
| Khuyến mại | `promotions`, `promotion_products` |
| Tư vấn sản phẩm | `usage_needs`, `recommendation_rules` |

Tổng cộng: **22 bảng nghiệp vụ**. Thiết kế dữ liệu trong Chương 2 cũng yêu cầu lưu snapshot giá tại `order_details.unit_price`, tính doanh thu theo đơn hoàn thành và `orders.completed_at`, và cập nhật tồn kho cùng giao dịch kho trong một transaction. Phase 1 đã tạo Fluent API, migration và áp dụng migration đã duyệt; catalog xác nhận đủ 22 bảng nghiệp vụ.

## 4. Kiến trúc đề xuất

Đề xuất **modular monolith** theo hướng phân lớp: phù hợp quy mô khóa luận, chạy như một ứng dụng ASP.NET Core MVC, đồng thời giữ rõ ranh giới giữa giao diện, nghiệp vụ và lưu trữ.

| Thành phần đề xuất | Trách nhiệm |
|---|---|
| `NEXORA.Web` | MVC controllers, Razor Views, Areas theo Customer/Sales/Warehouse/Admin, xác thực request, phân quyền, view models và giao diện Bootstrap/JavaScript. |
| `NEXORA.Application` | Use Case handlers/services, DTO, validation, điều phối nghiệp vụ cho Catalog, Consultation, Cart/Order, Inventory, Administration và Reporting. |
| `NEXORA.Domain` | Thực thể, trạng thái, quy tắc bất biến và logic miền độc lập với MVC/EF Core. |
| `NEXORA.Infrastructure` | EF Core `DbContext`, cấu hình entity/quan hệ/chỉ mục, truy vấn SQL Server, transaction, lưu trữ và tích hợp ngoài nếu sau này cần. |
| `NEXORA.Tests` | Unit test cho quy tắc miền/parser/xếp hạng; integration test cho EF Core, SQL Server và luồng MVC/authorization. Phase 1 có 27 automated tests; ngoài ra đã kiểm tra catalog SQL Server và chạy smoke test HTTP cho xác thực/phân quyền. Kiểm thử nghiệp vụ xuyên module và cạnh tranh kho sẽ bổ sung ở các Phase sau. |

**Quy tắc phụ thuộc:** Web gọi Application; Application dùng Domain và các abstraction cần thiết; Infrastructure hiện thực lưu trữ/tích hợp cho Application. Controller không chứa thuật toán tư vấn, quy tắc chuyển trạng thái đơn hoặc thao tác tồn kho nhiều bước.

**Bảo mật và vai trò:** ASP.NET Core Identity dùng cookie và ánh xạ `AppUser`, `AppRole`, `AppUserRole` lần lượt vào ba bảng nghiệp vụ `users`, `roles`, `user_roles`. Các cột Identity như `user_name`, `normalized_user_name`, `normalized_email`, `security_stamp`, `lockout_end` và `access_failed_count` được lưu trên hai bảng người dùng/vai trò; mật khẩu chỉ lưu trong `password_hash`. Identity hỗ trợ claim, external login và token bằng bốn bảng kỹ thuật riêng; không tính chúng vào 22 bảng. Bốn role được tạo idempotently khi ứng dụng khởi động sau khi schema đã được áp dụng. Không có tài khoản Admin hoặc mật khẩu mặc định.

**Email khôi phục mật khẩu:** `IEmailSender` nằm trong Application. Development dùng hộp thư giả lập chỉ trong bộ nhớ, truy cập tại `/dev/mailbox`; production chưa có provider gửi thư và báo lỗi cấu hình nếu gọi reset. Token chỉ nằm trong email/reset flow, không được ghi vào log.

**Giữ hàng đồng thời:** `Inventory` có `Reserve`, `ReleaseReservation`, `ExportReserved`, `AdjustStock` cùng các kiểm tra bất biến. Cột SQL Server `row_version` là concurrency token; xung đột cập nhật khiến EF Core báo `DbUpdateConcurrencyException`, không cho hai request ghi đè cùng phiên bản tồn. Luồng tạo Order/OrderDetail PENDING và giữ hàng trong cùng transaction sẽ được gọi từ service đặt hàng ở Phase 4; Phase 1 chưa hiện thực luồng đặt hàng.

**Tư vấn:** cô lập parser tiếng Việt, chuẩn hóa hồ sơ nhu cầu, bộ lọc cứng, scorer xác định và bộ tạo lời giải thích trong module `Consultation`. Dùng các lớp miền `UsageNeed`/`RecommendationRule` và bảng tương ứng. Parser không được tự quyết định kết quả xếp hạng.

**Giao diện:** Razor Views và Areas giữ chung một ứng dụng; danh mục, lọc, so sánh, tư vấn, giỏ/đơn và dashboard được tách theo module nghiệp vụ. Giao diện responsive theo yêu cầu NFR05.

## 5. Kế hoạch theo Phase và tiêu chí hoàn tất

Các tiêu chí dưới đây là đề xuất nghiệm thu. Kết quả Phase 1 được ghi tại chính phase đó; tiêu chí của các phase tương lai chưa được kiểm thử.

### Phase 0 – Audit & Planning (đã hoàn tất)

**Công việc:** kiểm tra workspace, đọc Chương 2 và các phần phạm vi liên quan, kiểm tra SDK/Git/EF CLI/SQL Server, lập kế hoạch và ma trận truy vết.

**Tiêu chí hoàn tất:** xác nhận các số lượng FR/NFR/BR/actor/UC/bảng; khẳng định UC-C08 bắt buộc; ghi rõ giới hạn audit môi trường; hai tài liệu kế hoạch được tạo mà không sửa DOCX gốc. Phase 0 không tạo database, migration hay source code.

### Phase 1 – Nền tảng solution, xác thực và phân quyền (đã hoàn tất)

**Đã thực hiện:** tạo `NEXORA.sln`; tạo Web, Application, Domain, Infrastructure và Tests; cấu hình dependency injection, EF Core SQL Server, 22 bảng nghiệp vụ/Fluent configurations và ASP.NET Core Identity; xây dựng register/login/logout/password reset link, email abstraction/Development mailbox, role authorization, NEXORA Razor layout với Bootstrap 5.3.8, logging không chứa mật khẩu/token, migration `20261015173046_InitialFoundation` và SQL review script `docs/INITIAL_FOUNDATION_REVIEW.sql`. Sau đợt rà soát schema, migration đầu tiên đã được tạo lại khi database chưa tồn tại; sau khi chủ dự án duyệt đích, migration đã được áp dụng như ghi nhận dưới đây. Báo cáo chi tiết tại [`DATABASE_REVIEW.md`](./DATABASE_REVIEW.md).

**Kết quả xác minh sau rà soát và áp dụng:** `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln` thành công với 0 warning/0 error; `dotnet test NEXORA.sln` đạt 27/27. Test bao gồm bất biến kho, rowversion concurrency token, Identity, FK/CHECK/index, công thức dòng đơn và Domain policy. `dotnet ef migrations has-pending-model-changes` xác nhận model không lệch migration. Migration được áp dụng trên SQL Server Express; truy vấn catalog xác nhận 27 bảng, 36 FK, constraints/index trọng yếu và cột yêu cầu. Website được khởi động tại `http://127.0.0.1:5047/`; trang chủ trả HTTP 200, trang đăng ký/đăng nhập, đăng ký Customer thử nghiệm, luồng reset qua Development mailbox, đăng nhập/đăng xuất và authorization HTTP đã được kiểm tra. RoleSeeder tạo đủ Customer/Sales/Warehouse/Admin; không có tài khoản nào được gán Admin. Đã tạo một tài khoản Customer thử nghiệm. Kiểm thử cạnh tranh nhiều SQL connection vẫn để Phase 4–5.

**Database đã được chủ dự án duyệt và cập nhật:** `PC01\SQLEXPRESS`, database `NEXORA`. Trước cập nhật, truy vấn chỉ đọc trên `master` xác nhận database chưa tồn tại và cấu hình Web `.\SQLEXPRESS` phân giải về hostname `PC01`; sau đó EF Core áp dụng đúng `20261015173046_InitialFoundation`. Database hiện có 27 bảng: 22 bảng nghiệp vụ, bốn bảng kỹ thuật Identity và `__EFMigrationsHistory`. File [`INITIAL_FOUNDATION_REVIEW.sql`](./INITIAL_FOUNDATION_REVIEW.sql) được giữ làm artifact review, không chạy trực tiếp.

### Phase 2 – Danh mục, sản phẩm, thông số động, tìm kiếm/lọc/so sánh (đã nghiệm thu và commit local)

**Công việc:** hiện thực nhóm bảng danh mục/sản phẩm/thông số; quản lý ảnh; trang liệt kê và chi tiết; tìm kiếm; lọc theo cấu hình thông số của danh mục; so sánh 2–3 sản phẩm theo UC-C09.

#### 2.1 – Git checkpoint và cấp quyền Admin Development

**Đã thực hiện:** khởi tạo repository Git cục bộ, mở rộng `.gitignore` để loại `bin/`, `obj/`, cấu hình Development chứa connection string, secrets, `.env`, khóa chứng thư và ảnh upload; đã kiểm tra các file bị loại không nằm trong staging. Chủ dự án đã cấu hình danh tính Git local; sau khi kiểm tra `user.name`/`user.email`, rà soát file staged và quét dấu hiệu bí mật, checkpoint Phase 1 đã được commit thành `fcd1153` (`chore: checkpoint Phase 1 foundation`). File cấu hình Development không được commit.

Đã thêm `--provision-admin` chạy riêng qua console tương tác, chỉ cho phép ở Development; công cụ tra cứu tài khoản đang hoạt động có sẵn, yêu cầu xác nhận `GRANT ADMIN <ID>`, không nhận email qua tham số lệnh, không có endpoint HTTP và không ghi email/mật khẩu/token vào log. Tại thời điểm hoàn tất phần 2.1, chưa gán role Admin cho tài khoản nào. Sau đó, theo xác nhận riêng của chủ dự án ở đợt nghiệm thu 2.8, tài khoản ID 2 đã được cấp Admin bằng command này. Hướng dẫn thao tác nằm ở [`DEVELOPMENT_ADMIN_PROVISIONING.md`](./DEVELOPMENT_ADMIN_PROVISIONING.md). Kiểm thử parser command đạt; lời gọi console với tài khoản không tồn tại không thực hiện thay đổi.

**Kiểm tra tại thời điểm hoàn tất phần 2.1:** `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln --no-restore` thành công với 0 warning/0 error sau khi dừng tiến trình website cũ đang khóa executable; `dotnet test NEXORA.sln --no-build --no-restore` đạt 30/30. Lệnh provisioning console với địa chỉ thử nghiệm không tồn tại trả về không có thay đổi; ở thời điểm đó chưa cấp quyền Admin cho tài khoản nào.

#### 2.2 – Category & Brand (đã hoàn tất)

Đã bổ sung Application contract và Infrastructure service truy vấn/tạo/sửa/tìm kiếm/xóa an toàn cho danh mục và thương hiệu. Danh mục hỗ trợ phân cấp, kiểm tra tên trùng cùng cấp, không cho vòng lặp cha-con; khi còn sản phẩm hoặc danh mục con thì chuyển sang ngừng sử dụng, nếu không còn liên kết thì mới xóa. Thương hiệu kiểm tra tên trùng và chỉ nhận logo URL HTTPS hoặc đường dẫn nội bộ; thương hiệu còn sản phẩm được ngừng sử dụng. Hai Admin Area controller đều yêu cầu role `Admin`, mọi lệnh ghi dùng POST và antiforgery; Razor có validation phía client/server, tìm kiếm, trạng thái rỗng và thông báo kết quả. Layout có liên kết danh mục/thương hiệu chỉ cho Admin.

Phạm vi 10 nhóm danh mục lấy từ tài liệu khóa luận sẽ được seed idempotent ở 2.7, không seed nhãn hoặc thông số sản phẩm ở giai đoạn này. Chưa thay đổi schema hoặc dữ liệu SQL Server. Test authorization cho controller và antiforgery đã bổ sung. `dotnet build NEXORA.sln --no-restore` thành công (0 warning/0 error); `dotnet test NEXORA.sln --no-build --no-restore` đạt 34/34.

#### 2.3 – Product & Product Image (đã hoàn tất)

Đã thêm quy tắc Domain và service Application/Infrastructure cho tạo, sửa, tìm kiếm, lọc và phân trang sản phẩm. Giá dùng `decimal`, được kiểm tra trong giới hạn `decimal(18,2)` và tối đa hai chữ số thập phân; trạng thái chỉ nhận `ACTIVE`/`INACTIVE`; sản phẩm chỉ trỏ tới danh mục/thương hiệu đang hoạt động (hoặc giữ liên kết hiện tại khi sửa sản phẩm cũ). Không xóa cứng sản phẩm; thao tác ngừng kinh doanh giữ toàn bộ quan hệ nghiệp vụ.

Ảnh hỗ trợ một ảnh đại diện và tối đa tám ảnh chi tiết mỗi lần tải. Hệ thống chỉ nhận JPEG/PNG/WebP, kiểm tra chữ ký byte thực tế cùng phần mở rộng, giới hạn 5 MB/ảnh, đổi tên GUID và lưu URL tương đối tại `wwwroot/uploads/products`. Thay ảnh đại diện giữ ảnh trước làm ảnh chi tiết; xóa ảnh chỉ nhắm file GUID trong thư mục upload NEXORA. Có preview phía trình duyệt; đã thêm `UseStaticFiles` để phục vụ file ảnh phát sinh sau khi ứng dụng khởi động. Chưa đổi schema hoặc ghi dữ liệu lên SQL Server.

Đã thêm Admin CRUD/list/detail, tìm kiếm theo tên/mã, lọc danh mục/thương hiệu/trạng thái, phân trang server-side và endpoint POST chống CSRF. Build 0 warning/0 error, test đạt 49/49 ở thời điểm hoàn tất; test kiểm tra validation giá/trạng thái, chữ ký và phần mở rộng ảnh, tên file được sinh an toàn và thao tác lưu/xóa file upload.

#### 2.4 – Specification động (đã hoàn tất)

Đã xây dựng quản lý Specification dùng đúng các bảng `specifications`, `category_specifications`, `product_specifications`; không thêm cột cố định vào Product, không tạo bảng/cột/schema mới. DataType được giới hạn theo hợp đồng hiện có: `NUMBER`, `TEXT`, `ENUM`, `BOOLEAN`. Thông số có thể được gán theo danh mục với cờ lọc, bắt buộc và thứ tự. Gỡ gán bị từ chối khi còn giá trị sản phẩm hoặc quy tắc tư vấn; chuyển thông số sang kiểu khác bị từ chối khi đã có dữ liệu hoặc quy tắc. Trước khi đặt thuộc tính bắt buộc, kiểm tra sản phẩm đang kinh doanh đã có giá trị.

Biểu mẫu sản phẩm dựng từ cấu hình Category, xác thực toàn bộ Specification gửi lên thuộc danh mục hiện tại, từ chối ID trùng, enforce trường bắt buộc và giữ giá trị nếu một trường tùy chọn không được gửi. NUMBER parse theo invariant culture, chuẩn hóa `value`, ghi riêng `numeric_value` và `display_value` theo `decimal(18,4)`; BOOLEAN ghi `true`/`false` cùng nhãn hiển thị tiếng Việt; ENUM mở, gợi ý dựa trên giá trị đang có và không yêu cầu bảng lựa chọn riêng. Khi đổi Category, sản phẩm có giá trị thông số không tương thích bị chặn để tránh làm mất/tách dữ liệu. Chi tiết Admin hiển thị giá trị động từ DB.

Kiểm thử policy cho numeric invariant/range, enum mở, boolean, gán sai danh mục/ID trùng; model test xác nhận alternate key, unique index product-specification, index numeric và FK. `dotnet build` đạt 0 warning/0 error; `dotnet test` đạt 60/60. `dotnet ef migrations has-pending-model-changes` báo model không thay đổi. Không tạo migration mới và không ghi dữ liệu database trong phần 2.4.

#### 2.5 – Catalog giao diện khách hàng (đã hoàn tất)

Đã thay trang chủ tĩnh bằng trang chủ đọc danh mục/sản phẩm từ `ICustomerCatalogQueryService`; giới thiệu tư vấn ngôn ngữ tự nhiên đúng vai trò tính năng bắt buộc nhưng ghi rõ đang thuộc giai đoạn kế tiếp, không giả vờ đã có Recommendation Engine. Có danh sách sản phẩm, danh mục, kết quả tìm kiếm, lọc cơ bản, chi tiết sản phẩm, ảnh và bảng Specification động; trang không dùng product mock và có empty state. Header có tìm kiếm, navigation, footer; văn hóa request là `vi-VN`, giá hiển thị theo VNĐ, giao diện dùng Razor/Bootstrap 5 responsive.

Ảnh khách hàng được phục vụ bằng Static File Middleware vì thư mục upload phát sinh khi chạy không có trong static asset manifest. Build sạch 0 warning/0 error; test suite hiện đạt 63/63 sau khi bổ sung query/pagination/so sánh. Phần 2.5 chỉ đọc DB; không chạy migration.

#### 2.6 – Tìm kiếm, lọc động và so sánh UC-C09 (đã hoàn tất)

Tìm theo tên/mã; lọc category, brand, giá hiệu lực; sort tên/giá; phân trang giới hạn phía server. Khi chọn category, facet chỉ lấy CategorySpecification đang bật `IsFilterable`; NUMBER dùng Min/Max từ `numeric_value`, ENUM/BOOLEAN lấy option từ dữ liệu thực, TEXT nhận giá trị nhập. Mỗi điều kiện được ghép trên `IQueryable` để EF sinh SQL; specification không thuộc bộ lọc category bị bỏ qua. Truy vấn mẫu được compile bằng SQL Server provider và test xác nhận các điều kiện Category/Brand/Price/Numeric/Value cùng OFFSET xuất hiện trong SQL.

So sánh server nhận 2–3 mã sản phẩm, từ chối ID trùng, sản phẩm không còn kinh doanh và category khác nhau; dữ liệu ảnh/giá/thương hiệu và hợp các Specification hiện có được dựng trực tiếp từ DB. Hàng thiếu giá trị được ghi rõ, so sánh khác biệt được tô nền và có nút chỉ hiện hàng khác nhau. Selection UI hỗ trợ nhiều checkbox, chặn quá 3 và category khác khi JavaScript hoạt động; server vẫn kiểm tra lại. Browser đã chọn hai checkbox Laptop Alpha/Beta, submit form và xác nhận bảng so sánh hiển thị hai sản phẩm. Test policy cho giới hạn, category, missing values; pagination clamp. Build 0 warning/0 error; test 63/63 tại thời điểm hoàn tất phần 2.6.

#### 2.7 – Seed data, tích hợp và kiểm thử toàn bộ (đã hoàn tất)

Đã đối chiếu tài liệu khóa luận và seed đúng 10 Category: Điện thoại, Laptop, Máy tính bảng, Màn hình, Bàn phím, Chuột, Tai nghe, Đồng hồ thông minh, Máy ảnh, Phụ kiện công nghệ. Dữ liệu sản phẩm chỉ được tạo cho ba nhóm trọng tâm trong tài liệu (Điện thoại, Laptop, Máy tính bảng) và Phụ kiện công nghệ; sáu nhóm còn lại không có sản phẩm hoặc Specification giả định. Hai thương hiệu và 11 sản phẩm được gắn rõ nhãn demo; tên và mô tả ghi đây là dữ liệu mô phỏng, không đại diện thông số do nhà sản xuất xác nhận. Mô tả và phạm vi chi tiết nằm ở [`PHASE2_DEMO_DATA.md`](./PHASE2_DEMO_DATA.md).

`CatalogDemoSeeder` chỉ chạy khi ứng dụng ở Development, bổ sung theo khóa nhận diện, bỏ qua tên/slug trùng, không sửa hoặc xóa dữ liệu đang có, không tạo dữ liệu khi Production. Seed chạy trong execution strategy và transaction Serializable. Lần chạy đầu đã thêm 10 categories, 2 brands, 11 specifications, 18 category assignments, 11 products và 52 product specification values; lần khởi động xác nhận tiếp theo báo thêm 0 hàng ở tất cả nhóm và vẫn tìm thấy 11 sản phẩm demo. Không seed ảnh, tồn kho, giỏ hàng, đơn hàng, giao dịch kho, đánh giá, khuyến mại, hồ sơ nhu cầu hay quy tắc tư vấn.

**Kiểm chứng SQL Server Development (trạng thái sau nghiệm thu browser):** truy vấn chỉ đọc xác nhận server `PC01\\SQLEXPRESS`, database `NEXORA`, 27 bảng tổng cộng (22 nghiệp vụ, 4 bảng kỹ thuật Identity và `__EFMigrationsHistory`). Đếm được 10 categories, 2 brands, 11 products, 0 product images, 11 specifications, 18 category assignments, 52 product specification values (38 có `numeric_value`), 2 users với 3 gán role, 0 orders và 0 inventories. Bốn role tồn tại; user ID `2` đã được chủ dự án chỉ định và được gán role Admin bằng provisioning command Development; không tạo mật khẩu mặc định. User ID 1 vẫn là Customer; Customer QA ID 3 tạo để kiểm thử trong lượt này đã được dọn. `__EFMigrationsHistory` chỉ ghi `20261015173046_InitialFoundation`. Không chạy database update, migration hay thay đổi schema trong Phase 2. Dữ liệu test `P2ACCEPT` đã được dọn và các số lượng trên được xác nhận sau dọn.

**Kiểm chứng HTTP và trình duyệt:** website Development được chạy tại `http://127.0.0.1:5058`. Anonymous GET tới trang chủ, Catalog, đăng ký và đăng nhập trả HTTP 200; `/Admin/Products` chuyển hướng HTTP 302 tới trang đăng nhập. Phiên trình duyệt có quyền Admin đã thực hiện CRUD/validation cho Category, Brand, Product, Specification, ảnh và ProductSpecification editor; đã kiểm tra catalog, search, Category/Brand/price/dynamic-spec filter, details, empty state, so sánh qua thao tác chọn hai checkbox và submit, route 3 sản phẩm, cùng request 4 ID bị từ chối. Customer QA tạm chỉ có role Customer đã đăng nhập được, truy cập Catalog, bị từ chối tại `/Admin/Products`, sau đó đăng xuất; tài khoản tạm đã được dọn. Kết quả đầy đủ, được phân loại theo unit/controller, HTTP smoke và browser test, nằm trong [`PHASE2_ACCEPTANCE.md`](./PHASE2_ACCEPTANCE.md).

**Kiểm thử cuối Phase 2:** ngày 16/10/2026, `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln` thành công với 0 warning/0 error; `dotnet test NEXORA.sln` đạt 64/64. `dotnet ef migrations has-pending-model-changes --no-build --project src/NEXORA.Infrastructure --startup-project src/NEXORA.Web --context AppDbContext` xác nhận model không thay đổi kể từ InitialFoundation. Không có migration mới. Mục tiêu tìm kiếm/lọc 3 giây theo NFR01 chưa đo; không tuyên bố đạt hiệu năng.

**Kết quả nghiệm thu:** Phase 2 cung cấp nền catalog, quản trị danh mục/sản phẩm/Specification, ảnh tải lên, tìm kiếm/lọc/so sánh và seed Development. Kiểm thử chức năng, quyền, regression và secret scan đã đạt; commit local đã được tạo, không push. Mục tiêu tìm kiếm/lọc 3 giây theo NFR01 chưa đo. Tư vấn NLP là chức năng bắt buộc thuộc Phase 3 và chưa được triển khai trong Phase 2.

**Tiêu chí Phase 2:** unit/model/controller/query tests bao phủ policy Specification, tính toàn vẹn truy vấn, giới hạn so sánh; HTTP smoke xác nhận trang khách và anonymous redirect; browser test xác nhận CRUD quản trị, Catalog, compare checkbox và quyền Customer-only như ghi trong acceptance report. NFR01 chưa đo.

#### 2.8 – Nghiệm thu cuối Phase 2 (kiểm thử đạt, chờ checkpoint Git)

Ngày 16/10/2026 đã kiểm tra lại Git và xác nhận checkpoint `fcd1153` tồn tại. `.gitignore` loại `bin/`, `obj/`, `appsettings.Development.json`, `.env`, secrets, khóa chứng thư và media upload; secret scan trên 70 file text thuộc commit không thấy dấu hiệu password, token, key hoặc connection string nhạy cảm. Development config và upload path đã được kiểm tra bằng `git check-ignore`. Commit local `feat: complete Phase 2 product catalog` đã được tạo sau staged diff review; không push.

Đã xác minh cấu hình Development là `PC01\\SQLEXPRESS` / `NEXORA`, Windows Integrated Authentication. Chủ dự án xác nhận tài khoản Đỗ Duy Trường (ID `2`) là tài khoản đích. `DevelopmentAdminProvisioningCommand` đã chạy trong Development sau khi người vận hành đối chiếu tên/ID và nhập `GRANT ADMIN 2`; truy vấn chỉ đọc sau đó xác nhận user ID 2 thuộc role Admin. Provisioning command không đổi mật khẩu, không tạo tài khoản Admin hoặc mật khẩu mặc định. Các gán role Customer hiện có được giữ nguyên.

Nghiệm thu browser đã thực hiện với phiên đang đăng nhập Admin: dashboard, CRUD Category/Brand/Product/Specification, validation, upload ảnh đúng/sai/giới hạn, dynamic Specification, Catalog, search/filter/detail, compare checkbox chọn hai sản phẩm rồi submit thành công, compare route và empty state. Một Customer QA tạm ID 3 được đăng ký qua UI, đăng nhập, được phép vào Catalog, bị từ chối tại `/Admin/Products` và đăng xuất; truy vấn role chỉ đọc xác nhận chỉ có Customer. Bản ghi QA do lượt này tạo đã được dọn sau khi xác nhận không có dữ liệu phụ thuộc. Đã sửa lỗi Razor Tag Helpers trong Areas, parse Range theo invariant culture, khôi phục metadata khi Specification lỗi, chặn Product ACTIVE thiếu Specification bắt buộc và cập nhật nội dung hướng dẫn ảnh. Anonymous HTTP smoke cho `/`, `/Catalog`, `/account/register`, `/account/login` trả 200; `/Admin/Products` chuyển hướng 302. Xem [`PHASE2_ACCEPTANCE.md`](./PHASE2_ACCEPTANCE.md) để biết phạm vi từng loại test.

Sau sửa lỗi, `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln` thành công với 0 warning/0 error; `dotnet test NEXORA.sln` đạt 64/64. `dotnet ef migrations has-pending-model-changes --no-build --project src/NEXORA.Infrastructure --startup-project src/NEXORA.Web --context AppDbContext` xác nhận model không thay đổi kể từ InitialFoundation. Truy vấn chỉ đọc xác nhận 22 bảng nghiệp vụ, 4 bảng kỹ thuật Identity và bảng history; InitialFoundation vẫn là migration duy nhất. Dữ liệu `P2ACCEPT` tạo riêng cho browser test đã được dọn; không có migration/schema update.

Tài khoản ID 2 đã được cấp Admin theo xác nhận của chủ dự án. Browser test đã xác nhận compare checkbox/submit và Customer-only access denial bằng Customer QA tạm ID 3. Phase 2 đã đạt tiêu chí kiểm thử. Regression cuối: restore thành công, build 0 warning/0 error, test 64/64. Secret scan 70 file không phát hiện dấu hiệu bí mật; staged diff đã được rà soát và commit local `feat: complete Phase 2 product catalog` đã tạo. Không push.

### Phase 3 – Tư vấn lựa chọn sản phẩm bằng câu tiếng Việt (bắt buộc)

**Công việc:** cấu hình `usage_needs`/`recommendation_rules`; phân tích và chuẩn hóa câu tiếng Việt; làm rõ trường bắt buộc; lọc điều kiện cứng; chấm điểm có trọng số; xếp hạng ổn định; giải thích gợi ý theo thông số thật; liên kết sang trang chi tiết/so sánh.

**Tiêu chí kiểm thử đề xuất:** thực hiện đủ TC-NLP01–TC-NLP09 tại mục 2.10 khóa luận; đo độ chính xác trích xuất theo từng trường, khả năng xử lý câu trong phạm vi, mức thỏa điều kiện cứng, căn cứ của lời giải thích và thời gian phản hồi. Không có kết quả thì báo rõ; thiếu thông tin thì hỏi bổ sung; parser lỗi thì cho nhập lại/bộ lọc; cùng đầu vào, dữ liệu và quy tắc phải cho thứ hạng giống nhau (NFR09). Không có gợi ý nào vượt điều kiện cứng; mọi lý do truy được về ProductSpecification (NFR08). Mục tiêu tư vấn là 8 giây theo NFR01 trong môi trường thử nghiệm được ghi lại. Chỉ kết luận đạt sau khi chạy phần mềm thực tế.

### Phase 4 – Giỏ hàng, khuyến mại, đặt hàng và hủy đơn

**Công việc:** thực hiện giỏ hàng, kiểm tra giá/khuyến mại/tồn ngay trước khi đặt; ghi `Order`/`OrderDetail` với snapshot; xử lý trạng thái và hủy đơn theo BR01/BR03/BR10–BR12.

**Tiêu chí kiểm thử đề xuất:** giỏ rỗng/thông tin nhận hàng sai/thiếu tồn/giá thay đổi được xử lý theo luồng UC-C12; lỗi ghi bất kỳ bước nào rollback toàn bộ; đơn lưu đúng giá và giảm giá giao dịch, không đổi khi giá sản phẩm hiện hành đổi; khách chỉ tự hủy đơn Chờ xác nhận; khuyến mại chỉ áp dụng khi đang hoạt động trong thời hạn. Kiểm tra tổng dòng và tổng đơn theo công thức ở mục 2.9.

### Phase 5 – Nghiệp vụ Sales và Warehouse

**Công việc:** danh sách/tìm kiếm/chi tiết đơn; xác nhận đơn; chuyển sang kho; tiếp nhận và chuẩn bị; xuất kho; lịch sử giao dịch; điều chỉnh tồn có lý do; theo dõi vòng đời trạng thái.

**Tiêu chí kiểm thử đề xuất:** chỉ đơn Đã xác nhận chuyển kho (BR04); chỉ vai trò được cấp thực hiện chuyển trạng thái; đơn sai trạng thái hoặc thiếu hàng không được xuất; cập nhật tồn, tạo giao dịch OUT và đổi trạng thái cùng transaction; lỗi giữa chừng rollback (BR05); tồn khả dụng không âm và không cho đặt vượt lượng có thể bán (BR03). Kiểm thử các nhánh hủy/giải phóng hàng theo chính sách giữ hàng đã chốt.

### Phase 6 – Quản trị và báo cáo

**Công việc:** quản lý user/nhân viên/role; danh mục, thương hiệu, sản phẩm, ảnh, thông số; quy tắc tư vấn; khuyến mại, đánh giá, giá; dashboard và thống kê đơn hàng/doanh thu/sản phẩm/tồn kho.

**Tiêu chí kiểm thử đề xuất:** kiểm tra quyền Admin và validation dữ liệu; một khách không có favorite trùng; review chỉ gắn với đơn hoàn thành và giao dịch của người mua; cấu hình promotion tuân thời gian; cập nhật giá không đổi đơn cũ; doanh thu chỉ lấy đơn `COMPLETED`, theo `completed_at` và số tiền giao dịch đã chốt (BR13–BR14); hành động quan trọng có log theo NFR10.

### Phase 7 – Tích hợp, nghiệm thu và bàn giao

**Công việc:** kiểm thử xuyên vai trò từ tư vấn/catalog đến đặt hàng, Sales, Warehouse, hoàn thành đơn, review và báo cáo; chạy kiểm thử bảo mật, tính toàn vẹn, khả dụng, hiệu năng; chuẩn bị hướng dẫn chạy và minh chứng khóa luận.

**Tiêu chí kiểm thử đề xuất:** toàn bộ FR có bằng chứng test hoặc nghiệm thu được liên kết trong ma trận; không có lỗi chặn luồng chính; 10 NFR được đánh giá theo cách đo đã thống nhất; chạy lại TC-NLP01–09 và lưu phiên bản/phạm vi dữ liệu/kết quả thực tế; migrations tạo đúng 22 bảng chính thức cộng các cột đã chốt; triển khai thử trên môi trường đích và xác nhận cấu hình kết nối bảo mật.

## 6. Nguyên tắc kiểm thử xuyên suốt

- Kiểm thử đơn vị cho quy tắc miền, parser/chuẩn hóa, bộ lọc, công thức điểm và giải thích.
- Kiểm thử tích hợp cho ràng buộc SQL Server, transaction EF Core, snapshot giá, khóa duy nhất, chỉ mục và truy vấn lọc.
- Kiểm thử authorization cho từng role; không xem việc ẩn nút giao diện là kiểm soát quyền.
- Kiểm thử end-to-end cho các Use Case trọng tâm: UC-C08, UC-C12, UC-C14, UC-S04, UC-W06, UC-A12.
- Ghi ngày chạy, phiên bản, cấu hình và kích thước dữ liệu cho mọi kết quả hiệu năng/tư vấn. Phân biệt tiêu chí dự kiến với số đo thực tế.

## 7. Nguyên tắc bảo toàn phạm vi dữ liệu

- Bảo đảm 22 bảng chính thức đúng danh sách ở mục 3.3; không thêm `product_price_histories` vào schema chính thức nếu chưa có quyết định phạm vi.
- Giữ các trường giao dịch, đặc biệt `orders.completed_at`, snapshot giá và giá trị kho cần thiết theo Chương 2.
- Dùng ràng buộc FK/unique/check và transaction cho nghiệp vụ có nhiều thay đổi dữ liệu.
- Ghi nhận mọi sai khác giữa tài liệu và quyết định triển khai trong tài liệu thiết kế trước khi hiện thực hóa.

## 8. Điểm cần chốt trước các giai đoạn liên quan

Các mục sau cần quyết định tại giai đoạn thiết kế phù hợp vì tài liệu không chỉ rõ toàn bộ chi tiết triển khai:

1. **Provider gửi email production:** abstraction đã có, nhưng chưa cấu hình dịch vụ gửi thư thật.
2. **Tài khoản Admin ban đầu:** Development đã có quy trình provisioning có xác nhận và tài khoản ID 2 đã được chủ dự án cho phép cấp role Admin. Không tạo tài khoản hoặc mật khẩu mặc định. Trước Production cần xác định quy trình bootstrap đặc quyền riêng theo vận hành môi trường.
3. **Transaction nghiệp vụ đặt hàng:** quy tắc miền và rowversion đã có; SQL Server retry cho lỗi tạm thời được bật (tối đa 3 lần, delay 5 giây). Phase 4 phải chạy transaction bên trong execution strategy, ghi Order, OrderDetail và cập nhật ReservedQuantity nguyên tử; giải phóng reservation khi hủy, giảm tồn/ghi giao dịch khi xuất. Khi rowversion xung đột phải tải lại và kiểm tra tồn trước retry có giới hạn; cần kiểm thử cạnh tranh trên SQL Server sau khi database được duyệt.
4. **Log NFR10:** sử dụng `ILogger` và không log bí mật; nơi lưu, thời gian lưu và chính sách truy cập log chưa được quy định.
5. **Parser tư vấn:** tập từ đồng nghĩa, cách xử lý tiền/đơn vị, ngưỡng xác nhận trường mơ hồ và bộ dữ liệu nghiệm thu phải được định nghĩa, không suy ra độ chính xác khi chưa đo.

