# NEXORA – Phase 5 Sales & Warehouse Design

**Trạng thái:** Phase 5 **COMPLETED cho Development/QA acceptance; READY FOR FINAL OWNER SIGN-OFF (2026-10-21)**. Chủ dự án xác nhận 9/9 mục UAT tổng hợp PASS. Database `NEXORA` chính chưa được cập nhật; migration Phase 5.2 cần phê duyệt riêng.
**Ngày cập nhật:** 2026-10-21

## 1. Căn cứ và phạm vi

Thiết kế giữ Modular Monolith ASP.NET Core MVC, Domain, Application, Infrastructure và Web Areas hiện có. Hệ thống có đúng bốn vai trò: Customer, Sales, Warehouse, Admin. State machine giữ nguyên bảy trạng thái `PENDING`, `CONFIRMED`, `PREPARING`, `EXPORTED`, `SHIPPING`, `COMPLETED`, `CANCELLED`.

Đối chiếu khóa luận Chương 2 (mục 2.4.5–2.4.7, 2.7, 2.9.6–2.9.7), `REQUIREMENTS_TRACEABILITY.md` (FR19–FR25, FR27–FR33, UC-C14, UC-S01–UC-S07, UC-W01–UC-W07, BR01, BR03–BR05, BR08, NFR03–NFR04, NFR10) và tài liệu Phase 4. Tài liệu khóa luận không được diễn giải thành trạng thái mới ngoài bảy trạng thái đang có.

## 2. Hiện trạng có thể tái sử dụng

- Checkout tạo đơn `PENDING`, giữ tồn và ghi `RESERVE` trong transaction. Sales xác nhận không được reserve lại.
- Customer hiện chỉ được hủy đơn của chính mình khi `PENDING`; hủy giải phóng reservation và ghi `RELEASE` trong transaction.
- `Inventory` có `Receive`, `Reserve`, `ReleaseReservation`, `ExportReserved`, `AdjustStock`; `row_version` là concurrency token. CHECK hiện có ngăn tồn/giữ âm và giữ vượt tồn.
- `orders` hiện có `ConfirmedBy`, `ConfirmedAt`, `CancelledAt`, `CancelReason`, `CompletedAt`; chưa có concurrency column hoặc dữ liệu thời điểm riêng cho mọi chuyển trạng thái.
- `inventory_transactions` đã hỗ trợ `IN`, `OUT`, `ADJUST`, `RESERVE`, `RELEASE`; `quantity` bắt buộc dương, không có idempotency key.
- Bốn Identity role đã được seed; đăng ký công khai vẫn chỉ dành cho Customer. Chưa có Sales/Warehouse controller hay chức năng cấp tài khoản nhân viên.

## 3. Ma trận chức năng và quyền

| Chức năng | Customer | Sales | Warehouse | Admin |
|---|---|---|---|---|
| Checkout, xem đơn | Checkout và chỉ xem đơn của mình | Không tạo đơn; xem/tìm tất cả đơn để xử lý | Chỉ xem hàng đợi đã bàn giao ở bước Warehouse | Giám sát và xem toàn bộ |
| Hủy trước xác nhận | Chủ đơn tự hủy `PENDING` | Không | Không | Không thay khách hủy |
| Xác nhận đơn | Không | `PENDING → CONFIRMED` | Không | Được xác nhận |
| Bàn giao kho | Không | `CONFIRMED → PREPARING` | Nhận hàng đợi, không tự tạo bàn giao | Chỉ giám sát |
| Yêu cầu hủy sau xác nhận | Chủ đơn gửi tối đa một yêu cầu ở `CONFIRMED`/`PREPARING` | Xét duyệt/từ chối | Không quyết định | Xét duyệt/từ chối |
| Chuẩn bị/xuất kho | Không | Theo dõi | Chuẩn bị; `PREPARING → EXPORTED` khi đủ điều kiện | Chỉ đọc, không xuất |
| Vận chuyển/hoàn tất | Chỉ xem | `EXPORTED → SHIPPING → COMPLETED` | Không | Chỉ đọc |
| Nhập/điều chỉnh/xem ledger kho | Không | Không mặc định | Có, có lý do; xem tồn và giao dịch | Chỉ xem tồn/tiến độ |

