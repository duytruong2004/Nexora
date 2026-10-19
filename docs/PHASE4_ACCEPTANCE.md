# Nghiệm thu Phase 4 NEXORA

> **Cập nhật cuối — 2026-10-19:** Chủ dự án xác nhận đã trực tiếp kiểm thử thủ công và PASS cả 6 nhóm UI Phase 4. Đây là **user-confirmed manual UAT**, không phải browser automation do Codex thực hiện; không có ảnh chụp nào được tạo. Trạng thái BLOCKED ngày 2026-10-18 được giữ ở phần lịch sử.

## Trạng thái

**COMPLETED — Phase 4 đã đạt điều kiện nghiệm thu.** Cart, promotion pricing/Admin, checkout, My Orders, cancellation và reservation đã triển khai. Automated/HTTP/SQL tests đạt; chủ dự án xác nhận Manual UAT 6/6 PASS ngày 2026-10-19. Migration idempotency đã được duyệt và áp dụng trên `NEXORA_IntegrationTests`, sau đó áp dụng lên Development `NEXORA` sau backup và VERIFYONLY thành công. Không commit hoặc push trong lần chốt tài liệu này.

## Phạm vi và trạng thái

| Yêu cầu | Module/tiêu chí | Kết quả hiện tại |
|---|---|---|
| FR13 / UC-C11 | Giỏ theo principal; thêm, cộng số lượng, cập nhật, xóa; giá/tồn từ SQL | Unit + SQL integration đạt; Catalog và Cart user-confirmed PASS |
| BR10 | Promotion active, thời gian, product assignment, percentage/fixed amount, best offer, không cộng dồn, cap và rounding | Unit + SQL pricing đạt; SQL Admin create/toggle đạt; Admin Promotions user-confirmed PASS |
| FR14 / UC-C12 | Checkout quote, antiforgery, validation, idempotency, transaction, snapshot và reservation | SQL integration đạt cho concurrency, replay idempotent và reservation; Checkout user-confirmed PASS |
| BR03 | Không âm tồn, rowversion, retry/conflict, two-customer race | SQL race đạt: một đặt hàng thắng, request còn lại bị từ chối; tồn giữ không vượt tồn thực, deadlock được retry giới hạn; Catalog/tồn kho user-confirmed PASS |
| BR11–BR12 | OrderDetail snapshot và giá cũ không đổi khi Product thay giá | SQL regression giữ snapshot; Checkout/đơn user-confirmed PASS |
| FR15 / UC-C13 | Chỉ chủ đơn xem danh sách/chi tiết, dùng snapshot | SQL ownership đạt; danh sách/chi tiết user-confirmed PASS |
| FR16 / UC-C14 / BR01 | Chỉ tự hủy PENDING; release nguyên tử đúng một lần | SQL cancellation đạt; hủy đơn user-confirmed PASS |
| Phân quyền/antiforgery | Customer role cho Cart/Checkout/Orders; Admin role cho Promotions | Unit/HTTP route gate đạt; Customer/Admin flow thuộc nhóm Manual UAT được người dùng xác nhận PASS |

## Quyết định nghiệp vụ đã chốt

- Chọn một khuyến mại hợp lệ giảm nhiều nhất trên từng dòng; không cộng dồn.
- `FIXED_AMOUNT` giảm một lần trên mỗi dòng sản phẩm, không nhân số lượng.
- Phần trăm áp dụng trên thành tiền trước promotion; không để giảm vượt giá trị dòng.
- Làm tròn tiền đến 2 chữ số với `MidpointRounding.AwayFromZero`; tie-break promotion theo ID tăng dần.
- Checkout request idempotency dùng `CheckoutRequestId` nullable và unique filtered index; migration đã được áp dụng trên cả database test và Development `NEXORA`.

## Schema và dữ liệu

