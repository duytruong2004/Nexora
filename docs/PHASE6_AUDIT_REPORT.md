# NEXORA – Phase 6.0: Audit và phân tích khoảng trống

**Trạng thái tại thời điểm audit 2026-10-21:** Audit/design draft; khi đó Phase 6 chưa bắt đầu. Đây là báo cáo baseline lịch sử; trạng thái triển khai hiện tại được ghi tại [`PHASE6_IMPLEMENTATION_PLAN.md`](./PHASE6_IMPLEMENTATION_PLAN.md).

**Ngày audit:** 2026-10-21.

**Phạm vi:** FR12, FR17, FR34–FR50; BR02, BR08, BR09, BR13, BR14 và các ràng buộc liên quan.

**Thao tác dữ liệu:** Chỉ đọc repository và tài liệu. Không kết nối database, chạy migration, sửa schema hoặc triển khai nghiệp vụ.

## 1. Căn cứ và giới hạn kiểm tra

Đã đối chiếu Chương 2 trong [`NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx`](./NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx) (mục 2.2–2.4, 2.9), [`REQUIREMENTS_TRACEABILITY.md`](./REQUIREMENTS_TRACEABILITY.md), [`IMPLEMENTATION_PLAN.md`](./IMPLEMENTATION_PLAN.md), các acceptance/design Phase 2–5, source, tests, EF configurations và migration snapshot.

Chương 2 quy định 50 FR, 19 BR, 10 NFR, 46 Use Case và đúng bốn actor: Customer, Sales, Warehouse, Admin. Các FR/UC bên dưới dùng nguyên mã của khóa luận. Kết quả QA Phase 5.5 được ghi nhận theo acceptance đã có: 209 automated tests và 9/9 nhóm manual UAT do chủ dự án xác nhận; audit này không chạy lại test hoặc kiểm thử giao diện.

Không truy cập database `NEXORA` chính hay database QA trong lượt này. Vì vậy báo cáo mô tả model/source và các bằng chứng đã ghi trong tài liệu; không khẳng định trạng thái dữ liệu runtime chưa được truy vấn.

## 2. Repository và mô hình dữ liệu

- Branch hiện tại: `master`; HEAD: `ab5d945` — `feat: complete Phase 5 sales and warehouse workflows`.
- Working tree trước audit chỉ có file untracked `docs/PHASE4_SCHEMA_REVIEW.sql`. File này được giữ nguyên.
- Migration hiện diện trong source: `20261015173046_InitialFoundation`, `20261016133620_AddCheckoutRequestId`, `20261019174632_AddOrderCancellationRequestFields`. Đây là quan sát repository, không phải truy vấn database.
- `AppDbContext` cùng Identity mapping mô tả **22 bảng nghiệp vụ chính thức** (19 bảng domain catalog/commerce/engagement cộng `users`, `roles`, `user_roles`), **4 bảng hỗ trợ Identity** (`AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`) và `__EFMigrationsHistory` khi EF được áp dụng. Không đề xuất bảng nghiệp vụ mới.
- Bốn role được định danh trong `RoleNames`: `Customer`, `Sales`, `Warehouse`, `Admin`. Không đề xuất actor/role thứ năm.
- Không thay đổi `orders.completed_at`, các cột cancellation Phase 5, hoặc logic/state machine Sales/Warehouse.

### Thành phần hiện có liên quan