Mọi quyền được kiểm tra tại endpoint bằng ASP.NET Core Authorization. Ẩn nút hoặc menu không thay thế authorization. Admin không được mặc định có quyền bàn giao, xuất kho, sửa tồn, cập nhật vận chuyển hoặc hoàn tất đơn. Không thêm vai trò thứ năm. QA Sales/Warehouse hiện được cấp qua quy trình provisioning cục bộ có kiểm soát; không có đăng ký nhân viên công khai hoặc endpoint tự nâng quyền.

## 4. State machine

| Hiện tại | Hành động | Vai trò | Mới | Reservation/tồn |
|---|---|---|---|---|
| `PENDING` | Xác nhận | Sales/Admin | `CONFIRMED` | Không đổi |
| `PENDING` | Khách hủy trực tiếp | Customer là chủ đơn | `CANCELLED` | RELEASE đúng một lần; tồn vật lý không đổi |
| `CONFIRMED` | Bàn giao kho | Sales | `PREPARING` | Không đổi |
| `PREPARING` | Xuất toàn đơn | Warehouse | `EXPORTED` | Trừ tồn vật lý và reservation, ghi `OUT`, cùng transaction |
| `EXPORTED` | Đang giao | Sales | `SHIPPING` | Không đổi |
| `SHIPPING` | Hoàn tất | Sales | `COMPLETED` | Không đổi; đặt `completed_at` |
| `CONFIRMED`/`PREPARING` | Duyệt yêu cầu hủy hợp lệ | Sales/Admin | `CANCELLED` | RELEASE đúng một lần; cùng transaction |

Không cho nhảy bước, lùi trạng thái, thao tác lên trạng thái kết thúc hoặc cập nhật trạng thái trực tiếp từ client. `EXPORTED` là mốc không còn nhận yêu cầu hủy theo luồng này. Trả hàng/hoàn tiền sau xuất kho nằm ngoài Phase 5.

## 5. Quy trình yêu cầu hủy sau xác nhận

Mỗi Order có tối đa một yêu cầu; nếu bị `REJECTED`, không gửi lần thứ hai. Không thêm `CANCEL_REQUESTED` vào `orders.status`. Ở `CONFIRMED`/`PREPARING`, yêu cầu `PENDING` không đổi trạng thái đơn và không đổi reservation; Warehouse được chuẩn bị nhưng không được xuất. Sales/Admin quyết định: `REJECTED` giữ nguyên Order/reservation; `APPROVED` chuyển `CANCELLED`, ghi `CancelledAt`/`CancelReason`, RELEASE từng dòng và ghi ledger cùng transaction.

Khi yêu cầu được duyệt, trạng thái yêu cầu và Order phải được kiểm tra lại trong cùng transaction. Customer yêu cầu hủy, quyết định của hai nhân viên và Warehouse export phải dùng compare-and-swap trên cùng hàng Order, theo điều kiện trạng thái/yêu cầu còn hiệu lực. Chỉ lệnh đầu tiên khớp điều kiện được commit. Nếu export thắng trước, yêu cầu hủy bị từ chối; nếu yêu cầu PENDING hoặc duyệt hủy thắng trước, export bị chặn. Hai reviewer cùng quyết định: chỉ một lệnh từ `PENDING` được cập nhật; RELEASE chỉ nằm trong transaction của lệnh approve thắng.

### Cột và ràng buộc đã được duyệt và triển khai trong Phase 5.2

Chi tiết có trong [`PHASE5_SCHEMA_CHANGE_REVIEW.md`](./PHASE5_SCHEMA_CHANGE_REVIEW.md). Đề xuất thêm bảy cột vào bảng `orders`: `cancel_request_status`, `cancel_request_reason`, `cancel_request_id`, `cancel_requested_at`, `cancel_reviewed_by`, `cancel_reviewed_at`, `cancel_decision_note`; CHECK về giá trị/tính đầy đủ, FK `cancel_reviewed_by → users.id`, unique filtered index cho `cancel_request_id` và filtered index cho hộp chờ xử lý. Không thêm bảng nghiệp vụ mới; tổng vẫn 22 bảng. Mọi cột, index và constraint đều chờ phê duyệt migration.

