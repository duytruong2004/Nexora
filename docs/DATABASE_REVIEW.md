# Rà soát thiết kế cơ sở dữ liệu NEXORA

## Phạm vi và trạng thái

Rà soát này áp dụng cho mô hình EF Core và migration `InitialFoundation`. Tại thời điểm rà soát ban đầu database chưa được tạo; sau khi chủ dự án duyệt, migration `20261015173046_InitialFoundation` đã được EF Core áp dụng trên `PC01\SQLEXPRESS`, database `NEXORA`. File SQL review chỉ dùng để đọc/review, không được thực thi trực tiếp. Tài liệu khóa luận gốc không bị sửa.

Thiết kế tiếp tục giữ đúng **22 bảng nghiệp vụ chính thức**:

`roles`, `users`, `user_roles`, `categories`, `brands`, `products`, `product_images`, `specifications`, `category_specifications`, `product_specifications`, `carts`, `cart_items`, `orders`, `order_details`, `inventories`, `inventory_transactions`, `favorites`, `reviews`, `promotions`, `promotion_products`, `usage_needs`, `recommendation_rules`.

Migration định nghĩa thêm **4 bảng kỹ thuật ASP.NET Core Identity**: `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`. EF Core tạo `__EFMigrationsHistory`, là bảng metadata migration, không thuộc Identity hay 22 bảng nghiệp vụ. Script có 27 lệnh tạo bảng; catalog database sau áp dụng xác nhận đúng **27 bảng**: 22 nghiệp vụ + 4 Identity + 1 metadata. Bốn role `Customer`, `Sales`, `Warehouse`, `Admin` đã được RoleSeeder khởi tạo khi website chạy; không có user nào được gán role Admin mặc định.

## A. Ràng buộc bắt buộc ở schema đã sửa

### Quy tắc tư vấn và thông số động

- `recommendation_rules.operator` chỉ nhận `EQ`, `NE`, `GT`, `GTE`, `LT`, `LTE`, `CONTAINS`, được bảo vệ bằng CHECK constraint.
- `weight` là `decimal(9,4)` và có CHECK `weight >= 0`. Chương 2 quy định `wi ≥ 0`, chuẩn hóa điểm theo tổng trọng số và dùng thứ tự mặc định cấu hình khi tổng bằng 0; vì vậy trọng số 0 hợp lệ và không áp thêm cận trên nghiệp vụ tùy ý. Giới hạn trên vật lý của kiểu cột là `99.999,9999` theo cách viết Việt Nam, tương đương `99999.9999`.
- `category_specifications` có khóa duy nhất thay thế trên `(category_id, specification_id)`. `recommendation_rules` có FK ghép cùng cặp này, nên một quy tắc không thể gắn Specification chưa được khai báo cho Category đó.
- Thiết kế vẫn dùng `specifications` và `product_specifications` dạng động, không tạo cột/bảng riêng theo từng loại thiết bị. CHECK chỉ giới hạn mã toán tử; kiểu và ngưỡng được kiểm tra theo `Specification.DataType` ở Domain policy để có thể mở rộng kiểu dữ liệu mà không khóa cứng schema.

### Đánh giá

- Thêm `reviews.order_id` để tạo FK ghép `(order_id, user_id) → orders(id, user_id)`, bảo đảm người đánh giá là chủ đơn ở cấp database.
- `order_details` có khóa duy nhất ghép `(id, product_id, order_id)`. `reviews` có FK ghép `(order_detail_id, product_id, order_id)` tới khóa này, đảm bảo sản phẩm được đánh giá đúng là sản phẩm của dòng hàng thuộc đơn đó.
- Mỗi `order_detail_id` chỉ có tối đa một review; điểm đánh giá vẫn được giới hạn 1–5 bằng CHECK.
- Database không thể dùng CHECK/FK thông thường để xác minh trạng thái của bản ghi Order liên quan là `COMPLETED`; điều kiện trạng thái được kiểm tra ở policy/service (mục B) trước khi tạo review.

### Chi tiết đơn hàng và snapshot giá

- CHECK bảo đảm `quantity > 0`, `unit_price >= 0`, `discount_amount >= 0`, `total_price >= 0`.
- CHECK mới buộc `total_price = (unit_price × quantity) − discount_amount`; kết hợp các CHECK không âm, khoản giảm không thể lớn hơn thành tiền trước giảm.
- `unit_price`, `discount_amount`, `total_price` vẫn là các cột giao dịch riêng trên `order_details`, không phải computed column phụ thuộc `products.price`. Đổi giá sản phẩm hiện tại vì vậy không cập nhật snapshot của đơn đã tạo.
- Tổng `orders.total_amount = SUM(order_details.total_price)` là bất biến nhiều dòng/nhiều bảng, được xác minh trong Application Service và transaction, không thể biểu diễn bằng CHECK thông thường.