| Thành phần | Quan sát source |
|---|---|
| Identity | `AppUser : IdentityUser<int>`, `AppRole : IdentityRole<int>`, `AppUserRole : IdentityUserRole<int>`; bảng `users`, `roles`, `user_roles`; role seeder bảo đảm bốn role. `AppUser.IsActive` có mapping và Login/Profile kiểm tra cờ này. |
| Account administration | Có đăng ký Customer, đăng nhập, reset mật khẩu và profile. Không thấy Admin controller/view/service để tra cứu, khóa/mở khóa, tạo nhân viên hoặc gán role. Role provisioning hiện có là công cụ phát triển/QA, không thay thế UC-A01–A03. |
| Favorites | Entity `Favorite`, FK User/Product và unique index `ux_favorites_user_product`. Không thấy service, controller hoặc view Favorites cho Customer. |
| Reviews | Entity `Review`; CHECK rating 1–5, comment tối đa 3000, unique `OrderDetailId`, composite FK gắn OrderDetail/Product/Order và FK `(OrderId, UserId)` tới Order, cùng `ReviewEligibilityPolicy`. Không thấy workflow tạo/xem/moderate review trong MVC. `Status` là string tối đa 30, mặc định `PENDING`, chưa được giới hạn bằng enum/CHECK. |
| Catalog administration | Admin MVC đã có Categories, Brands, Products/ProductImages, Specifications/ProductSpecifications, Consultation và Promotions. Các controllers thuộc Area Admin dùng authorization phía server; chi tiết acceptance nằm trong tài liệu Phase 2–4. |
| Price | `ProductManagementService.SaveAsync` cập nhật `Product.Price` và `DiscountPrice`; OrderDetail giữ snapshot giao dịch. Không thấy bảng lịch sử giá; khóa luận ghi lịch sử giá là tùy chọn, không thuộc 22 bảng chính thức. |
| Dashboard | `SqlAdminDashboardQueryService` truy vấn SQL-side số Product, Product active, Category, UsageNeed, RecommendationRule. Dashboard chưa có các thống kê đơn hàng, doanh thu, sản phẩm bán và tồn kho FR47–50. |
| Inventory administration | `Warehouse/InventoryController` cho Warehouse/Admin đọc danh sách/lịch sử; Receive/Adjust POST được giới hạn Warehouse. Acceptance Phase 5.5 ghi Admin chỉ đọc. Đây chưa phải báo cáo tồn/low-stock của Admin (FR50). |
| Logging | Có `ILogger` ở một số service nghiệp vụ (ví dụ cancellation review, controller xử lý upload); chưa xác minh sink/audit retention tập trung. NFR10 cần rà soát cho hành động tài khoản, phân quyền, moderation và đổi giá. |

## 3. Ma trận truy vết và trạng thái

**Quy ước:** `COMPLETED` = chức năng được triển khai và có kiểm thử/acceptance phù hợp; `PARTIAL` = có nền tảng hoặc một phần luồng nhưng FR chưa đủ; `NOT IMPLEMENTED` = chưa thấy workflow được triển khai; `NEEDS VERIFICATION` = có phần triển khai nhưng cần xác minh bằng runtime/evidence chưa có trong audit. Sự tồn tại của Entity hoặc bảng không được coi là hoàn thành use case.