## 6. Quy trình Sales Order Management — phần đã triển khai

Sales/Admin có danh sách, tìm kiếm/lọc và chi tiết đơn. Sales được xác nhận `PENDING → CONFIRMED`, bàn giao `CONFIRMED → PREPARING`, cập nhật `EXPORTED → SHIPPING`, `SHIPPING → COMPLETED`. Admin chỉ xem/giám sát và xác nhận `PENDING → CONFIRMED`. Mỗi thay đổi dùng một `UPDATE ... WHERE id=? AND status=?`; confirm/completion metadata được cập nhật trong cùng câu SQL. Nếu không có hàng bị ảnh hưởng, service đọc trạng thái hiện hành và trả conflict hoặc trạng thái đã được cập nhật. Reservation không bị thay đổi khi xác nhận/bàn giao/vận chuyển.

Schema hiện không lưu bền vững mọi mốc bàn giao/vận chuyển và không có status-history table. Không giả lập các mốc đó; ghi log ứng dụng chỉ gồm ID, trạng thái cũ/mới, actor ID và thời điểm, không ghi thông tin cá nhân. Nếu cần lịch sử kiểm toán có thể truy vấn sau này, cần phê duyệt thiết kế schema riêng; theo quyết định hiện tại không thêm bảng lịch sử.

Phần code tương ứng: `ISalesOrderManagementService`, `SalesOrderManagementService`, Domain `OrderWorkflowRules`, controller Area Sales/Admin, Razor views và dependency injection. HTTP integration test xác nhận render Sales/Admin và Customer bị chặn khỏi cả hai area; SQL Server integration test xác nhận tìm kiếm, chi tiết, compare-and-swap đồng thời, idempotent retry theo trạng thái và confirm/handoff/shipping/completion. Final manual UAT S01/S02/S03 được chủ dự án xác nhận PASS trong báo cáo Phase 5.5; automated evidence vẫn được phân loại riêng.

## 7. Quy trình Warehouse — Phase 5.3 đã triển khai

1. Hàng đợi chỉ truy vấn đơn `PREPARING`, hỗ trợ tìm theo mã đơn/SKU/tên sản phẩm và phân trang. Warehouse mở được chi tiết đơn đang chuẩn bị hoặc đã xuất để xác minh thao tác lặp.
2. Warehouse kiểm tra SKU, số lượng đơn, tồn vật lý, reservation tổng, reservation thuộc đơn, tồn khả dụng và yêu cầu hủy. Yêu cầu `PENDING` cho phép tiếp tục chuẩn bị nhưng chặn export ở backend.
3. Export bắt đầu transaction SQL `SERIALIZABLE`, cập nhật có điều kiện Order từ `PREPARING` sang `EXPORTED` khi yêu cầu hủy không `PENDING` (chỉ NULL/REJECTED hợp lệ), sau đó khóa các Inventory theo `ProductId` tăng dần bằng `UPDLOCK, ROWLOCK, HOLDLOCK`.
4. Service kiểm tra lại OrderDetail, công thức/tổng tiền, ledger `RESERVE - RELEASE`, Inventory và reservation. Dùng `Inventory.ExportReserved`, ghi một `OUT` cho mỗi Product với `OrderId` và actor ID. `SaveChanges` và commit cùng transaction; lỗi giữa chừng rollback cả status, inventory và ledger.
5. Request/approval/export cùng tranh chấp trên hàng Order trước khi khóa Inventory, tiếp theo khóa Inventory theo thứ tự tăng dần. SQL Server deadlock victim 1205 được xác minh lại bằng status/ledger rồi retry toàn bộ transaction tối đa ba lần; lỗi rowversion trả conflict. Replay đọc trạng thái và so ledger OUT với số lượng đơn trước khi báo đã xuất; không nhận exactly-once tuyệt đối ngoài bảo đảm trạng thái/transaction hiện có.
6. Schema không lưu actor hoặc thời điểm riêng cho tiếp nhận/hoàn tất chuẩn bị; giao diện ghi rõ giới hạn, không tạo hành động “đã chuẩn bị” giả hoặc trạng thái Order mới. Thao tác vật lý chuẩn bị diễn ra khi Order vẫn `PREPARING`.

