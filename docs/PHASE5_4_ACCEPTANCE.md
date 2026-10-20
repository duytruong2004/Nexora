# NEXORA – Phase 5.4 Warehouse Inventory Management

**Ngày kiểm tra:** 2026-10-20
**Trạng thái:** Code và automated QA đạt; Phase 5 tổng thể vẫn **IN PROGRESS**. Browser/manual UAT bằng tài khoản nhân viên chưa thực hiện.

## 1. Phạm vi đã triển khai

- Warehouse có danh sách tồn kho theo SKU/tên, lọc hết hàng, phân trang, trạng thái và các cột tồn vật lý, đang giữ, có thể bán. Công thức có thể bán lấy từ `QuantityInStock - ReservedQuantity` trong truy vấn; sản phẩm thiếu Inventory hiển thị “Chưa tạo tồn kho”.
- Warehouse có thể ghi nhập kho `IN` với số nguyên dương, tham chiếu bắt buộc và ghi chú tùy chọn. Service gọi `Inventory.Receive`, giữ nguyên reservation và ghi Inventory cùng ledger trong một SQL transaction. Product không tồn tại hoặc thiếu Inventory bị từ chối; service không tự tạo bản ghi Inventory.
- Warehouse có thể điều chỉnh `INCREASE`/`DECREASE`; số lượng phải dương và lý do bắt buộc. Service chuyển thành delta có dấu, gọi `Inventory.AdjustStock`, giữ nguyên ReservedQuantity và từ chối giảm tồn xuống thấp hơn lượng đang giữ. Ledger `ADJUST` ghi quantity dương và note có hướng/lý do.
- Lịch sử truy vấn `inventory_transactions` hiện có, lọc sản phẩm/loại/ngày, phân trang; hiển thị actor, Order liên quan và note. Hướng ADJUST chỉ được giải thích khi note khớp định dạng canonical mới có OPID GUID và DIR. Bản ghi cũ không đủ thông tin hiển thị “Không xác định”.
- Warehouse có quyền GET và POST; Admin chỉ đọc danh sách/lịch sử; Sales, Customer và anonymous không được truy cập. POST dùng Anti-Forgery. Actor lấy từ claim server; không nhận user ID hoặc tổng tồn từ trình duyệt.
- Giao diện dùng Razor/Bootstrap hiện có, bảng cuộn nội bộ trên viewport hẹp và form nhập liệu với validation phía server.

Không thay đổi CheckoutService, OrderService, reservation/release/export hoặc Product Create/Edit. Không sửa database, `inventory_transactions`, migration, snapshot hay entity mapping cho Phase 5.4.

## 2. Retry, concurrency và mức bảo đảm

Biểu mẫu giữ một RequestId ổn định và service lưu marker `OPID=<guid>` trong note ledger. Trong transaction, service lấy SQL Server transaction-owned `sp_getapplock` theo RequestId, tra ledger trước khi khóa/cập nhật Inventory và đối chiếu actor, product, transaction type, quantity, direction/reason/note. Replay cùng payload trả `AlreadyApplied`; cùng token với payload khác trả `IdempotencyConflict`. Inventory dùng `InventoryRowLocking.LoadForUpdateAsync`, cùng đường khóa `UPDLOCK, ROWLOCK, HOLDLOCK` được dùng cho fulfillment; rowversion và các CHECK trong schema vẫn có hiệu lực.

Deadlock victim SQL 1205 được SQL Server rollback xác nhận; service kiểm tra marker rồi retry có giới hạn bằng đúng RequestId. Lỗi ghi ledger rollback tồn. Khi lỗi khiến không xác minh được commit outcome, service không báo thành công; trả `CommitOutcomeUnknown` để người vận hành kiểm tra lịch sử và giữ nguyên token khi thử lại.

Đây là chống replay giữa các đường ghi ứng dụng dùng cùng applock, **không phải exactly-once tuyệt đối**. Schema không có unique constraint cho marker; một đường ghi trực tiếp SQL hoặc ứng dụng khác bỏ qua applock có thể tạo ledger trùng. Nếu SQL Server không thể truy vấn sau lỗi kết nối, outcome có thể vẫn chưa xác định. Không thêm `operation_id` hay cột hướng vào schema theo quyết định đã duyệt.