| FR | Use Case / BR | Entity | Service | Controller / View | Tests/evidence hiện có | Trạng thái, khoảng trống và đề xuất |
|---|---|---|---|---|---|---|
| FR12 | UC-C10; BR09 | `Favorite`; unique `(UserId, ProductId)` | Chưa có Favorite service | Chưa có Customer Favorites controller/view | `AppDbContextModelTests` kiểm tra model/index; không có workflow test | **PARTIAL** — DB bảo vệ duplicate nhưng chưa có thêm/xem/xóa yêu thích. Phase 6.2 thêm luồng Customer theo identity hiện hành; xử lý duplicate idempotent/race bằng unique index. |
| FR17 | UC-C15; BR02 | `Review`, `Order`, `OrderDetail`; FK owner/product/line, unique OrderDetail | Chỉ có `ReviewEligibilityPolicy.Validate` trong Domain | Chưa có Customer Review controller/view | `BusinessPolicyTests.ReviewEligibilityPolicy_RequiresOrderOwnerMatchingDetailAndCompletedOrder`; `AppDbContextModelTests` kiểm tra FK/unique/CHECK | **PARTIAL** — policy và schema hỗ trợ chủ đơn, đúng sản phẩm/chi tiết và COMPLETED; không có luồng gửi/xem review, kiểm tra quyền lúc ghi, nội dung/rate limit hoặc hiển thị. Phase 6.2 cần service và HTTP authorization. |
| FR34 | UC-A01; BR08, NFR02/NFR03/NFR10 | Identity `AppUser` (`IsActive`, Lockout fields) | Không có Admin account management service | Không có Admin Users controller/view | Identity/account tests bao phủ login/profile; không có Admin lock/unlock tests | **PARTIAL** — đăng nhập chặn user `IsActive=false`, nhưng chưa có UI Admin và chưa xác minh vô hiệu hóa session đã cấp. Thiết kế service lock/unlock, bảo vệ Admin cuối cùng, self-lock và session revocation. |
| FR35 | UC-A02; NFR02/NFR10 | `AppUser`, `AppUserRole` | QA/development provisioning command, không phải Admin use case | Không có Admin staff controller/view | Provisioning command/SQL regression tests; không chứng minh Admin UI | **PARTIAL** — có tiện ích provisioning tin cậy cho Development/QA; Admin chưa tạo/quản lý nhân viên. Đề xuất tạo nhân viên qua Admin workflow, cấp quyền bằng lời mời/reset link và không lưu password tạm. |
| FR36 | UC-A03; BR08, NFR03/NFR10 | `AppRole`, `AppUserRole` | `RoleSeeder`; các command provisioning giới hạn môi trường | Không có role management UI | `AdminProvisioningCommandTests`, `QaStaffProvisioningSqlServerIntegrationTests`; authorization tests cho role hiện hữu | **PARTIAL** — bốn role và công cụ cấp role có tồn tại, nhưng chưa có thao tác Admin phân quyền. Cần quyết định policy cấp Admin và chuyển Customer thành staff trước khi code. |
| FR37 | UC-A04; BR08 | `Category` | `CategoryBrandManagementService` | `Admin/CategoriesController`, Categories views | `CatalogAdminAuthorizationTests`, catalog/service/integration coverage; Phase 2 browser acceptance | **COMPLETED** cho CRUD Admin đã nghiệm thu Phase 2. Tránh viết lại; chỉ sửa gap phát sinh từ test/traceability. |
| FR38 | UC-A05; BR08 | `Brand` | `CategoryBrandManagementService` | `Admin/BrandsController`, Brands views | `CatalogAdminAuthorizationTests`, catalog/service/integration coverage; Phase 2 browser acceptance | **COMPLETED** cho quản trị Brand đã nghiệm thu Phase 2. |
| FR39 | UC-A06; BR08, BR12 | `Product`, `Inventory` tách domain | `ProductManagementService` | `Admin/ProductsController`, Products views | `ProductCatalogTests`, `ProductInventorySqlServerIntegrationTests`, `CatalogAdminAuthorizationTests`; Phase 2 acceptance | **COMPLETED** cho create/edit/stop selling; kiểm soát Inventory vẫn ở nghiệp vụ kho. Đổi giá hiện hành không được làm đổi snapshot OrderDetail. |
| FR40 | UC-A07; BR08 | `ProductImage` | `ProductImageManagementService`, storage abstraction | `Admin/ProductsController`, product details/image views | Catalog/admin tests và Phase 2 browser acceptance | **COMPLETED** theo Phase 2 acceptance; kiểm tra file upload/ownership không thuộc đề xuất quản trị lại. |
| FR41 | UC-A08; BR06 | `Specification`, `CategorySpecification`, `ProductSpecification` | `SpecificationManagementService`, `ProductSpecificationManagementService` | `Admin/SpecificationsController`, `ProductSpecificationsController`, views | `ProductSpecificationEditorControllerTests`, catalog/integration/authorization tests | **COMPLETED** cho cấu hình thông số đã nghiệm thu Phase 2. |
| FR42 | UC-A09; BR15–BR19, NFR03 | `UsageNeed`, `RecommendationRule` | `ConsultationManagementService` | `Admin/ConsultationController`, Consultation views | `ConsultationAdminAuthorizationTests`, `ConsultationSqlServerIntegrationTests`, parser/scoring tests; Phase 3 evidence có các giới hạn UAT được ghi | **COMPLETED** cho Admin CRUD/cấu hình quy tắc theo Phase 3; không sửa NLP pipeline trong Phase 6 nếu không có lỗi đã chứng minh. |
| FR43 | UC-A10; BR10 | `Promotion`, `PromotionProduct` | `AdminPromotionManagementService` | `Admin/PromotionsController`, Promotions views | `PromotionFormViewModelValidationTests`, `CommerceSqlServerIntegrationTests`, Admin HTTP/acceptance Phase 4 | **COMPLETED** cho tạo/cập nhật/kích hoạt promotion đã nghiệm thu Phase 4. |
| FR44 | UC-A11; BR08, NFR03/NFR10 | `Review.Status`, `CreatedAt`, `UpdatedAt` | Chưa có moderation service | Chưa có Admin Review controller/view | Chỉ policy/model tests, không moderation tests | **NOT IMPLEMENTED** — không có danh sách, quyết định moderation, actor/reason/timestamp của kiểm duyệt. `Status` hiện là free-form string; không được đoán tập giá trị/ý nghĩa đã lưu. Chốt workflow và audit requirement; mọi cột audit cần duyệt/migration riêng. |
| FR45 | UC-A12; BR12, NFR10 | `Product.Price`, `DiscountPrice`; OrderDetail snapshot | `ProductManagementService.SaveAsync` | Product Edit trong Admin Products | Product/integration/checkout snapshot coverage; không có price-history workflow | **PARTIAL** — giá bán hiện hành có thể cập nhật qua sửa Product và đơn giữ snapshot; thiếu thao tác giá riêng, log/audit truy vấn được và báo cáo thay đổi. Không tự thêm `product_price_histories` (tùy chọn ngoài 22 bảng). |
| FR46 | UC-A13; BR08 | Catalog/consultation entities | `IAdminDashboardQueryService` / `SqlAdminDashboardQueryService` | `RoleLandingController.Admin`, Admin Dashboard view | `AdminDashboardSqlServerIntegrationTests`, `AdminLayoutHttpIntegrationTests` | **PARTIAL** — 5 số đếm hiện tại là truy vấn thật; chưa phản ánh đơn hàng/doanh thu/sản phẩm/tồn kho. Mở rộng bằng SQL aggregates khi FR47–50 được triển khai, không dùng dữ liệu giả. |
| FR47 | UC-A14; BR08 | `Order` | Không có reporting query/service riêng | Không có Order Reports page (có Order operations, khác báo cáo) | Sales/Order tests không phải thống kê Admin | **NOT IMPLEMENTED** — cần thống kê count theo status/date và định nghĩa bộ lọc/thời gian. |
| FR48 | UC-A15; BR13–BR14, NFR04 | `Order.TotalAmount`, `Order.CompletedAt`, Order status | Không có revenue query/service | Không có Revenue Report view | Order tests kiểm tra snapshot/trạng thái; không có revenue report aggregation test | **NOT IMPLEMENTED** — chỉ tính `COMPLETED`; tổng theo số tiền giao dịch đã chốt; lọc theo `completed_at`; không join line làm nhân bản Order.TotalAmount. |
| FR49 | UC-A16; BR11–BR14 | `Order`, `OrderDetail` | Không có product sales report query/service | Không có Product Sales report view | Chưa có reporting-specific tests | **NOT IMPLEMENTED** — cần chốt status được tính là “bán ra”; đề xuất mặc định báo cáo giao dịch hoàn tất, dùng snapshot dòng đơn. |
| FR50 | UC-A17; BR03–BR05, NFR04 | `Inventory`, `InventoryTransaction`, `Product` | `WarehouseInventoryManagementService` phục vụ kho | Warehouse Inventory/History; Admin read-only | `WarehouseInventorySqlServerIntegrationTests`, `WarehouseAuthorizationHttpIntegrationTests`; owner UAT A01 PASS | **PARTIAL** — Admin xem tồn/lịch sử kho qua module Warehouse, nhưng chưa có thống kê tổng/low-stock riêng. Phase 6.4 thêm báo cáo chỉ đọc, không mở quyền nhập/xuất/điều chỉnh cho Admin. |