Nhập kho tăng `QuantityInStock`, không đổi reservation, ghi `IN`. Điều chỉnh nhận hướng `INCREASE`/`DECREASE` và lý do; service tính delta có dấu, gọi `AdjustStock`, không cho tồn mới thấp hơn `ReservedQuantity`, ledger ghi `quantity` dương và lưu hướng/lý do trong note theo dạng `OPID=<guid>;DIR=DECREASE;REASON=...;NOTE=...`. Chuỗi người dùng được escape delimiter để giữ nội dung có thể đọc trong history.

IN/ADJUST cập nhật Inventory và ghi ledger trong cùng SQL transaction. Các thao tác cùng token được tuần tự hóa bằng transaction-owned `sp_getapplock`; trong transaction service tìm marker `OPID` trước khi thay đổi tồn, xác thực payload (actor/product/type/quantity/note), rồi khóa Inventory bằng `InventoryRowLocking.LoadForUpdateAsync` theo cùng cơ chế với fulfillment. Request replay đúng payload trả trạng thái đã áp dụng; token dùng lại với payload khác bị từ chối. SQL deadlock victim 1205 có thể retry bounded vì SQL Server đã rollback transaction; mọi lần chạy lại dùng cùng token và kiểm tra ledger trước. Lỗi có kết quả commit chưa xác định không được báo thành công nếu không xác minh được marker; người dùng giữ nguyên token để kiểm tra/gửi lại.

Đây là chống trùng ở các đường ghi ứng dụng phối hợp cùng applock, không phải exactly-once tuyệt đối: schema không có unique constraint trên marker, direct SQL/đường ghi bỏ qua applock có thể tạo bản ghi trùng; khi database không thể truy vấn sau lỗi commit, kết quả vẫn được báo `CommitOutcomeUnknown`. Không thêm `operation_id`, cột hướng điều chỉnh hay bảng mới.

### Phase 5.4 – Quản lý tồn và lịch sử giao dịch kho

- `Warehouse/Inventory` cung cấp tìm theo SKU/tên, lọc sản phẩm hết hàng, phân trang và hiển thị tồn vật lý/đang giữ/có thể bán. Sản phẩm thiếu Inventory được hiển thị “Chưa tạo tồn kho”; service không tự tạo Inventory.
- Warehouse được GET danh sách/lịch sử và POST nhập/điều chỉnh. Admin chỉ GET xem/giám sát; Sales, Customer và anonymous bị chặn. POST dùng antiforgery và actor lấy từ claim server.
- Nhập chỉ nhận số nguyên dương, tham chiếu bắt buộc, tùy chọn ghi chú; gọi `Inventory.Receive`, ghi `IN`, giữ nguyên reservation. Điều chỉnh yêu cầu hướng, số lượng dương và lý do; gọi `Inventory.AdjustStock`, từ chối giảm dưới reserved, ghi `ADJUST` với quantity dương.
- Lịch sử truy vấn `inventory_transactions` hiện có, lọc Product/type/date và phân trang. Hướng ADJUST chỉ suy ra từ định dạng marker mới có GUID và trường DIR ở vị trí chuẩn; ghi cũ thiếu hướng hiển thị “Không xác định”.
- Không thay đổi các luồng checkout/reservation/release/export. Inventory rowversion, CHECK constraints và shared SQL row locking vẫn là lớp cuối cùng bảo vệ tồn/reservation.

## 8. Reservation và transaction

- Checkout thành công: tăng `ReservedQuantity`, ghi `RESERVE`; không giảm `QuantityInStock`.
- Xác nhận/bàn giao: không thay đổi Inventory.
- Hủy PENDING hiện có hoặc hủy sau xác nhận được duyệt: giảm reservation và ghi `RELEASE` đúng một lần; `QuantityInStock` giữ nguyên.
- Xuất kho thành công: giảm tồn vật lý và reservation tương ứng, ghi `OUT`, đổi trạng thái sang `EXPORTED` trong một transaction.
- `AvailableQuantity = QuantityInStock - ReservedQuantity`; không cho giá trị âm.
- Không tự hết hạn PENDING, không background job, không auto-cancel sau 24/48 giờ.