### Ảnh đại diện

- Thêm filtered unique index trên `product_images(product_id)` với filter `[is_primary] = 1`. Một sản phẩm có thể có nhiều ảnh phụ nhưng chỉ một ảnh có `is_primary = true`. Đây là filtered index SQL Server, không tạo bảng mới.

### Khuyến mại

- `discount_type` chỉ nhận hai mã biểu diễn hai hình thức trong tài liệu: `PERCENTAGE` hoặc `FIXED_AMOUNT`.
- CHECK giới hạn `discount_value >= 0`; CHECK riêng giới hạn giá trị `PERCENTAGE` không vượt quá 100. Kiểu `decimal(18,2)` hỗ trợ tiền cố định và tỷ lệ có hai chữ số thập phân.
- CHECK giữ `end_date >= start_date`. Tính còn hiệu lực tại thời điểm đặt hàng phụ thuộc đồng hồ hiện tại, nên phải kiểm tra trong Application Service, không thể bảo đảm bằng CHECK ổn định.

### Tồn kho và Identity

- `inventories.row_version` được cấu hình `.IsRowVersion()` và `.IsConcurrencyToken()`. Kiểu migration là SQL Server `rowversion`; model test xác minh concurrency token và `ValueGenerated.OnAddOrUpdate`.
- CHECK giữ `quantity_in_stock >= 0`, `reserved_quantity >= 0`, `reserved_quantity <= quantity_in_stock`; unique index bảo đảm một inventory cho mỗi product.
- ASP.NET Core Identity là hệ thống xác thực/phân quyền duy nhất. `AppUser`, `AppRole`, `AppUserRole` dùng khóa `int`, ánh xạ trực tiếp vào `users`, `roles`, `user_roles`; `user_roles` dùng khóa chính ghép hai khóa ngoại.
- `users.normalized_user_name` có unique index chuẩn của Identity; `users.normalized_email` có unique index riêng; `roles.normalized_name` có unique index chuẩn của Identity. Các chuỗi khóa tên/email được giới hạn 256 ký tự.
- Bốn bảng Identity hỗ trợ giữ kiểu khóa int và FK tới `users`/`roles`. Khóa của `AspNetUserLogins` dùng hai chuỗi tối đa 128 ký tự; khóa của `AspNetUserTokens` dùng `user_id` int và hai chuỗi tối đa 128 ký tự. Giới hạn này giữ composite key tối đa khoảng 512/516 byte, thấp hơn giới hạn khóa clustered 900 byte của SQL Server.
- `orders.completed_at` kiểu `datetime2 NULL` được giữ nguyên để ghi thời điểm hoàn thành và tính doanh thu theo yêu cầu Chương 2.

## B. Quy tắc cần thực hiện ở Domain/Application Service

