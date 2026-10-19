# NEXORA — Báo cáo bàn giao và nghiệm thu Phase 4

**Ngày chốt:** 19/10/2026<br>
**Trạng thái:** **COMPLETED**<br>
**Git:** Các thay đổi còn ở working tree, chưa commit và chưa push.

## 1. Mục tiêu và phạm vi

Phase 4 thực hiện UC-C11–UC-C14: quản lý giỏ hàng, áp dụng khuyến mại, checkout tạo đơn, xem đơn và khách tự hủy đơn đang `PENDING`. Phạm vi nghiệp vụ truy vết với FR13–FR16, BR01, BR03, BR10–BR12; bổ sung idempotency checkout theo quyết định đã được chủ dự án duyệt.

Không thuộc Phase 4: thanh toán trực tuyến, xác nhận/chuyển đơn bởi Sales, chuẩn bị/xuất kho, giao dịch `OUT`, điều chỉnh tồn có lý do và quy trình fulfillment đầy đủ. Các hạng mục này được chuyển sang Phase 5.

## 2. Chức năng hoàn thành

- Cart theo người dùng đăng nhập: thêm, cộng số lượng, cập nhật, xóa; AJAX/fetch cập nhật giỏ và badge mà không reload. Server/CartService xác thực quyền sở hữu, giá và tồn có thể bán.
- Giá và khuyến mại do server tính. Chọn một khuyến mại hợp lệ giảm nhiều nhất cho mỗi dòng, không cộng dồn; `FIXED_AMOUNT` áp dụng một lần mỗi dòng. Có quản trị khuyến mại tạo/sửa/gán sản phẩm/bật/tắt và validation dữ liệu.
- Checkout tải lại báo giá, kiểm tra tồn và trạng thái sản phẩm, tạo `PENDING` Order cùng snapshot OrderDetail và reserve tồn trong transaction. Idempotency chống tạo đơn trùng.
- Customer xem danh sách/chi tiết đơn của chính mình và chỉ tự hủy được đơn `PENDING`; hủy lặp không tạo thêm giao dịch release.
- Product mới có Inventory mặc định `0/0`; danh sách Admin hiển thị tồn/đang giữ/có thể bán. Các thay đổi này không thêm tồn kho vào Product.
- Phân quyền Customer/Admin, antiforgery, concurrency/rowversion, CHECK constraints và test SQL Server cho luồng đặt hàng được giữ trong phạm vi đã duyệt.

## 3. Kết quả kiểm thử