Các thao tác nhiều sản phẩm dùng thứ tự ProductId ổn định, execution strategy EF Core, transaction SQL Server và xử lý `DbUpdateConcurrencyException` có giới hạn. Sau lỗi commit không rõ, đọc lại trạng thái/ledger trước khi retry. Không lặp release/export từ giả định client chỉ gửi một lần.

## 9. Kiểm thử quan trọng

- Role matrix: anonymous/Customer bị chặn khỏi Sales/Admin; Warehouse bị chặn khỏi Sales transitions; Admin bị chặn khỏi handoff, export, stock write, shipping/completion.
- Mỗi chuyển trạng thái đúng và sai trạng thái; không nhảy/lùi; transition lặp không ghi lại metadata.
- Hai Sales cùng confirm PENDING: chỉ một cập nhật trạng thái.
- Customer PENDING cancellation hiện tại vẫn release đúng một lần.
- Với schema hủy sau khi được duyệt: gửi trùng/đổi ID lần hai bị từ chối; REJECTED không cho gửi lại; race request/export, approve/export, hai reviewer.
- Export đủ/thiếu reservation, xung đột concurrency, lỗi ledger và rollback; double export không ghi OUT hai lần.
- IN/ADJUST biên âm, vượt giữ, lý do trống; document giới hạn exactly-once của ledger hiện tại.
- Checkout/Cart Phase 4 regression.

## 10. Kế hoạch triển khai

1. [Hoàn tất] Cập nhật thiết kế và review schema.
2. [Hoàn tất] Sales Order Management trên schema hiện tại, role authorization và unit/HTTP/SQL tests.
3. [Hoàn tất trên QA] Migration yêu cầu hủy; Customer request, Sales/Admin review, race/concurrency và transaction tests.
4. [Hoàn tất code/QA – Phase 5.3] Warehouse queue/detail, role/antiforgery, export transaction, cancellation guard, OUT ledger, idempotent state/ledger verification, SQL rollback và race tests.
5. [Hoàn tất code/SQL QA – Phase 5.4] Warehouse Inventory list/history, IN/ADJUST, authorization, antiforgery, replay marker/app lock, rollback, checkout/export race tests.
6. [Hoàn tất Development/QA acceptance – Phase 5.5] Final regression 209/209, model/migration consistency, QA postflight và 9/9 mục UAT tổng hợp do chủ dự án xác nhận.

**Kết quả từng giai đoạn:** Phase 5.1: restore/build và 151 test; Phase 5.2: 172 test, migration chỉ trên QA; Phase 5.3: 187 test trên `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; Phase 5.4: chi tiết trong [`PHASE5_4_ACCEPTANCE.md`](./PHASE5_4_ACCEPTANCE.md). Các ghi chú “browser UAT chưa thực hiện” trong báo cáo theo giai đoạn phản ánh trạng thái tại ngày 2026-10-20; chúng được cập nhật bởi Phase 5.5 final review: 209/209 automated tests pass và 9/9 mục manual UAT tổng hợp do chủ dự án xác nhận. QA hậu kiểm giữ 27 bảng vật lý và ba migration. Không thay đổi database NEXORA chính.

Các tài khoản Sales/Warehouse QA đã tồn tại khi kiểm tra cuối; role counts không đổi trước/sau regression. HTTP integration test dùng principal chỉ dành cho test, tách biệt với manual UAT chủ dự án xác nhận.

**Phạm vi hoàn tất:** Phase 5 Development/QA đã đạt nghiệm thu theo báo cáo [`PHASE5_5_ACCEPTANCE.md`](./PHASE5_5_ACCEPTANCE.md). Manual UAT 9/9 là xác nhận tổng hợp của chủ dự án, không phải Codex browser automation; không có screenshot được cung cấp. Các detailed test cases chưa được xác nhận riêng không được đánh dấu PASS.

**Ngoài phạm vi hoàn tất:** migration Phase 5.2 chưa áp dụng vào database `NEXORA` chính và cần phê duyệt/backup/preflight riêng. Giới hạn còn lại gồm không có persisted actor/time audit cho chuẩn bị kho, idempotency `IN`/`ADJUST` chỉ được bảo vệ trên application paths dùng marker/app lock chứ không phải exactly-once trước direct SQL/bypass, cùng returns/refunds sau export và automatic pending-order expiry.