- Schema ban đầu giữ 22 bảng nghiệp vụ + 4 bảng kỹ thuật Identity; không thêm bảng. `orders.completed_at` và snapshot `order_details.product_name` vẫn được giữ.
- `NEXORA` trên `PC01\SQLEXPRESS` hiện ghi nhận `20261015173046_InitialFoundation` và `20261016133620_AddCheckoutRequestId`; database test cũng có hai migration này.
- `20261016133620_AddCheckoutRequestId` chỉ thêm `orders.checkout_request_id uniqueidentifier NULL` và unique filtered index `ux_orders_checkout_request_id`; tổng vẫn 27 bảng vật lý. Script preview tại [`PHASE4_PRODUCTION_MIGRATION_PREVIEW.sql`](./PHASE4_PRODUCTION_MIGRATION_PREVIEW.sql), review và nhật ký backup tại [`PHASE4_PRODUCTION_MIGRATION_REVIEW.md`](./PHASE4_PRODUCTION_MIGRATION_REVIEW.md).
- Full COPY_ONLY backup được SQL Server tạo và `RESTORE VERIFYONLY WITH CHECKSUM` xác nhận hợp lệ trước migration. Không có đơn hàng hoặc thay đổi tồn trong database chính.
- Seed Development chạy tường minh sau kiểm tra target `PC01\SQLEXPRESS / NEXORA`: thêm 11 dòng inventory, mỗi dòng 10 đơn vị mô phỏng; tổng tồn vật lý 110, reserved 0. Lần chạy lặp thêm 0 dòng.
- SQL test chỉ chạy khi xác minh `NEXORA_SQLSERVER_TEST_CONNECTION` trỏ tới `PC01\SQLEXPRESS / NEXORA_IntegrationTests`. Test seeder bổ sung stock còn thiếu trong database test. Không tạo/hủy đơn trên NEXORA chính.

## Test log thực tế — 2026-10-16

- `dotnet restore NEXORA.sln`: thành công; dependency hiện hành.
- `dotnet build NEXORA.sln --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test NEXORA.sln --no-build --no-restore`, với test connection đã kiểm tra đúng instance/database và `NEXORA_CHECKOUT_SQL_TESTS=1`: **117 passed, 0 failed, 0 skipped**.
- SQL workflow xác nhận hai khách cạnh tranh 1 đơn vị tồn: đúng một order thành công, khách còn lại nhận `InsufficientStock`; có một `RESERVE`; replay cùng request trả cùng OrderId; đổi giá sản phẩm không đổi snapshot; chủ đơn đọc được đơn, user khác không đọc/hủy; hủy PENDING chỉ giải phóng đúng một lần. Test còn xác nhận Admin Promotion service tạo và bật/tắt chương trình trên SQL.
- Lượt cạnh tranh đầu tiên phát hiện deadlock 1205 bị EF bọc trong lỗi lưu. `CheckoutService` được sửa để nhận diện lỗi SQL lồng nhau, retry transaction có giới hạn, xác minh idempotency sau lỗi và phân biệt `TransientFailure` với `CommitResultUnknown`; lần chạy sau đạt.
- SQL test chỉ chạy trên `NEXORA_IntegrationTests` và dọn toàn bộ QA records do test tạo. Truy vấn chỉ đọc sau test xác nhận số QA checkout users/products/orders/inventory transactions đều bằng 0.
- `dotnet ef migrations has-pending-model-changes`: “No changes have been made to the model since the last migration.”
- Read-only schema verification trên test DB: 27 bảng vật lý không đổi, 36 FK và 21 CHECK; migration history có InitialFoundation và AddCheckoutRequestId; cột nullable `uniqueidentifier`, index unique filtered đúng filter; `orders.completed_at` còn nguyên và `inventories.row_version` vẫn là `timestamp`. `usage_needs` và `recommendation_rules` tồn tại.
- Read-only hậu kiểm trên database chính: `NEXORA` có đúng hai migration; 27 bảng, 36 FK, 21 CHECK. Số lượng users 2, products 11, specifications 11, product_specifications 52, UsageNeeds 8, RecommendationRules 86, roles 4, orders 0 không đổi sau migration và HTTP smoke.
- HTTP smoke dùng Production-mode instance tạm trỏ tới `PC01\SQLEXPRESS / NEXORA` tại `http://127.0.0.1:5058`: Home, Catalog, Product Detail, Compare, tư vấn, Login/Register và CSS/Bootstrap trả 200; Cart, Checkout, Orders, Admin Promotions trả 302 tới Login khi anonymous. Dev seeders không chạy; instance đã dừng sau smoke. Có cảnh báo không cấu hình HTTPS port cho instance smoke HTTP.
- Browser chưa đăng nhập để không tự động hóa authentication; chưa thực hiện thao tác Cart/Checkout/Orders/Admin Promotions sau đăng nhập, cũng chưa kiểm tra viewport 1440×900, 768×1024, 390×844. Không tuyên bố các bước đó đạt.