## 4. State machine / kho cần được bảo toàn khi thêm báo cáo

Phase 5 đã xác định các trạng thái persisted: `PENDING`, `CONFIRMED`, `PREPARING`, `EXPORTED`, `SHIPPING`, `COMPLETED`, `CANCELLED`. Chuyển hợp lệ: Customer owner hủy `PENDING`; Sales/Admin xác nhận `PENDING → CONFIRMED`; Sales handoff `CONFIRMED → PREPARING`; Warehouse export `PREPARING → EXPORTED`; Sales tiến `EXPORTED → SHIPPING → COMPLETED`. Cancellation request là trạng thái riêng (`PENDING`, `APPROVED`, `REJECTED`) trên các trường hiện có, không được nhập vào `orders.status`; chỉ được yêu cầu/xét duyệt ở `CONFIRMED`/`PREPARING`, và pending request chặn export. Sau `EXPORTED`, không dùng luồng hủy này.

Tồn kho: Checkout RESERVE; xác nhận/handoff không reserve lại; hủy hợp lệ RELEASE một lần; export giảm tồn vật lý và reserved rồi ghi OUT trong cùng transaction; receipt/adjustment dùng IN/ADJUST theo Phase 5.4. Phase 6 reporting chỉ đọc số liệu và không gọi các command nghiệp vụ này.