1. **Rule tư vấn:** `RecommendationRulePolicy` hiện kiểm tra danh sách toán tử, `weight` trong miền `decimal(9,4)`, và kiểu/giá trị ngưỡng. Hợp đồng handler hiện tại là `NUMBER` (EQ/NE và so sánh số), `TEXT` (EQ/NE/CONTAINS), `ENUM` (EQ/NE), `BOOLEAN` (EQ/NE với true/false). Giá trị số dùng dấu chấm thập phân theo invariant format, không nhận dấu phẩy làm phân cách hàng nghìn. Mã `DataType` khác bị từ chối cho đến khi có handler và test. Các mã handler này là hợp đồng triển khai; khóa luận không quy định danh sách literal của `DataType` hay `operator`.
2. **Scoring:** điều kiện `IsRequired` phải lọc sản phẩm trước khi xếp hạng. Tính điểm mềm theo công thức Chương 2 `Σ(wi × si) / Σwi`; khi tổng trọng số bằng 0 dùng thứ tự mặc định cấu hình. Chuẩn hóa ngưỡng/đơn vị phải phù hợp từng Category. Không dùng `weight` để thay thế điều kiện bắt buộc.
3. **Review:** `ReviewEligibilityPolicy` kiểm tra chủ đơn, dòng hàng/sản phẩm liên quan và `Order.Status == COMPLETED`. Service phải tải các bản ghi từ database và tự lấy ID chuẩn từ order detail, không tin các ID do request gửi; kiểm tra lại trạng thái trước khi ghi. Composite FK là lớp bảo vệ toàn vẹn bổ sung, không thay authorization.
4. **Khuyến mại:** `Promotion.IsApplicableAt(utcNow)` kiểm tra `IsActive` và thời điểm nằm trong khoảng cấu hình. Khi checkout phải đánh giá tại thời điểm server UTC, rồi tính giảm giá theo loại khuyến mại và lưu số tiền giảm thực tế vào snapshot OrderDetail. Chính sách cộng dồn/ưu tiên nhiều promotion chưa được tài liệu chốt.
5. **Đơn hàng:** tại lần xác nhận cuối, kiểm tra giá/tồn/khuyến mại lại; chụp `Product.Name`, giá giao dịch và số tiền giảm vào OrderDetail; kiểm tra tổng Order bằng tổng các dòng. Thay đổi `Product.Price` sau đó không tính lại đơn cũ.
6. **Tồn kho và tính đồng thời:** service đặt hàng phải mở một transaction cho tạo Order PENDING, OrderDetail, tăng `ReservedQuantity` và các InventoryTransaction liên quan. Hủy đơn phải nhả phần đã giữ trong cùng transaction; xuất kho phải giảm `QuantityInStock` và `ReservedQuantity`, đổi trạng thái và ghi giao dịch kho trong cùng transaction.
7. **Retry/conflict:** EF SQL Server đã bật retry lỗi tạm thời tối đa 3 lần, delay tối đa 5 giây. Khi dùng transaction tường minh, toàn bộ delegate transaction phải chạy bên trong `Database.CreateExecutionStrategy().ExecuteAsync(...)`. Nếu EF ném `DbUpdateConcurrencyException`, tải lại rowversion/tồn kho, chạy lại kiểm tra nghiệp vụ; chỉ retry có giới hạn (tối đa 3 lần) khi lượng khả dụng vẫn đủ. Nếu không đủ hoặc vẫn xung đột, trả kết quả xung đột/thiếu hàng; không lặp lại mù quáng thao tác đã tạo đơn hay giao dịch kho. Cần kiểm thử race condition bằng nhiều context/connection SQL Server ở Phase 4–5; model/unit test hiện không thay thế được kiểm thử đó.

## C. Cải tiến tùy chọn hoặc để Phase sau

- Chỉ bổ sung toán tử, kiểu Specification, quy tắc đơn vị/ngưỡng hoặc giá trị đa lựa chọn khi đặc tả tư vấn và tập test đã xác định; mở rộng qua handler, không sinh cột theo từng loại sản phẩm.
- Chốt quy tắc kết hợp/nối tiếp nhiều promotion, thời điểm làm tròn và chính sách khi dữ liệu giá/khuyến mại đổi trong lúc checkout ở Phase 4.
- Đánh giá idempotency key cho yêu cầu đặt hàng nếu giao diện/API cần chống tạo đơn trùng khi client gửi lại sau lỗi mạng hoặc kết quả commit không rõ. Chưa thêm cột/bảng vì tài liệu hiện chưa yêu cầu khóa idempotency.
- Chạy integration/concurrency tests trên SQL Server Express sau khi chủ dự án duyệt database đích và migration; đo tải, deadlock, thời gian phục hồi và hành vi khi retry cạn.
- `product_price_histories` vẫn là tùy chọn ngoài phạm vi; không thêm bảng nghiệp vụ thứ 23.

## Kết quả kiểm tra artifact

- `dotnet restore NEXORA.sln`: thành công.
- `dotnet build NEXORA.sln`: thành công, 0 warning/0 error.
- `dotnet test NEXORA.sln`: 27 test đạt, 0 thất bại, 0 bỏ qua.
- `dotnet ef migrations has-pending-model-changes`: không có thay đổi model chưa nằm trong migration.
- Migration `20261015173046_InitialFoundation` đã được áp dụng bằng `dotnet ef database update` với `AppDbContext`, Infrastructure project và Web startup project. Catalog xác nhận migration ID trong `__EFMigrationsHistory`, 36 FK, các CHECK constraint và index trọng yếu. Không chạy SQL review script trực tiếp; script không chứa lệnh `CREATE DATABASE`.
- SQL Server Express thực tế tạo được schema với kiểu `int`, `nvarchar`, `datetime2`, `decimal`, `bit`, `rowversion`, CHECK/FK và filtered index. Đây là xác nhận áp dụng migration và đọc catalog; chưa phải kiểm thử tải/concurrency nhiều phiên.