## Lịch sử cổng nghiệm thu tại 2026-10-18 (đã đóng)

Các bước và trạng thái dưới đây phản ánh việc còn chờ tại thời điểm 2026-10-18; chúng được giữ làm lịch sử và đã được thay thế bởi xác nhận Manual UAT 6/6 PASS ngày 2026-10-19.

1. Đăng nhập bằng Customer QA, thêm một sản phẩm có stock vào giỏ; cộng/cập nhật/xóa và xác nhận chỉ giỏ của phiên đó thay đổi.
2. Xem giá và promotion, sửa giá hoặc promotion sau khi mở Checkout; xác nhận UI yêu cầu kiểm tra và xác nhận lại quote mới.
3. Đăng nhập Customer trong browser, thao tác Cart/Checkout và xem đơn; xác nhận không xem được đơn của user khác.
4. Đăng nhập Admin trong browser để tạo/tắt/bật Promotion; không dùng tài khoản production khi chạy test DB.
5. Kiểm tra desktop/tablet/mobile, form, nút, antiforgery và quyền Customer/Admin trong browser thật.

## Ghi nhận trước khi chốt

- Ở thời điểm 2026-10-18, chưa có đơn trong database chính và không tạo đơn thử nghiệm tại đó; kiểm thử hủy đơn thực tế được thực hiện trên database QA. Đây là ghi nhận lịch sử trước Manual UAT.
- Migration tiếp theo vẫn cần review/xác nhận riêng. Phase 4 hiện COMPLETED; chưa tạo commit hoặc push.

## E2E và hồi quy mới nhất — 2026-10-18

### Phạm vi an toàn đã xác minh

- Website được chạy tạm tại `http://127.0.0.1:5059` với `ASPNETCORE_ENVIRONMENT=Development` và `ConnectionStrings__DefaultConnection` được override trực tiếp bằng `NEXORA_SQLSERVER_TEST_CONNECTION`.
- Connection target được xác minh là `PC01\SQLEXPRESS / NEXORA_IntegrationTests`, Windows Integrated Security. Database ONLINE, migration history vẫn chỉ có `20261015173046_InitialFoundation` và `20261016133620_AddCheckoutRequestId`.
- Không kết nối hoặc ghi vào database `NEXORA` chính. Không chạy migration/database update.
- Trước khi chạy ứng dụng, 11 SKU demo đều có Inventory `QuantityInStock=10`, `ReservedQuantity=0`; không có QA user, cart, order, inventory transaction hay promotion.
- Khi khởi động trên test DB, Catalog/Consultation seeder báo thêm 0 dòng; Inventory seeder tự bỏ qua vì database đích không phải Development `NEXORA`. Tồn vật lý của 11 SKU demo không bị thay đổi.

### Kết quả HTTP E2E trên ứng dụng thật

Các request dùng cookie jar riêng theo tài khoản, Anti-Forgery token lấy từ Razor form và gửi tới Kestrel đang chạy; đây là kiểm thử HTTP, **không thực thi JavaScript hoặc thao tác trình duyệt**.