## 5. Rủi ro và khoảng trống được ghi nhận tại thời điểm audit ban đầu

Danh sách dưới đây giữ nguyên quan sát tại thời điểm audit trước khi chủ dự án chốt policy; nó không còn là danh sách quyết định đang chờ. Mỗi chủ đề đã được giải quyết bởi decision tương ứng ở mục 5A. Trạng thái FR trong ma trận mục 3 không thay đổi theo việc duyệt policy.

1. **Account lock/session:** Login kiểm tra `IsActive`; chưa thấy enforcement tập trung cho mọi authenticated request sau khi cookie đã phát hành. `ConfigureApplicationCookie` hiện không thiết lập chính sách riêng để đạt invalidation tức thời. Cần chốt khóa là disable vĩnh viễn hay Identity lockout, thời hạn session revocation và cách xử lý cookie hiện hữu.
2. **Admin quyền cao nhất:** Quyết định ai được cấp role Admin, bootstrap/khôi phục Admin cuối cùng và thao tác tự hạ quyền chưa được thể hiện thành workflow Admin UI.
3. **Đánh giá:** `Status` hiện là string mặc định `PENDING`, không có enum/check, moderator id, moderation time hoặc reason. Không được tự coi mọi trạng thái hay nội dung hiện có là đã duyệt/ẩn.
4. **Đổi giá:** chưa có lịch sử truy vấn được. ILogger có thể tạo operational log nhưng không thay thế audit record bền vững nếu NFR yêu cầu truy xuất lâu dài.
5. **Doanh thu:** phải dùng `CompletedAt` UTC và số tổng giao dịch `Order.TotalAmount` sau giảm giá; timezone báo cáo và cách hiểu ngày kết thúc cần chốt. Thời gian SQL/DB không được đọc trong audit này.
6. **Sản phẩm bán:** khóa luận chưa định nghĩa rõ trạng thái đơn tính vào FR49. Đề xuất `COMPLETED`, trừ khi chủ dự án xác định khác.
7. **Low-stock:** cần ngưỡng do người xem báo cáo nhập/chọn; chưa có trường/table threshold trong schema. Không tự tạo ngưỡng tồn kho cố định hoặc dữ liệu giả.
8. **Logging:** NFR10 gồm role change, đổi giá và hành động nhạy cảm. Không log mật khẩu, reset token, security stamp, nội dung riêng tư không cần thiết; cần xác định sink/retention hiện được triển khai trước khi tuyên bố audit đầy đủ.