## 3. Kết quả kiểm thử

Trước test SQL, cấu hình được xác minh khớp chính xác Windows Integrated Authentication tới `PC01\SQLEXPRESS / NEXORA_IntegrationTests`. Read-only preflight xác nhận database `ONLINE`, 27 bảng vật lý và ba migration hiện có. Không kết nối/ghi vào database `NEXORA` chính.

| Loại kiểm thử | Kết quả | Bằng chứng/phạm vi |
|---|---:|---|
| Restore | PASS | `dotnet restore NEXORA.sln`; mọi project up-to-date |
| Build | PASS | `dotnet build NEXORA.sln --no-restore`; 0 warning, 0 error |
| Full automated solution | **197 passed, 0 failed, 0 skipped** | `dotnet test NEXORA.sln --no-build --no-restore`; SQL suites dùng database QA đã xác minh |
| Phase 5.4 targeted | **11 passed, 0 failed, 0 skipped** | 10 SQL Server integration tests và 1 HTTP authorization/form test |
| Model/migration drift | PASS | `dotnet ef migrations has-pending-model-changes` trả “No changes have been made to the model since the last migration.” Design-time connection được trỏ tạm tới database QA; không chạy update |
| `git diff --check` | PASS | Không phát hiện whitespace error; Git chỉ hiện cảnh báo chuẩn hóa LF/CRLF |

### SQL Server race/transaction tests đã chạy thật

10 SQL tests bao gồm: nhập thành công và replay; invalid quantity/product/Inventory thiếu; adjustment tăng/giảm và từ chối dưới reservation; lỗi insert ledger làm rollback; hai request cùng token và token sai payload; hai lần nhập đồng thời cùng SKU; hai adjustment từ hai actor đồng thời cùng SKU; adjustment giảm đồng thời Checkout; adjustment tăng đồng thời Warehouse Export; query Inventory thiếu record và hướng ADJUST cũ không xác định.

Race Checkout đạt một trong các trạng thái tuần tự hợp lệ: Checkout reserve trước khiến adjustment giảm bị chặn, hoặc adjustment giảm trước khiến Checkout không thể reserve tồn đã hết. Race Export xác nhận adjustment INCREASE và export cùng hoàn tất, chỉ có một ledger OUT, inventory và reservation cuối cùng nhất quán. Đây là các lần chạy SQL Server thực tế, không dùng EF InMemory/SQLite.

HTTP test host xác nhận Warehouse đọc và gửi form; Admin chỉ GET danh sách/history, bị 403 với trang/action ghi; Sales/Customer bị 403; anonymous nhận challenge; POST thiếu antiforgery bị 400; POST hợp lệ chuyển actor ID từ principal đến service. Đây là HTTP integration test, không phải browser test.

## 4. Database và migration

- Database dùng cho SQL QA: `PC01\SQLEXPRESS / NEXORA_IntegrationTests`, ONLINE.
- Bảng vật lý: 27 = 22 bảng nghiệp vụ + 4 bảng kỹ thuật ASP.NET Core Identity + `__EFMigrationsHistory`.
- Migration history giữ nguyên:
  - `20261015173046_InitialFoundation`
  - `20261016133620_AddCheckoutRequestId`
  - `20261019174632_AddOrderCancellationRequestFields`
- Hậu kiểm chỉ đọc xác nhận Product/User/Order/Category/Brand fixture mang marker `NEXORA-P54-*`/`phase54-*` còn lại: 0. `orders.completed_at` và `inventories.row_version` vẫn hiện diện.
- Không tạo hoặc áp dụng migration; model snapshot không thay đổi. Database `NEXORA` chính không được truy cập trong công việc Phase 5.4.