| Loại kiểm thử | Kết quả | Ghi chú |
|---|---|---|
| Restore/build | PASS | Lần chạy gần nhất trước chốt tài liệu thành công. Không chạy lại do lượt này chỉ cập nhật tài liệu. |
| Automated test | **143 passed, 0 failed, 0 skipped** | Lần chạy gần nhất trên toàn solution. |
| HTTP E2E | **63 assertions PASS** | Cart, Checkout, Order, Promotion, authorization và validation. |
| SQL Server integration | PASS | Dùng riêng `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; gồm transaction, concurrency, idempotency, reservation, snapshot và hủy đơn. |
| Manual browser UAT | **6/6 PASS — user-confirmed** | Chủ dự án xác nhận trực tiếp ngày 19/10/2026. Codex không chạy browser automation và không tạo ảnh chụp. |
| HTTP smoke trong lượt chuẩn bị | PASS | Các route công khai và static assets trả HTTP 200; đây không phải browser UI test. |
| `git diff --check` | PASS | Không có whitespace error; có cảnh báo Git chuẩn hóa LF/CRLF cho một số file. |

### Manual UAT 6/6

1. Catalog và hiển thị tồn kho — PASS.
2. Giỏ hàng — PASS.
3. Checkout — PASS.
4. Danh sách, chi tiết và hủy đơn — PASS.
5. Admin Promotions — PASS.
6. Responsive desktop/tablet/mobile — PASS.

Nguồn kết quả là xác nhận của chủ dự án trong ngày 19/10/2026. Xác nhận không kèm ảnh minh chứng; báo cáo không gán kết quả này cho Codex browser automation.

## 4. Lỗi đã xử lý trong Phase 4

- Sửa validation/model metadata của Promotion để form Create render được với culture hiện tại và giới hạn decimal hợp lệ.
- Hoàn thiện thao tác Cart bằng fetch/AJAX, anti-forgery, xử lý lỗi và cập nhật tóm tắt giỏ từ server.
- Tạo Inventory `0/0` cùng Product mới trong cùng lần lưu; Development inventory seeder chỉ áp dụng cho SKU demo cố định, tránh cấp tồn mô phỏng cho sản phẩm Admin mới.
- Thêm inventory summary vào danh sách Admin Products và hiển thị rõ khi chưa có Inventory.
- Bổ sung `CheckoutRequestId` để xử lý retry/idempotency checkout.

## 5. Schema và migration

- Migration Phase 4 duy nhất là `20261016133620_AddCheckoutRequestId`. Nó chỉ thêm `orders.checkout_request_id uniqueidentifier NULL` và unique filtered index `ux_orders_checkout_request_id` với filter `[checkout_request_id] IS NOT NULL`.
- Migration được duyệt và áp dụng trên `NEXORA_IntegrationTests`, sau đó trên Development `PC01\SQLEXPRESS / NEXORA` theo quy trình backup/preflight đã duyệt.
- Read-only verification ngày 19/10/2026 xác nhận cả hai database ONLINE, có `InitialFoundation` và `AddCheckoutRequestId`, có đúng cột nullable/index nêu trên, và mỗi database có 27 bảng vật lý.
- Diff EF Core trong working tree chỉ bổ sung đúng property/index tương ứng vào model snapshot/configuration. Không phát hiện migration hoặc thay đổi schema Phase 4 ngoài phạm vi đã duyệt. Không tạo migration trong lượt chốt.

## 6. Giới hạn và rủi ro còn lại

- Manual UAT được xác nhận ở mức nhóm chức năng; không có ảnh, log trình duyệt hoặc kết quả viewport chi tiết được gửi kèm. Đây là giới hạn của bằng chứng lưu trữ, không phủ định xác nhận PASS của chủ dự án.
- Codex không tự chạy browser automation trong lần nghiệm thu cuối.
- QA database chứa tài khoản, đơn và promotion/fixture kiểm thử; không tự dọn. Fixture hết hàng Product ID 149 (`NEXORA-QA-OOS-20261019`) chỉ thuộc `NEXORA_IntegrationTests`; 11 SKU demo không bị đổi tồn khi chuẩn bị UAT.
- Email reset hiện dùng Development mailbox; production email provider chưa cấu hình.
- Cổng thanh toán trực tuyến chưa triển khai theo phạm vi thiết kế.
- Sales/Warehouse và fulfillment chưa triển khai; xem Phase 5 bên dưới.

## 7. Công việc chuyển sang Phase 5

- Sales xác nhận đơn, xử lý vòng đời và chuyển đơn sang kho.
- Warehouse tiếp nhận/chuẩn bị hàng, xuất kho; cập nhật tồn thực tế, reservation, trạng thái đơn và `InventoryTransaction` loại `OUT` nguyên tử.
- Điều chỉnh tồn có lý do, tra cứu/lịch sử nhập-xuất-điều chỉnh và kiểm soát quyền thao tác.
- Kiểm thử rollback, trạng thái không hợp lệ, thiếu tồn và concurrency cho toàn luồng fulfillment.

Không có công việc Phase 5 nào được thực hiện trong lượt này.

## 8. Danh sách file đang thay đổi/chưa commit

Danh sách dưới đây là toàn bộ các file modified/untracked trong working tree tại lúc lập báo cáo; gồm implementation Phase 4, test và hồ sơ bàn giao. Không có file nào được commit.

### Tài liệu

- `docs/IMPLEMENTATION_PLAN.md`
- `docs/REQUIREMENTS_TRACEABILITY.md`
- `docs/PHASE4_ACCEPTANCE.md`
- `docs/PHASE4_DESIGN.md`
- `docs/PHASE4_MANUAL_UAT.md`
- `docs/PHASE4_FINAL_REPORT.md`
- `docs/PHASE4_PRODUCTION_MIGRATION_PREVIEW.sql`
- `docs/PHASE4_PRODUCTION_MIGRATION_REVIEW.md`
- `docs/PHASE4_SCHEMA_REVIEW.sql`

### Application và Domain

- `src/NEXORA.Application/Catalog/CustomerCatalogContracts.cs`
- `src/NEXORA.Application/Catalog/ProductManagementContracts.cs`
- `src/NEXORA.Application/Commerce/CartContracts.cs`
- `src/NEXORA.Application/Commerce/DevelopmentInventorySeeder.cs`
- `src/NEXORA.Application/Commerce/OrderContracts.cs`
- `src/NEXORA.Application/Commerce/PromotionAdminContracts.cs`
- `src/NEXORA.Domain/Entities/CommerceEntities.cs`
- `src/NEXORA.Domain/Entities/PromotionPricingPolicy.cs`

### Infrastructure và EF Core

- `src/NEXORA.Infrastructure/Catalog/CustomerCatalogQueryService.cs`
- `src/NEXORA.Infrastructure/Catalog/ProductManagementService.cs`
- `src/NEXORA.Infrastructure/Commerce/AdminPromotionManagementService.cs`
- `src/NEXORA.Infrastructure/Commerce/CartService.cs`
- `src/NEXORA.Infrastructure/Commerce/CheckoutService.cs`
- `src/NEXORA.Infrastructure/Commerce/CustomerOrderService.cs`
- `src/NEXORA.Infrastructure/Commerce/DevelopmentInventorySeeder.cs`
- `src/NEXORA.Infrastructure/Commerce/OrderPricingService.cs`
- `src/NEXORA.Infrastructure/DependencyInjection.cs`
- `src/NEXORA.Infrastructure/Persistence/EntityConfigurations.cs`
- `src/NEXORA.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- `src/NEXORA.Infrastructure/Persistence/Migrations/20261016133620_AddCheckoutRequestId.cs`
- `src/NEXORA.Infrastructure/Persistence/Migrations/20261016133620_AddCheckoutRequestId.Designer.cs`