| Trường hợp | Trạng thái | Bằng chứng |
|---|---|---|
| Đăng ký Customer QA, đăng nhập/đăng xuất, hồ sơ | PASS | Đăng ký và đăng nhập qua endpoint MVC; hồ sơ trả đúng danh tính của phiên; logout cần token hợp lệ; đăng nhập lại vẫn còn Cart. Mật khẩu QA sinh ngẫu nhiên trong bộ nhớ, không in hoặc lưu. |
| Quyền Customer/Admin | PASS | Customer đọc được trang công khai và bị chuyển tới AccessDenied khi gọi `/Admin/Promotions`; tài khoản QA có role Admin mở được list/Create Promotion. Anonymous truy cập đơn hàng bị chuyển tới Login. |
| Cart — Add, tăng/giảm, xóa, empty state | PASS | Catalog Add trả item count từ server; gọi Add thiếu Anti-Forgery trả 400; product không tồn tại trả 404; Update `1→2→1` trả CartSnapshot; yêu cầu `11` với tồn khả dụng `10` trả 409 và snapshot giữ quantity `1`; Add lặp tăng cùng dòng; Remove trả empty snapshot và trang Cart hiển thị rỗng. |
| Promotion — validation, create, edit, toggle | PASS | Admin tạo PERCENTAGE 10% và FIXED_AMOUNT 500.000đ; validation từ chối 101%; sửa mức cố định thành 750.000đ; bật/tắt hai chương trình thành công. |
| Giá khuyến mại / quote | PASS | Hai chương trình cùng áp dụng cho laptop 16.990.000đ nhưng Cart chỉ dùng mức tốt nhất 10% (1.699.000đ), không cộng dồn. Sau khi đổi promotion trong lúc Checkout đang mở, POST quote cũ trả lại thông báo cần xác nhận báo giá mới; quote mới dùng mức cố định 750.000đ. |
| Checkout, idempotency, snapshot | PASS | Tạo order `19` ở PENDING rồi replay cùng CheckoutRequestId; replay trả cùng order, không tạo đơn thứ hai. Snapshot lưu UnitPrice 16.990.000đ, DiscountAmount 750.000đ và TotalPrice 16.240.000đ. |
| My Orders, ownership, hủy | PASS | Chủ đơn thấy list/details; Customer khác nhận 404 khi đọc và không hủy được đơn; lần hủy đầu chuyển CANCELLED, lần tiếp theo không tạo RELEASE thứ hai. Anonymous bị chuyển Login. |
| Empty Checkout | PASS | Sau khi đơn đã tạo và Cart được dọn, GET Checkout hiển thị trạng thái giỏ trống. |

Runner ghi nhận **63 HTTP assertions PASS**. Hai response validation được kiểm tra thêm sau khi decode HTML: phần trăm 101% bị từ chối và quote cũ bị yêu cầu xác nhận lại. Một lần đầu runner dùng raw HTML nên không nhận ra thông báo đã HTML-encode; nội dung server thực tế đúng. Route tư vấn chính xác là `/tu-van-san-pham` (không phải `/Consultation`). Đây là điều chỉnh của kiểm tra, không phải lỗi ứng dụng.

### HTTP smoke và static assets sau build

- HTTP 200: Home `/`, Catalog `/Catalog`, Product Details `/Catalog/Details/1`, Compare `/Catalog/Compare?ids=1&ids=2`, tư vấn `/tu-van-san-pham`, Register và Login.
- Anonymous nhận 302 tại `/Cart`, `/Checkout`, `/Orders` và `/Admin/Promotions` theo authorization hiện hành.
- Static assets trả 200: Bootstrap CSS/JS, `site.css`, `admin.css`, `cart.js`, `catalog-cart.js`, `admin-promotions-form.js`.
- Sau khi phát hiện cảnh báo EF Core cho projection chi tiết nạp đồng thời ảnh và thông số, `CustomerCatalogQueryService.GetProductAsync` dùng `AsSplitQuery()`. Product Details vẫn trả 200 và cảnh báo multiple-collection query không còn xuất hiện. Không đổi schema hay nghiệp vụ.
- Cảnh báo HTTPS redirect vẫn xuất hiện vì instance kiểm tra chỉ bind HTTP; đây là cấu hình môi trường smoke, không phải lỗi response.

### Regression test sau thay đổi

- `dotnet restore NEXORA.sln`: thành công, dependencies up-to-date.
- `dotnet build NEXORA.sln --no-restore`: thành công, **0 warning / 0 error**.
- `dotnet test NEXORA.sln --no-build --no-restore` với `NEXORA_CHECKOUT_SQL_TESTS=1` và connection đã xác minh tới test DB: **143 passed, 0 failed, 0 skipped**.
- `git diff --check`: thành công. Git có cảnh báo chuẩn hóa LF/CRLF cho một số file đã sửa từ trước; không có whitespace error.

### QA data còn trong test database

| Bản ghi | ID | Trạng thái sau E2E |
|---|---:|---|
| Customer QA | user 81 | Đăng ký riêng cho E2E |
| Admin QA | user 82 | Có role Customer và Admin; được cấp Admin bằng DevelopmentAdminProvisioningCommand |
| Customer QA thứ hai | user 83 | Dùng kiểm tra ownership |
| Order QA | order 19 | CANCELLED; một dòng snapshot |
| InventoryTransaction | order 19 | Một RESERVE và đúng một RELEASE |
| Promotion phần trăm | promotion 81 | IsActive=false |
| Promotion cố định | promotion 82 | Đã sửa thành 750.000đ, IsActive=false |
| Fixture hết hàng | product 149 | `NEXORA-QA-OOS-20261019`, ACTIVE, inventory 0/0; tạo trong `NEXORA_IntegrationTests` qua ProductManagementService |