Trong lượt test đầu, một test cũ cleanup không nhận Order Checkout do Order không có marker trong Note; query chỉ đọc tìm thấy đúng một fixture do chính lượt test đó tạo. Đã xóa riêng fixture này trong database QA theo khóa ngoại; không đụng dữ liệu khác. Cleanup helper được sửa để truy vấn Order theo user fixture. Các test cuối hoàn tất và hậu kiểm read-only xác nhận không còn fixture Phase 5.4.

## 5. Browser/manual UAT chưa thực hiện

Codex không chạy browser automation và không xác nhận giao diện qua trình duyệt. Cần nhân viên được cấp tài khoản thực tế tự nghiệm thu:

1. Đăng nhập Warehouse; mở `/Warehouse/Inventory`; thử tìm theo SKU/tên, lọc hết hàng, phân trang và đối chiếu tồn/đang giữ/có thể bán.
2. Chọn sản phẩm có Inventory; nhập số lượng dương và reference. Sau lưu, xác nhận tồn tăng, reservation không đổi, có ledger `IN` đúng actor/thời gian.
3. Thử quantity 0/âm và gửi lặp cùng biểu mẫu; xác nhận validation và không cộng trùng.
4. Điều chỉnh tăng rồi giảm hợp lệ; kiểm tra lý do/hướng và ledger `ADJUST`. Thử giảm vượt AvailableQuantity để xác nhận bị chặn dưới reservation.
5. Mở `/Warehouse/Inventory/History`; lọc Product/type/date; kiểm tra actor, Order và legacy ADJUST “Không xác định”.
6. Đăng nhập Admin; xác nhận đọc được Inventory/history nhưng không thấy thao tác ghi và POST nhập/điều chỉnh bị từ chối. Thử Sales, Customer và anonymous bị chặn.
7. Kiểm tra desktop/tablet/mobile trên Chrome/Edge; bảng cuộn trong vùng, form/nút không tràn ngang.

Ghi kết quả/ảnh minh chứng do người kiểm thử cung cấp; không đánh dấu các mục trên PASS trước xác nhận.

## 6. File Phase 5.4 đã thay đổi/tạo

- `src/NEXORA.Application/Commerce/WarehouseInventoryContracts.cs`
- `src/NEXORA.Infrastructure/Commerce/WarehouseInventoryManagementService.cs`
- `src/NEXORA.Infrastructure/DependencyInjection.cs`
- `src/NEXORA.Web/Areas/Warehouse/Controllers/InventoryController.cs`
- `src/NEXORA.Web/Areas/Warehouse/Views/Inventory/Index.cshtml`
- `src/NEXORA.Web/Areas/Warehouse/Views/Inventory/History.cshtml`
- `src/NEXORA.Web/Areas/Warehouse/Views/Inventory/Receive.cshtml`
- `src/NEXORA.Web/Areas/Warehouse/Views/Inventory/Adjust.cshtml`
- `src/NEXORA.Web/Models/WarehouseInventoryViewModels.cs`
- `src/NEXORA.Web/Views/Shared/_Layout.cshtml`
- `tests/NEXORA.Tests/WarehouseInventorySqlServerIntegrationTests.cs`
- `tests/NEXORA.Tests/WarehouseAuthorizationHttpIntegrationTests.cs`
- `docs/PHASE5_DESIGN_DRAFT.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/REQUIREMENTS_TRACEABILITY.md`
- `docs/PHASE5_4_ACCEPTANCE.md`

Các thay đổi Phase 5.1–5.3 đang có trong working tree được giữ nguyên, không commit/push.

## 7. Điều kiện còn lại trước khi nghiệm thu toàn Phase 5

- Hoàn tất browser/manual UAT cho Warehouse inventory/history và role matrix bằng tài khoản nhân viên thật được cấp hợp lệ.
- Chạy regression/manual workflow xuyên Customer → Sales → Warehouse → Admin theo kế hoạch Phase 5.
- Chủ dự án xem xét và chấp nhận hạn chế idempotency do schema không có unique operation key.

Phase 5.4 đạt kiểm thử tự động, nhưng Phase 5 chưa được đánh dấu COMPLETED.