### Web/MVC/UI

- `src/NEXORA.Web/Areas/Admin/Controllers/PromotionsController.cs`
- `src/NEXORA.Web/Areas/Admin/Models/PromotionAdminViewModels.cs`
- `src/NEXORA.Web/Areas/Admin/Views/Products/Index.cshtml`
- `src/NEXORA.Web/Areas/Admin/Views/Promotions/Form.cshtml`
- `src/NEXORA.Web/Areas/Admin/Views/Promotions/Index.cshtml`
- `src/NEXORA.Web/Areas/Admin/Views/Shared/_AdminNavigation.cshtml`
- `src/NEXORA.Web/Controllers/CartController.cs`
- `src/NEXORA.Web/Controllers/CheckoutController.cs`
- `src/NEXORA.Web/Controllers/OrdersController.cs`
- `src/NEXORA.Web/Identity/DevelopmentAdminProvisioningCommand.cs`
- `src/NEXORA.Web/Models/CheckoutViewModel.cs`
- `src/NEXORA.Web/Program.cs`
- `src/NEXORA.Web/ViewComponents/CartSummaryViewComponent.cs`
- `src/NEXORA.Web/Views/Cart/Index.cshtml`
- `src/NEXORA.Web/Views/Catalog/Compare.cshtml`
- `src/NEXORA.Web/Views/Catalog/Details.cshtml`
- `src/NEXORA.Web/Views/Catalog/Index.cshtml`
- `src/NEXORA.Web/Views/Checkout/Index.cshtml`
- `src/NEXORA.Web/Views/Orders/Details.cshtml`
- `src/NEXORA.Web/Views/Orders/Index.cshtml`
- `src/NEXORA.Web/Views/Shared/Components/CartSummary/Default.cshtml`
- `src/NEXORA.Web/Views/Shared/_Layout.cshtml`
- `src/NEXORA.Web/Views/Shared/_ProductCard.cshtml`
- `src/NEXORA.Web/wwwroot/css/admin.css`
- `src/NEXORA.Web/wwwroot/css/site.css`
- `src/NEXORA.Web/wwwroot/js/admin-promotions-form.js`
- `src/NEXORA.Web/wwwroot/js/cart.js`
- `src/NEXORA.Web/wwwroot/js/catalog-cart.js`
- `src/NEXORA.Web/wwwroot/js/catalog-compare-selection.js`

### Tests

- `tests/NEXORA.Tests/AccountProfileHttpIntegrationTests.cs`
- `tests/NEXORA.Tests/AdminDashboardSqlServerIntegrationTests.cs`
- `tests/NEXORA.Tests/AdminLayoutHttpIntegrationTests.cs`
- `tests/NEXORA.Tests/AdminProvisioningCommandTests.cs`
- `tests/NEXORA.Tests/AppDbContextModelTests.cs`
- `tests/NEXORA.Tests/BusinessPolicyTests.cs`
- `tests/NEXORA.Tests/CartControllerAddResponseTests.cs`
- `tests/NEXORA.Tests/CheckoutSqlServerFactAttribute.cs`
- `tests/NEXORA.Tests/CheckoutSqlServerIntegrationTests.cs`
- `tests/NEXORA.Tests/CommerceAuthorizationTests.cs`
- `tests/NEXORA.Tests/CommerceSqlServerIntegrationTests.cs`
- `tests/NEXORA.Tests/CommerceTestDoubles.cs`
- `tests/NEXORA.Tests/ConsultationSqlServerIntegrationTests.cs`
- `tests/NEXORA.Tests/ProductInventorySqlServerIntegrationTests.cs`
- `tests/NEXORA.Tests/PromotionFormViewModelValidationTests.cs`
- `tests/NEXORA.Tests/SqlServerFactAttribute.cs`
- `tests/NEXORA.Tests/SqlServerIntegrationCollection.cs`

## 9. Kết luận và điều kiện commit

Phase 4 đáp ứng phạm vi chức năng đã thống nhất, regression đạt, schema chỉ có thay đổi đã duyệt và chủ dự án xác nhận Manual UAT 6/6 PASS. Không còn yêu cầu bắt buộc Phase 4 hoặc lỗi nghiêm trọng chưa xử lý được xác định trong rà soát này. **Phase 4 COMPLETED; repository sẵn sàng được review để tạo commit kết thúc Phase 4 khi chủ dự án yêu cầu.** Chưa commit/push.
