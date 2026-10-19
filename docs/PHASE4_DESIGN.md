# Thiết kế Phase 4 NEXORA

## Mục tiêu và phạm vi

Phase 4 triển khai UC-C11–UC-C14 cho giỏ hàng, áp dụng khuyến mại, checkout, theo dõi đơn và tự hủy đơn đang `PENDING`. Thiết kế tiếp tục dùng ASP.NET Core MVC trong Modular Monolith, không triển khai thanh toán online hoặc nghiệp vụ Sales/Warehouse đầy đủ. Migration `20261016133620_AddCheckoutRequestId` đã được chủ dự án duyệt và áp dụng lên `NEXORA_IntegrationTests`, sau đó lên Development `NEXORA` theo quy trình backup/preflight đã duyệt.

Nguồn yêu cầu là FR13–FR16, BR01, BR03, BR10–BR12 và Chương 2 của khóa luận. Hệ thống hiện có 22 bảng nghiệp vụ, 4 bảng kỹ thuật Identity và `__EFMigrationsHistory`; đơn hàng đã có snapshot tên sản phẩm, giá, giảm giá, thành tiền và `orders.completed_at`.

## Phân lớp và mô-đun

- `NEXORA.Web`: Customer MVC controllers, model binding, antiforgery, authorization, Razor Views và phản hồi lỗi thân thiện.
- `NEXORA.Application`: contracts/use case cho Cart, Pricing, Checkout, Order Query và Cancellation.
- `NEXORA.Domain`: trạng thái đơn, quy tắc giá và bất biến tồn kho.
- `NEXORA.Infrastructure`: truy vấn EF Core, transaction SQL Server, reservation, `InventoryTransaction` và retry/concurrency.
- `NEXORA.Tests`: unit tests, HTTP/controller integration và SQL Server integration trên database riêng `NEXORA_IntegrationTests`.

Controllers chỉ điều phối use case; `UserId` luôn lấy từ principal hiện tại. Không thêm quyền xem đơn của Customer khác cho Admin.

## Giỏ hàng và giá

Giỏ của người dùng được tải theo `carts.user_id`; dòng hàng được định danh duy nhất bởi `(cart_id, product_id)`. Thêm lại sản phẩm cộng số lượng; cập nhật và xóa chỉ thao tác dòng thuộc giỏ của principal. Product phải còn kinh doanh; quantity là số nguyên dương và không vượt `QuantityInStock - ReservedQuantity` tại thời điểm kiểm tra. Giỏ không giữ hàng.

Giá bán hiện hành được lấy từ database, dùng `DiscountPrice` nếu có, nếu không dùng `Price`. Thành tiền tạm tính là `unit price × quantity`, tính ở server với độ chính xác tiền tệ hai chữ số. Không nhận giá/tổng tiền từ request. Checkout tải lại toàn bộ dữ liệu và so sánh báo giá đã phát hành với giá hiện tại; khi thay đổi, yêu cầu khách xác nhận lại.

## Khuyến mại

Promotion chỉ hợp lệ khi `IsActive`, thời gian UTC của server nằm trong `[StartDate, EndDate]`, sản phẩm có trong `promotion_products`, loại và giá trị giảm hợp lệ. Giảm giá không vượt thành tiền dòng; số tiền giảm được lưu trong `order_details.discount_amount` và không tính lại cho đơn cũ. Mỗi dòng chọn đúng một offer cho mức giảm lớn nhất; không cộng dồn. `PERCENTAGE` áp dụng trên tổng tiền dòng trước khuyến mại. `FIXED_AMOUNT` áp dụng một lần cho dòng, không nhân theo số lượng. Hòa điểm được phân định theo ID promotion tăng dần để thứ hạng giá xác định. Tiền được làm tròn 2 chữ số với `MidpointRounding.AwayFromZero`.

Quản trị khuyến mại tối thiểu cho phép tạo/sửa, gán sản phẩm và bật/tắt. Không xóa cứng chương trình đã dùng; tắt để giữ lịch sử. Phần trăm ngoài [0,100], số tiền âm, thời gian không hợp lệ hoặc sản phẩm đã ngừng kinh doanh bị từ chối ở service; schema vẫn giữ CHECK hiện có.

## Trạng thái đơn hàng và reservation

Tạo đơn ở `PENDING`. Khách chỉ tự hủy khi đơn còn `PENDING`, theo BR01. Không xóa `Order`/`OrderDetail`; các trạng thái `CONFIRMED`, `PREPARING`, `EXPORTED`, `SHIPPING`, `COMPLETED`, `CANCELLED` không thể tự hủy từ Customer flow.

Reservation lifecycle:

1. Thêm sản phẩm vào giỏ chỉ đọc lượng có thể bán, không đổi tồn.
2. Checkout trong một transaction kiểm tra lại product/status/giá/promotion/tồn.
3. Tạo `Order`, `OrderDetail` snapshot; tăng `ReservedQuantity`; ghi `inventory_transactions` loại `RESERVE`; xóa dòng giỏ.
4. Hủy đơn trong một transaction đổi trạng thái sang `CANCELLED`, giảm reservation và ghi loại `RELEASE`.
5. Phase 5 sẽ xuất kho bằng cách giảm cả tồn thực tế lẫn reservation, ghi `OUT` và đổi trạng thái nguyên tử.