QA marker: `714c22496d`. Baseline trước khi Manual UAT bắt đầu: 11 Product demo, 11 Inventory demo, mỗi SKU `10 / 0`; Cart của user 81 có 0 item; 3 QA user, 1 QA order đã CANCELLED, 2 QA inventory transactions và 2 QA promotions. Fixture product 149 được thêm riêng sau đó; tồn các SKU demo không bị đổi. Các QA records giữ trong **test DB**; không dọn dữ liệu. Những thao tác UAT sau baseline có thể đã cập nhật Cart, Order hoặc Promotion; lần chốt tài liệu này không đọc/ghi các giá trị đó.

## Browser/UI acceptance — PASS (user-confirmed, 2026-10-19)

Ngày 2026-10-19, chủ dự án xác nhận đã trực tiếp kiểm tra trên trình duyệt và PASS cả sáu nhóm:

| Hạng mục | Kết quả | Nguồn xác nhận |
|---|---|---|
| Catalog và hiển thị tồn kho | PASS | Xác nhận thủ công của người dùng |
| Giỏ hàng | PASS | Xác nhận thủ công của người dùng |
| Checkout | PASS | Xác nhận thủ công của người dùng |
| Danh sách, chi tiết và hủy đơn | PASS | Xác nhận thủ công của người dùng |
| Admin Promotions | PASS | Xác nhận thủ công của người dùng |
| Responsive desktop/tablet/mobile | PASS | Xác nhận thủ công của người dùng |

Tổng kết Manual UAT: **6/6 PASS**. Đây không phải kết quả browser automation do Codex thực hiện. Codex không tạo ảnh chụp màn hình hoặc bằng chứng kiểm thử giả; không có ảnh đính kèm lời xác nhận. Chi tiết và ghi nhận nguồn xác nhận nằm trong [`PHASE4_MANUAL_UAT.md`](./PHASE4_MANUAL_UAT.md).

## Lịch sử trạng thái browser ngày 2026-10-18

Computer Use đã dừng trước thao tác trình duyệt vì công cụ không xác định được URL hiện tại trên Chrome đủ chắc chắn. Tại thời điểm đó, thao tác Cart/Checkout/Orders/Admin Promotions và responsive 1440×900, 768×1024, 390×844 còn BLOCKED/NOT RUN. Trạng thái này được đóng bằng xác nhận Manual UAT của chủ dự án ngày 2026-10-19; không được hiểu là Codex đã tự động hóa trình duyệt.

## Rà soát cuối và kết luận

- Đối chiếu `PHASE4_DESIGN.md` và `REQUIREMENTS_TRACEABILITY.md`: phạm vi Phase 4 FR13–FR16, UC-C11–UC-C14, BR01, BR03, BR10–BR12 cùng idempotency đã được triển khai và kiểm thử. Không thấy yêu cầu bắt buộc Phase 4 còn mở.
- Migration `20261016133620_AddCheckoutRequestId` là migration duy nhất của Phase 4: cột `orders.checkout_request_id` nullable `uniqueidentifier` và unique filtered index `ux_orders_checkout_request_id`. Read-only verification ngày 2026-10-19 xác nhận history/cột/index đúng trên `NEXORA` và `NEXORA_IntegrationTests`. Không có migration hoặc schema change khác trong repository.
- Kết quả regression gần nhất: **143 passed, 0 failed, 0 skipped**; HTTP E2E **63 assertions PASS**; SQL integration dùng `NEXORA_IntegrationTests`; restore/build thành công. Không chạy lại các suite trong lượt chỉ chốt tài liệu này.
- `git diff --check` không có whitespace error; Git có cảnh báo chuẩn hóa LF/CRLF cho một số file đã sửa.
- Sales/Warehouse fulfillment, xác nhận/chuyển đơn, xuất kho, giao dịch OUT và điều chỉnh tồn có lý do thuộc Phase 5, không nằm trong nghiệm thu Phase 4.
- **Phase 4: COMPLETED.** Thay đổi vẫn chưa commit/push.