## 5A. Xác nhận policy sau audit — 9/9 OWNER APPROVED

Chủ dự án đã chốt đủ chín quyết định trong yêu cầu **PHASE 6.0 FINAL DESIGN POLICY CONSOLIDATION**. Bản chi tiết, transition và kỹ thuật dự kiến nằm trong [`PHASE6_DESIGN_DRAFT.md`](./PHASE6_DESIGN_DRAFT.md).

| # | Quyết định | Trạng thái | Kết quả chính |
|---:|---|---|---|
| 01 | Admin role grant — Option A | APPROVED | UI chỉ tạo/cấp Sales, Warehouse; cấp/khôi phục Admin qua quy trình đặc quyền nội bộ; giữ Admin active cuối cùng và chống race/self-demotion. |
| 02 | Account/role transition — Option A | APPROVED | Một account đúng một role; Customer không chuyển thành staff; chỉ Sales↔Warehouse; transaction và session claims được cập nhật. |
| 03 | Disable/session — Option A | APPROVED | Khóa bằng `IsActive=false`, persistent tới mở thủ công; từ chối login và authenticated request tiếp theo; LockoutEnd chỉ chống brute-force. |
| 04 | Staff invitation — Option A | APPROVED | Staff inactive, email invite/reset có expiry/replay protection, tự đặt password; delivery fail vẫn inactive; resend không duplicate; provider thật là nghiệm thu bắt buộc. |
| 05 | Review workflow — Option A | APPROVED | Owner + đúng order detail/product + COMPLETED; PENDING/APPROVED/HIDDEN; chỉ APPROVED public; customer không edit/delete, Admin không sửa nội dung. |
| 06 | Moderation audit — Option B | APPROVED | Không thêm audit columns/table; structured ILogger; chấp nhận không có persistent audit history. |
| 07 | FR49 product sales — Option A | APPROVED | Chỉ COMPLETED, filter CompletedAt, sum OrderDetail.Quantity theo ProductId; không suy doanh thu từ Product.Price. |
| 08 | FR50 low stock — Option A | APPROVED | Threshold nguyên không âm do Admin nhập mỗi request; Available=physical-reserved; missing Inventory và anomaly tách biệt; không ghi threshold. |
| 09 | Timezone/date — Option A | APPROVED | UI Asia/Ho_Chi_Minh, DB UTC, local inclusive dates chuyển thành UTC half-open range; FR47 OrderDate, FR48/49 CompletedAt. |

Đây là phê duyệt chính sách nghiệp vụ, không phải bằng chứng code đã đáp ứng. Decision 04 còn yêu cầu xác minh model/token behavior trong 6.1; Decision 06 chấp nhận giới hạn logging; timezone runtime cần test trên profile Windows/deployment thực tế. Không có migration/schema được duyệt trong lượt này.

## 6. Kết luận audit

Các module CRUD catalog/specification/consultation/promotion đã có nền tảng/acceptance từ các phase trước và không nên viết lại. Phase 6 thực sự còn trọng tâm ở account administration, Favorites, Customer Reviews, Review Moderation và Reporting; đồng thời cần harden các điểm giao quyền/session và quyết định về audit.

**Phase 6.0 audit/phân tích hoàn tất; business policies APPROVED 9/9; design docs được cập nhật để chủ dự án rà soát. Trạng thái FR/UC trong ma trận vẫn phản ánh code/evidence trước quyết định và chưa đổi. Phase 6.1–6.5 NOT STARTED; Phase 6 production NOT STARTED. Lượt này không có code, migration, database access, staging, commit hoặc push. Chờ chủ dự án xem xét bản thiết kế cập nhật trước mọi triển khai.**