## Transaction, concurrency và idempotency

`inventories.row_version` đã được EF Core ánh xạ bằng `IsRowVersion().IsConcurrencyToken()`; CHECK hiện có giữ `quantity_in_stock >= 0`, `reserved_quantity >= 0` và `reserved_quantity <= quantity_in_stock`. Reservation phải được ghi cùng transaction với Order/OrderDetail/log kho. Checkout dùng transaction `SERIALIZABLE` để khóa giỏ trong khi xác nhận và rowversion cho tồn. Khi nhận `DbUpdateConcurrencyException`, rollback và retry tối đa 3 lần bằng context đã clear/reload; mỗi lượt kiểm tra lại giỏ/tồn. SQL Server execution strategy (3 lần, delay tối đa 5 giây) bao bọc transaction; MARS bị tắt trong EF options để bật savepoint rollback. Thiếu tồn/validation không retry. Deadlock 1205 bị EF bọc trong `DbUpdateException` được nhận diện riêng và retry giới hạn sau rollback. Với lỗi transient hoặc kết quả commit chưa rõ, cùng `CheckoutRequestId` được truy vấn; ứng dụng phân biệt `TransientFailure` với `CommitResultUnknown` để có thể gửi lại cùng request an toàn.

Migration `20261016133620_AddCheckoutRequestId` bổ sung nullable `orders.checkout_request_id` và unique filtered index `ux_orders_checkout_request_id`; không thêm bảng nghiệp vụ. Trước transaction, trong mỗi retry và sau lỗi ghi, tra request ID theo user. Cùng user nhận lại OrderId đã tạo; request ID thuộc user khác bị từ chối. Migration được áp dụng trên `NEXORA_IntegrationTests` và Development `NEXORA` sau xác nhận riêng; read-only verification ngày 2026-10-19 xác nhận migration history, cột và index trên cả hai database.

## Bảo mật và riêng tư

- Cart, checkout và đơn chỉ truy cập theo user ID của phiên đăng nhập; không nhận user/order owner ID từ client.
- Mọi request thay đổi dữ liệu dùng POST, `[Authorize(Roles = Customer)]` và antiforgery.
- Giá, discount, trạng thái Order, chủ đơn và tồn kho đều do server tải/tính.
- Không ghi password, cookie, token, địa chỉ giao hàng hoặc thông tin nhạy cảm vào `ILogger`.
- Customer chỉ xem/hủy đơn của chính mình; Admin không có quyền xem đơn khách khác qua route Customer.

## Seed và dữ liệu kiểm thử

Trước seed, truy vấn chỉ đọc xác nhận 11 sản phẩm đang kinh doanh và 0 inventory trong NEXORA. `--seed-development-inventory` chỉ chạy ở Development, xác thực target `PC01\SQLEXPRESS / NEXORA`, và chỉ tạo inventory còn thiếu với 10 đơn vị mô phỏng; chạy lần thứ hai thêm 0 dòng. Sau seed, NEXORA có 11 inventory rows, tổng physical stock 110, reserved 0. SQL integration tests xác nhận connection target là `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; seeder test điền các bản ghi thiếu ở test DB, không reset inventory hiện có. Checkout/cancel/concurrency/idempotency SQL integration test đã chạy trên test DB riêng và cleanup các bản ghi QA.

## Quyết định đã chốt và việc còn chờ

1. Đã chốt: một promotion tốt nhất trên mỗi dòng, không cộng dồn; fixed amount trừ một lần mỗi dòng.
2. Migration `20261016133620_AddCheckoutRequestId` được duyệt và áp dụng trên cả `NEXORA_IntegrationTests` và Development `NEXORA`; read-only verification xác nhận nullable GUID column và unique filtered index, không có bảng mới.
3. SQL transaction, idempotency, checkout song song khi chỉ còn hàng cho một đơn, snapshot giá, truy vấn đơn có kiểm soát chủ sở hữu và hủy lặp đã được kiểm thử trên test DB riêng.
4. Chủ dự án xác nhận Manual UAT trên trình duyệt 6/6 PASS ngày 2026-10-19 (Catalog/tồn, Cart, Checkout, Orders/hủy, Admin Promotions, responsive desktop/tablet/mobile). Đây là user-confirmed manual result; Codex không chạy browser automation hoặc tạo ảnh kiểm thử.

## Trạng thái nghiệm thu cuối — 2026-10-19

Phase 4 được đánh dấu **COMPLETED**. Automated regression đạt 143/143, HTTP E2E có 63 assertions PASS, SQL Server integration chạy trên `NEXORA_IntegrationTests`, và người dùng xác nhận Manual UAT 6/6 PASS. Fulfillment, Sales/Warehouse và xuất kho vẫn thuộc Phase 5.
