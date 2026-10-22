# NEXORA – Phase 6.5 Final Project Readiness Review

**Ngày rà soát:** 22/10/2026<br>
**Kết luận:** **BLOCKED – chưa sẵn sàng nghiệm thu chủ dự án**<br>
**Phạm vi:** review source Phase 6.1–6.4, chuẩn bị điều kiện kiểm thử và UAT. Không triển khai tính năng mới.

> “Build verified” chỉ xác nhận source biên dịch. Báo cáo này không xác nhận SQL integration tests, browser UAT, email thật, hoặc triển khai Production.

## Cập nhật SAFE TEST RECOVERY — 22/10/2026

Phần này là trạng thái mới nhất và **ghi đè mọi trạng thái test cũ bên dưới**.

- **Code Integrity:** Windows Security log `Microsoft-Windows-CodeIntegrity/Operational` ghi Event ID **3033** và **3077** khi `testhost.exe` từ `tests/NEXORA.Tests/bin/Debug/net10.0` và `tests/NEXORA.Tests/bin/Phase61SecurityRemediation/Debug/net10.0` nạp `NEXORA.Tests.dll`; lý do ghi trong event là không đạt Enterprise signing level / vi phạm Code Integrity policy. Policy ID được ghi nhận là `0283ac0f-fff1-49ae-ada1-8a933130cad6`. Không tìm thấy event AppLocker tương ứng trong log đã kiểm tra.
- **Vì sao 31 test trước chạy được:** lần chạy trước gọi `dotnet vstest` trực tiếp trên assembly ở output riêng dưới `%TEMP%`; kết quả process là 31 PASS. Nhật ký hiện có chỉ chứng minh hai vị trí trong repository bị chặn, không có bản ghi cho phép hoặc xác nhận quy tắc policy đã phê duyệt cho `%TEMP%`. Do thay đổi vị trí thực thi có thể làm thay đổi quyết định policy, **không thể xác nhận lần chạy đó tuân thủ policy**; không dùng 31 test này làm bằng chứng security acceptance và không lặp lại cách chạy đó.
- **Test execution mới:** không chạy testhost trong lượt này vì chưa có CI runner/test host hoặc signing/allowlisting được IT xác nhận. Không có test case mới PASS/FAIL/SKIPPED; trạng thái suite unit/HTTP là **BLOCKED / NOT RUN**. Không tạo TRX mới để tránh tạo bằng chứng giả. TRX lịch sử vẫn ở `%TEMP%\NEXORA-P1-Connection-FinalVerified-20261022-074352\TestResults\P1ConnectionSafetyAndProvisioning.trx`, nhưng chỉ ghi nhận lịch sử, không chứng minh tuân thủ policy.
- **Restore/build mới:** `dotnet restore NEXORA.sln` PASS. Build mặc định thất bại do process `NEXORA.Web (20752)` đang giữ khóa DLL trong `src/NEXORA.Web/bin/Debug/net10.0`; không dừng process đó. Build lại source hiện tại vào output tạm riêng (không chạy binary) PASS, **0 warning / 0 error**. Đây là bằng chứng biên dịch, không phải test.
- **Test filter đã rà soát để chạy trên runner được phê duyệt:** unit/policy: `AccountAuthenticationPolicyTests`, `DatabaseConnectionPolicyTests`, `AdminProvisioningCommandTests`, `StaffAccountStatePolicyTests`, `Phase6AuthorizationTests`, `Phase6EngagementPolicyTests`, `Phase6ReportingPolicyTests`, `BusinessPolicyTests`, `CommerceAuthorizationTests`, `SalesOrderAuthorizationTests`; HTTP host cô lập dùng fake stores/services: `AccountLoginPolicyHttpTests`, `AccountAuthenticationCookieHttpTests`, `AccountAdministrationHttpIntegrationTests`, `AccountProfileHttpIntegrationTests`, `AdminLayoutHttpIntegrationTests`, `Phase6EngagementHttpIntegrationTests`, `WarehouseAuthorizationHttpIntegrationTests`, `CartControllerAddResponseTests`. Các lớp này chưa được thực thi trong lượt này.
- **Coverage còn thiếu:** test invitation replay/expiry/resend/concurrency, ownership/review persistence và reporting SQL chỉ có bằng chứng ở nhóm SQL integration; không chạy. Các HTTP fake test chỉ xác minh controller/HTTP contract theo fake service, không thay thế SQL persistence.
- **Database/startup:** không mở SQL connection, không chạy SQL test, không khởi động website và không gọi EF command. Source review cho thấy Development config cục bộ bị `.gitignore` loại trừ và trỏ local SQL Express / `NEXORA`; source guard sẽ từ chối target đó. Các biến kết nối ở process/user hiện không được xác nhận là connection string QA hợp lệ. Không xác minh trạng thái database hoặc cấu hình của process website đang chạy.
- **Hành động cần IT/owner:** cung cấp CI runner/test host được phê duyệt theo Windows Code Integrity, hoặc hoàn tất ký/allowlist qua quy trình quản trị. Không chuyển DLL sang thư mục khác để tìm nơi policy cho chạy. Chỉ sau khi có runner được phê duyệt mới chạy filter unit/HTTP, xuất TRX gắn với source manifest; SQL tests vẫn cần QA target preflight và quyền riêng cho thao tác fixture ghi/xóa.

### Phân loại test theo khả năng chạm SQL

| Nhóm | Các test/source đã rà | Quyết định lượt này |
|---|---|---|
| A — Unit/policy, không tạo kết nối | Các lớp unit/policy nêu trên; `AppDbContextModelTests` chỉ đọc metadata; `ProductCatalogTests.CatalogFilter_ComposesCombinedDynamicConditionsIntoServerSideSql` gọi `ToQueryString()` để sinh SQL, không execute query | Không chạy do testhost chưa có môi trường được xác nhận cho phép |
| B — HTTP test host với fake | Các lớp HTTP nêu trên tự dựng `WebApplication` nhỏ, không chạy `Program.cs`, không gọi `AddNexoraInfrastructure`, dùng fake Identity store/service và cài test-only authentication | Static audit không thấy SQL provider/open connection/seeder; vẫn không chạy vì testhost execution chưa được policy phê duyệt |
| C — SQL / có thể ghi fixture | Tất cả `*SqlServerIntegrationTests` và lớp dùng `SqlServerFact`/`CheckoutSqlServerFact`, gồm account/invitation/session, dashboard/reporting, commerce/checkout, consultation, cancellation, engagement, product inventory, sales order, warehouse fulfillment/inventory và QA provisioning | **NOT RUN**; không đọc hoặc ghi database. `SqlServerFactAttribute` và `CheckoutSqlServerFactAttribute` kiểm tra allowlist `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; checkout còn cần opt-in riêng. Việc có guard không đồng nghĩa đã xác minh trạng thái database hoặc được phép chạy fixture.

### Startup và RoleSeeder — source-only review

- `Program.cs` gọi `DatabaseConnectionPolicy.Resolve(...)` trước `AddNexoraInfrastructure(...)`, tạo DI container hoặc mở scope. Guard chỉ parse/kiểm tra connection string; không mở SQL connection. Missing/invalid/unapproved Development/QA target phải fail-fast.
- `AddNexoraInfrastructure` chỉ đăng ký `AppDbContext`/SQL provider và services; không tự mở connection. Không tìm thấy `Database.Migrate`, `EnsureCreated` hoặc `EnsureDeleted` trong startup.
- Website startup bình thường gọi `RoleSeeder.EnsureRolesAsync()` **ở mọi environment** trừ command `--provision-qa-staff`. `RoleExistsAsync` có thể mở connection; nếu role trong bốn role chuẩn còn thiếu, `CreateAsync` ghi thêm dòng vào `roles`. Seeder không tạo user. Do đó QA preflight read-only phải xác nhận chính xác bốn role trước khi chạy website để startup không ghi dữ liệu.
- Ba demo seeders chỉ được gọi trong Development, không phải provisioning command, và khi `TargetsDevelopmentDatabase(connectionString)` trả true. Development policy mới chỉ chấp nhận QA target, trong khi predicate này nhận diện database `NEXORA`; vì vậy với resolver hiện tại predicate không đạt. QA environment không vào nhánh `IsDevelopment()`. Không có seeder nào được gọi trong lượt này.
- `--provision-qa-staff`, `--provision-admin` và `--seed-development-inventory` là command paths có thể ghi Identity/inventory; không được gọi. Lệnh QA staff tự preflight database nhưng có thể tạo account khi tiếp tục; lệnh Admin và inventory cần được coi là thao tác ghi tách biệt.
- Cấu hình đã kiểm tra bằng cách chỉ đọc tên nguồn và target không chứa credential: `appsettings.json` không cung cấp `DefaultConnection`; file ignored `appsettings.Development.json` có target local SQL Express / database `NEXORA`, nên bị guard từ chối. Các biến môi trường kết nối hiện diện ở process/user scope nhưng chưa xác minh là connection string QA hợp lệ; không sử dụng và không thử kết nối. Không đọc/hiển thị password hay connection string đầy đủ.

### Điều kiện an toàn trước khi chạy website QA

1. IT xác nhận runner hợp lệ; fresh tests thực thi từ đúng source snapshot và phát TRX mới.
2. Chủ dự án phê duyệt riêng read-only QA preflight; xác minh instance/database ONLINE, migration/schema và bốn role. Sai khác thì dừng.
3. Xác minh effective connection string chỉ trỏ `PC01\SQLEXPRESS / NEXORA_IntegrationTests`, Windows Authentication; không dùng `appsettings.Development.json` hiện tại.
4. Giải quyết Change Request `NEXORA-P61-QA-ROLE82` bằng phê duyệt riêng trước khi bật fail-closed guard. Không sửa UserId 82 trong lượt này.
5. Vì startup RoleSeeder có thể tạo role thiếu, chỉ khởi chạy sau khi preflight xác nhận role set đầy đủ; dùng source build mới sau khi có binary mới và cổng được owner chọn.
6. Sau đó mới thực hiện browser/manual UAT, cấu hình email provider QA và kiểm thử gửi mail sau khi owner xác nhận mailbox + đồng ý gửi.

**Kết luận cập nhật:** **BLOCKED** — chưa có bằng chứng test execution tuân thủ Code Integrity, chưa có fresh TRX, không có QA preflight, Admin multi-role blocker chưa được phê duyệt, email thật và manual UAT chưa chạy. Phase 6 không được đánh dấu COMPLETED.

## 1. Tóm tắt readiness

| Hạng mục | Trạng thái | Bằng chứng / giới hạn |
|---|---|---|
| Phase 6.1–6.4 source review | **VERIFIED – static review** | Đã đối chiếu controller, service, DI, routes, authorization, query và traceability. Không chạy runtime trong lượt này. |
| Restore | **DONE** | dotnet restore NEXORA.sln thành công, mọi project up-to-date. |
| Build | **VERIFIED** | Fresh restore và dotnet build NEXORA.sln --no-restore với OutDir riêng thành công, 0 warning, 0 error. |
| P1 connection fail-closed | **VERIFIED – source; test evidence BLOCKED** | Đã bỏ design-time fallback. Có một lần chạy lịch sử 31 test từ `%TEMP%`, nhưng không xác minh được policy đã cho phép vị trí đó; không tính là security acceptance. |
| Unit/HTTP/SQL tests của source hiện tại | **BLOCKED / NOT RUN** | Windows Code Integrity chặn testhost ở output repository. Chưa có runner hoặc signing/allowlist được IT xác nhận. Không có TRX mới. |
| SQL Server QA preflight | **NOT TESTED** | Không mở kết nối SQL trong lượt này. Chưa xác minh server/database ONLINE, schema, migration history hoặc role set. |
| Browser/manual UAT | **NOT TESTED** | Không mở hoặc thao tác website trong lượt này. |
| Real email UAT | **BLOCKED / NEEDS OWNER APPROVAL** | Không có sender thật được cấu hình trong code; non-Development dùng UnconfiguredEmailSender. Chưa đăng ký provider, gửi email hoặc thay credential. |
| UserId 82 / Admin lockout | **BLOCKED / NEEDS OWNER APPROVAL** | Preflight lịch sử ghi nhận UserId 82 là Admin active duy nhất và đồng thời có role Customer. Source mới từ chối account nhiều role. Không đọc lại hoặc sửa mapping trong lượt này. Change NEXORA-P61-QA-ROLE82 chưa được duyệt thực thi. |
| Schema / migration | **VERIFIED – không đổi** | Không sửa Entity, AppDbContext model mapping, migration hoặc snapshot. Không tạo/chạy migration. |
| Git/source preservation | **VERIFIED** | Đầu lượt remediation: 76 mục (19 modified + 57 untracked), 0 staged; HEAD ab5d945018b89d69beb72d9897594cf270e15949 trên master. Tất cả 76 mục còn nguyên; remediation thêm 4 modified và 2 untracked, hiện tổng 82 mục (23 modified + 59 untracked), 0 staged. |

Vì test execution, QA database preflight và owner UAT chưa đạt, không đánh dấu Phase 6 hoặc toàn dự án COMPLETED.

## 2. Source, yêu cầu và quyết định nghiệp vụ

### 2.1 Phạm vi code được tìm thấy

- **Phase 6.1 – Account Administration:** tìm kiếm/xem tài khoản, tạo Sales/Warehouse bằng invitation, lock/unlock, đổi Sales ↔ Warehouse và kiểm tra trạng thái account/cookie.
- **Phase 6.2 – Favorites/Reviews:** Favorites theo Customer hiện tại; gửi và xem review gắn với OrderDetail đủ điều kiện.
- **Phase 6.3 – Review Moderation:** Admin xem hàng đợi, duyệt/ẩn/khôi phục review.
- **Phase 6.4 – Dashboard/Reporting:** số liệu dashboard; báo cáo đơn, doanh thu, sản phẩm bán và tồn kho.
- Không thấy TODO, NotImplementedException, href="#" hoặc route/action trống trong các controller/views đã rà soát. Đây là kiểm tra tĩnh theo các mẫu trên, không thay thế browser UAT.

### 2.1.1 Dependency injection, MVC và validation

- `Program.cs` đăng ký `AddNexoraInfrastructure(connectionString)`, ASP.NET Core Identity với EF store `AppDbContext`, cookie validation authoritative và MVC controllers/views. Identity và account administration dùng chung `AppDbContext`; không thấy hệ thống xác thực thứ hai.
- Route controllers được map qua conventional Area route và default MVC route. Các Reports/Reviews/Users Admin controller dùng `[Authorize(Roles = Admin)]`; mutating actions kiểm tra antiforgery. Favorites/Reviews Customer controller dùng `[Authorize(Roles = Customer)]`, lấy user ID từ claim và POST có antiforgery.
- Invitation activation đặt token trong URL fragment; JavaScript xóa fragment khỏi address bar trước khi fetch validation, antiforgery được gửi trong request body/header. Đây là source review; browser execution chưa được làm.
- Report filters kiểm tra `DateOnly` range và threshold; report pages có giới hạn kích thước. Những lỗi runtime/database được chuyển tới exception middleware theo environment; error-path runtime chưa được kiểm thử.
- Query report dùng `AsNoTracking`, server-side aggregate/grouping và page limit. Không có query plan/latency benchmark; phần performance chỉ được review tĩnh.

### 2.2 Coverage đối chiếu

| Yêu cầu | Module / Use Case | Đánh giá source hiện tại |
|---|---|---|
| FR34–FR36; UC-A01–A03; BR08; NFR02/NFR03/NFR10 | Admin account/role/invitation | Có controller Admin-only và service Identity; UI không cấp Admin, chỉ tạo Sales/Warehouse và chuyển Sales ↔ Warehouse. Test source hiện tại chưa chạy. |
| FR12; UC-C10; BR09 | Favorites | User ID lấy từ principal; Customer authorization; POST antiforgery; đọc/xóa theo owner; xử lý add trùng. Runtime chưa kiểm chứng. |
| FR17; UC-C15; BR02 | Review của Customer | Service kiểm tra owner, đơn COMPLETED và dòng OrderDetail/sản phẩm tương ứng; trạng thái mới PENDING; Customer xem dữ liệu của mình. Runtime/SQL concurrency chưa kiểm chứng. |
| FR44; UC-A11; NFR03/NFR10 | Review moderation | Admin authorization; mutation POST có antiforgery; transition có điều kiện cập nhật; status lạ fail-closed; structured ILogger. Theo Decision 06 không có moderation-history table, logger không phải audit history bền vững. |
| FR46 | Dashboard | Query service lấy số liệu từ database; không thấy số doanh thu/đơn/tồn giả trong các card đã rà soát. Runtime query và so khớp UI chưa kiểm chứng. |
| FR47; UC-A13 | Báo cáo đơn | Group theo Order.OrderDate, khoảng UTC nửa mở từ ngày giờ Việt Nam; query server-side. SQL runtime chưa kiểm chứng. |
| FR48; UC-A14; BR13–BR14 | Doanh thu | Chỉ COMPLETED có CompletedAt; cộng Order.TotalAmount ở cấp Order để tránh fan-out với OrderDetail. SQL timezone translation chưa kiểm chứng. |
| FR49; UC-A15 | Sản phẩm bán | Chỉ COMPLETED; sum Quantity theo ProductId và lấy tên từ snapshot OrderDetail. Giá hiện tại không dùng để suy ra doanh thu. SQL runtime chưa kiểm chứng. |
| FR50; UC-A16–A17 | Tồn kho | Tách NORMAL/LOW/OUT_OF_STOCK/MISSING/ANOMALY; available = stock - reserved; Admin chỉ đọc; threshold không âm. SQL/runtime chưa kiểm chứng. |

Đối chiếu chi tiết trong [REQUIREMENTS_TRACEABILITY.md](./REQUIREMENTS_TRACEABILITY.md), [PHASE6_DESIGN_DRAFT.md](./PHASE6_DESIGN_DRAFT.md), [PHASE6_IMPLEMENTATION_PLAN.md](./PHASE6_IMPLEMENTATION_PLAN.md) và các acceptance bên dưới. Traceability có nội dung lịch sử 240/240 ở phần Phase 6.1, trong khi acceptance 6.1 xác định kết quả đó là **historical, trước guard single-role**. Không dùng kết quả cũ làm bằng chứng của source hiện tại; nên hợp nhất cách ghi trạng thái trước nghiệm thu.

### 2.3 Chín quyết định nghiệp vụ đã duyệt

1. Admin UI chỉ tạo Sales/Warehouse, không cấp Admin; bảo vệ Admin cuối cùng, cấm tự khóa/tự hạ quyền và chống race.
2. Một account có đúng một trong bốn role; Customer không chuyển thành nhân viên; Sales ↔ Warehouse là chuyển role nhân viên duy nhất; thu hồi phiên cũ.
3. IsActive là khóa nghiệp vụ; request xác thực tiếp theo phải từ chối cookie; LockoutEnd dành cho chống brute-force.
4. Invitation có hạn dùng/chống replay; người nhận tự đặt password; lỗi email không kích hoạt account; resend dùng lại account; không log token/password.
5. Review chỉ từ Customer sở hữu đơn COMPLETED và OrderDetail tương ứng; review mới PENDING; chỉ APPROVED công khai; không cho Customer sửa/xóa sau gửi.
6. Moderation dùng structured ILogger; không thêm cột/bảng lịch sử. Giới hạn độ bền/truy vấn của log được chấp nhận.
7. FR49 chỉ COMPLETED, theo CompletedAt, sum OrderDetail.Quantity theo ProductId.
8. FR50 dùng threshold không âm; available = stock - reserved; dữ liệu thiếu/anomaly phải phân biệt; Admin chỉ đọc.
9. Báo cáo dùng ngày Việt Nam, lưu UTC, range nửa mở; FR47 theo OrderDate, FR48/49 theo CompletedAt và chỉ COMPLETED.

**Static policy review:** code hiện có guard role duy nhất trong login/cookie validation, account state check, stamp check, Admin-only routes và customer-owned data queries như thiết kế. Việc kiểm thử theo 9 quyết định trên đang **BLOCKED / NOT TESTED**; static review không xác nhận trạng thái runtime hoặc database.

## 3. Vấn đề và rủi ro phát hiện

### 3.1 P1 – EF design-time fallback trỏ database chính — ĐÃ KHẮC PHỤC TRONG SOURCE

`AppDbContextFactory` trước đây dùng fallback tới database chính khi thiếu `ConnectionStrings__DefaultConnection`; một lệnh EF design-time có thể vì vậy chọn nhầm target. Factory hiện chỉ đọc biến môi trường tường minh, sau đó gọi `DatabaseConnectionPolicy.Resolve`. Thiếu/rỗng/sai cú pháp sẽ fail-fast bằng thông báo chung không chứa input. Không còn fallback.

`DatabaseConnectionPolicy` được dùng ở startup và design-time: Development/QA chỉ chấp nhận `PC01\SQLEXPRESS` / `NEXORA_IntegrationTests`, Windows Authentication, không SQL credentials, attach database hoặc User Instance. Các môi trường khác vẫn có thể dùng SQL Server hợp lệ khi connection được cấu hình tường minh; phê duyệt triển khai Production vẫn là quy trình vận hành riêng, không được mã nguồn suy diễn.

`AddNexoraInfrastructure` yêu cầu caller truyền environment và dùng cùng resolver trước khi cấu hình EF. `QaStaffProvisioningCommand` dùng cùng allowlist. Lệnh seed inventory kiểm tra allowlist QA trước khi lấy service; target Development NEXORA bị từ chối. Không chạy EF tooling, migration, SQL hay website trong lượt remediation.

Focused tests xác nhận thiếu/rỗng/sai cú pháp, từ chối database chính ở Development/QA, chấp nhận target QA mà không mở connection, hỗ trợ cấu hình Production tường minh, không lộ chuỗi secret trong lỗi, factory không fallback, và provisioning inventory từ chối database chính.

### 3.2 P1 – Cấu hình Development cục bộ nhắm database chính

src/NEXORA.Web/appsettings.Development.json là file bị .gitignore loại trừ. Kiểm tra metadata cục bộ (không in connection string/credential) cho thấy target hiện tại vẫn là database NEXORA chính. Với guard mới, Development startup sẽ từ chối cấu hình này trước khi đăng ký DbContext; cần override bằng QA target tường minh nếu chủ dự án muốn chạy QA. Không thay đổi file ignored này và không mở connection.

Program.cs gọi `RoleSeeder.EnsureRolesAsync()` khi startup bình thường, ở mọi environment; khi target QA được cấu hình, đây vẫn là thao tác ghi nếu role thiếu. Trước QA startup cần preflight read-only xác nhận đúng bốn role đã tồn tại; nếu không khớp, dừng. Catalog/consultation/inventory startup seeders không chạy trên target QA. Lệnh inventory explicit hiện từ chối target không nằm trong allowlist QA. Không dùng guard connection thay cho preflight dữ liệu.

`DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase` vẫn là predicate legacy nhận diện target NEXORA trong nhánh auto-seeding. Nó không mở connection; `Program.cs` resolve/kiểm tra môi trường trước khi tới nhánh này nên Dev không thể khởi động bằng target đó, còn QA không khớp predicate và bị skip. Nên xóa/đổi tên predicate trong đợt cleanup riêng để tránh bị tái sử dụng sai.

### 3.3 P1 – Guard single-role và UserId 82

AuthoritativeAccountCookieValidation gọi AccountAuthenticationPolicy.HasCurrentSingleRole, từ chối cookie nếu persisted roles hoặc cookie claims không có đúng một role chuẩn hoặc không khớp nhau. Preflight lịch sử ghi UserId 82 là Admin active duy nhất nhưng có Admin + Customer; source guard hiện tại sẽ từ chối đăng nhập/phiên account đó. Đây là fail-closed policy đúng như quyết định đã duyệt, nhưng tạo **Admin lockout risk** với dữ liệu QA hiện có.

Không khởi động/triển khai guard lên QA, không tự cấp Admin mới, không sửa UserId 82 và không dùng ngoại lệ trong code. Change NEXORA-P61-QA-ROLE82 cần được duyệt riêng trước mọi thay đổi mapping. Nếu preflight mới khác dữ liệu lịch sử, dừng và xin xác nhận.

### 3.4 P1 – Hướng dẫn UAT 6.1 đang lỗi thời

docs/PHASE6_1_MANUAL_UAT.md còn ghi QA URL :5060, trạng thái cookie validator cũ cho phép account nhiều role khớp claims, và lệnh chạy DLL trong tests/NEXORA.Tests/bin/Phase61. Listener hiện tại cho thấy:

- 127.0.0.1:5060 chạy tests/NEXORA.Tests/bin/Phase61/Debug/net10.0/NEXORA.Web.dll – **không phải binary source mới vừa build**.
- 127.0.0.1:5059 chạy D:\NEXORA-CatalogCart-Build\bin\NEXORA.Web\debug\NEXORA.Web.dll – output ở thư mục build khác.

Hai listener không bị dừng hoặc gọi HTTP. Không dùng các URL/process này làm bằng chứng UAT. Cần thay phần “Verified QA runtime and accounts” và command cũ trong checklist trước khi chạy owner UAT. Không sao chép UserId/password/token hoặc snapshot cũ thành tài khoản hiện hành.

### 3.5 P2 – Query/report và nhãn lịch sử

- Reporting dùng AsNoTracking, aggregate/grouping phía SQL và paging giới hạn; tuy nhiên chưa có runtime SQL result, execution plan hoặc latency baseline.
- Product sales label dùng Max(ProductName) trong các snapshot theo ProductId. Số lượng vẫn group đúng, nhưng tên hiển thị có thể là tên snapshot theo thứ tự từ điển, không nhất thiết snapshot mới nhất. Nên ghi đây là giới hạn hiển thị; chưa đổi nghiệp vụ/report trong lượt này.
- Revenue query sử dụng SQL Server AT TIME ZONE; chưa kiểm tra query translation và biên ngày thật trên SQL Server trong source hiện tại.
- Account-state check đọc dữ liệu xác thực tại mỗi request. Cần đo tải/query latency ở QA; không có benchmark trong lượt này.
- FR45 đã có sửa giá sản phẩm và order-line price snapshots, nhưng NFR10 **price-change operational logging còn thiếu**. Chưa có log actor/old price/new price/outcome và không có price history persistence. Đây là gap đã mô tả trong design; chưa tự thêm bảng/tính năng. Cần chủ dự án chốt scope logging trước khi sửa.

### 3.6 Security/configuration scan

- .gitignore loại bin/, obj/, .vs, TestResults/, .env, local Development settings, secret files, key files và uploads. Không có cấu hình local hoặc build output trong danh sách 75 mục Git.
- Pattern scan các file đang sửa/untracked thấy các chỗ khai báo thuộc tính PasswordHash, connection-string test cases và test input giả; không tìm thấy bằng chứng credential provider/production key bị commit. Không in hoặc chép giá trị nhạy cảm vào báo cáo.
- SQL test connection không được hiển thị hoặc dùng để mở kết nối trong lượt này. Các SQL Fact guards được rà soát tĩnh; chỉ bộ lọc configuration tests được thực thi.
- TRX của lần chạy 31 test lịch sử được lưu ngoài repository trong `%TEMP%`; không có TRX mới trong lượt SAFE TEST RECOVERY. Không có log/test database dump trong Git status. SQL fixture execution chưa diễn ra.

## 4. Test execution plan và trạng thái

### 4.1 Kết quả remediation trước đây — lịch sử, không phải bằng chứng được chấp nhận

| Lệnh/kiểm tra | Kết quả |
|---|---|
| dotnet restore NEXORA.sln | **PASS (lượt hiện tại)** – mọi project up-to-date. |
| dotnet build NEXORA.sln --no-restore, output mặc định | **FAIL do file lock** – website `NEXORA.Web (20752)` giữ các dependency DLL. Không dừng website. |
| dotnet build NEXORA.sln --no-restore, OutDir tạm chỉ để biên dịch | **PASS (lượt hiện tại)** – 0 warning, 0 error tại `%TEMP%\NEXORA-Phase6-SafeValidation-20261022`; không chạy website hay testhost từ output này. |
| 31 focused connection/provisioning tests | **Lịch sử: PASS 31 / FAIL 0 / SKIP 0**, gọi `dotnet vstest` từ output riêng `%TEMP%`. Windows Code Integrity có log chặn testhost ở output repository; không có bằng chứng cho phép `%TEMP%`. Vì vậy tuân thủ policy **UNVERIFIED**, không dùng làm bằng chứng hiện tại. TRX lịch sử: `%TEMP%\NEXORA-P1-Connection-FinalVerified-20261022-074352\TestResults\P1ConnectionSafetyAndProvisioning.trx`. |
| Unit/HTTP tests được liệt kê trong cập nhật SAFE TEST RECOVERY | **BLOCKED / NOT RUN** – chưa có môi trường thực thi được IT xác nhận; không có TRX mới. |
| SQL Server integration tests | **NOT RUN** – không đọc/ghi database; không kết nối QA hoặc database chính. |
| Historical test counts | **Không dùng làm bằng chứng source hiện tại.** 240/240 trước guard mới và 143/143/63 HTTP từ Phase 4/5 vẫn chỉ là kết quả lịch sử. |

Lần chạy lịch sử gồm 17 case `DatabaseConnectionPolicyTests` (missing/malformed/redaction, QA-vs-main policy, design-time factory và inventory guard) cùng 14 case `AdminProvisioningCommandTests`. Các case này đã trả PASS trong lần gọi nói trên, nhưng lần chạy không được xem là policy-verified; không suy diễn sang login/HTTP/invitation hoặc toàn source.

Tên test chính xác trong TRX:

| Phạm vi | Test thực thi |
|---|---|
| Missing/empty/whitespace | `Resolve_RejectsMissingConnectionString` – 3 data cases |
| Parser/redaction | `Resolve_RejectsMalformedConnectionWithoutEchoingInput` |
| Chặn database chính trong Dev/QA và ở DI | `Resolve_RejectsMainDatabaseInDevelopmentAndQa` – 2 cases; `InfrastructureRegistration_RejectsMainDatabaseInDevelopment` |
| Target QA / Production explicit / credential mode | `Resolve_SelectsApprovedQaConfigurationWithoutOpeningConnection` – 2 cases; `Resolve_AllowsExplicitProductionConfiguration`; `QaPolicy_RejectsSqlAuthenticationAndAttachedDatabase` |
| EF design-time | `DesignTimeFactory_FailsWhenConnectionIsMissingAndDoesNotUseFallback`; `DesignTimeFactory_DevelopmentArgumentCannotBypassQaTargetGuard`; `DesignTimeFactory_AspNetCoreDevelopmentEnvironmentRequiresQaTarget`; `DesignTimeFactory_RequiresExplicitConnectionAndSelectsQaWithoutConnecting` |
| Provisioning | `DevelopmentInventoryProvisioning_RejectsMainDatabaseBeforeResolvingServices` |

### 4.2 Quy trình runner hợp lệ

1. Chủ dự án/IT chọn **CI runner riêng đã được phê duyệt** hoặc Windows test host được phê duyệt theo Code Integrity. Bộ test lọc hiện tại chạy được; nếu full test host bị chặn lại, IT ký/allowlist publisher hoặc artifact qua quy trình change control chuẩn. Không chuyển thư mục, đổi tên DLL, tắt policy hoặc thực hiện cách khác để né chặn.
2. Đưa đúng source snapshot vào runner theo kênh nội bộ được phép, bao gồm toàn bộ tracked modifications và untracked source/docs cần thiết, không chỉ Git HEAD. Không tải source hoặc backup lên dịch vụ ngoài khi chưa được chủ dự án cho phép.
3. Source identity hiện tại: HEAD ab5d945018b89d69beb72d9897594cf270e15949, 82 Git status entries (23 modified + 59 untracked), 0 staged. Tất cả 76 mục tồn tại đầu lượt được giữ; 6 mục remediation được thêm. Manifest trước remediation không còn đại diện cho source này; runner phải tính lại manifest/hash cho payload hiện tại và xác nhận các local ignored settings/secrets không được chuyển.
4. Runner thực hiện restore/build/test từ clean isolated output, xuất TRX mới gắn với commit + source manifest hash và lưu trong vùng QA được phép. Báo rõ test case, PASS/FAIL/SKIPPED và môi trường.
5. Trước SQL tests, runner phân tích connection string mà không in secret và bắt buộc khớp Windows Authentication + PC01\SQLEXPRESS + NEXORA_IntegrationTests. Sau đó query read-only xác nhận DB ONLINE, 27 bảng (22 nghiệp vụ, 4 Identity, history), migration history dự kiến và role set. Sai một điều kiện thì SQL tests **NOT RUN**. Không kết nối NEXORA chính.
6. Chạy Unit/HTTP trước; chỉ bật SQL test categories sau preflight. Dùng guards hiện có SqlServerFactAttribute, CheckoutSqlServerFactAttribute và các guard riêng của account tests. Không dùng database main hoặc đổi connection sang target khác để làm test pass.

**Điều kiện hiện tại:** không có test mới chạy được theo một phương thức đã xác nhận tuân thủ Code Integrity. Full Unit/HTTP regression **BLOCKED**; SQL tests **NOT RUN** cho tới khi runner được phê duyệt, QA target preflight hoàn tất và được cấp quyền riêng cho fixture ghi/xóa.

## 5. Database safety và chạy website QA

### 5.1 Trạng thái database

- Không kết nối PC01\SQLEXPRESS / NEXORA chính.
- Không kết nối QA trong lượt này; các biến môi trường không được dùng để mở kết nối và chưa được xác nhận hợp lệ.
- Không đọc schema/migration/users/roles mới, không ghi dữ liệu, không chạy migration.
- Không biết trạng thái hiện hành ONLINE hoặc số bảng/migration hiện tại từ lượt này. Snapshot trong tài liệu trước là historical, không phải preflight lần này.

### 5.2 Rủi ro website đang chạy

Ở lượt readiness trước, port 5059 và 5060 được quan sát là có listener dùng binary cũ; trạng thái port không được kiểm tra lại trong remediation này. Không dùng các listener đó làm bằng chứng cho source mới, không kết thúc process cũ.

Build source mới nhất cho remediation nằm tại:

`%TEMP%\NEXORA-P1-Connection-FinalVerified-20261022-074352\NEXORA.Web.dll`

Đây là output build riêng, không được chạy làm bằng chứng website; website QA chưa được khởi động hoặc triển khai. Vẫn cần read-only QA preflight và giải quyết Admin lockout blocker trước khi owner UAT.

### 5.3 Lệnh chạy local QA sau khi blocker được giải quyết

Đây là hướng dẫn dự kiến, **không thực thi trong lượt này**. Trước lệnh cần read-only preflight trên đúng QA database, xác nhận ONLINE, schema/history đúng, role set đủ bốn role, và Change NEXORA-P61-QA-ROLE82 đã được duyệt/thi hành trong lượt riêng. Nếu mapping khác expected hoặc role thiếu, dừng.

Sau preflight, kiểm tra port dự kiến còn trống; nếu không, chọn port khác và cập nhật base URL. Không dùng 5059/5060:

    $env:ConnectionStrings__DefaultConnection = $env:NEXORA_SQLSERVER_TEST_CONNECTION
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:StaffInvitation__PublicBaseUrl = 'http://127.0.0.1:5075'
    dotnet "$env:TEMP\NEXORA-P1-Connection-FinalVerified-20261022-074352\NEXORA.Web.dll" --contentRoot 'D:\Projects\NEXORA\src\NEXORA.Web' --urls 'http://127.0.0.1:5075'

Trước khi chạy phải kiểm tra connection target mà không in giá trị; abort nếu không khớp chính xác PC01\SQLEXPRESS / NEXORA_IntegrationTests. Với target QA, Development seeders trong Program.cs được guard để bỏ qua; role initializer vẫn chạy nên chỉ tiến hành nếu preflight xác nhận bốn role đã tồn tại. Development mode dùng in-memory mailbox và Developer Exception Page; chỉ dùng trên loopback, không expose ra mạng. QA/non-Development không có sender thật và invitation sẽ không chứng minh được email thật.

Tại thời điểm review, UserId 82 là multi-role theo preflight lịch sử và guard mới từ chối account đó; vì vậy lệnh trên **chưa đủ điều kiện để chạy**. Không login bằng user đó cho tới khi approved remediation hoàn tất. Không tự tạo Admin khác.

## 6. Manual UAT checklist – tất cả NOT RUN

### Điều kiện chung

- Chỉ dùng website được build từ source snapshot hiện tại, sau QA preflight và sau xử lý Admin blocker được phê duyệt. Ghi lại URL/port thực tế, build manifest, browser/version, thời điểm.
- Role truy cập phải đúng một trong Customer, Sales, Warehouse, Admin. Dùng QA fixtures được owner duyệt; không sửa user/order/review/inventory hiện hữu để tạo tình huống.
- Với thao tác tạo/đổi dữ liệu, chỉ dùng fixture QA riêng được chủ dự án chấp thuận và ghi ID. Không lưu password, invitation URL/token, hash, security stamp hoặc dữ liệu khách thật vào evidence.
- Mỗi test case cần ghi: NOT RUN/PASS/FAIL/BLOCKED, người chạy, thời điểm, URL, browser/device, kết quả quan sát, đường dẫn screenshot/log đã làm sạch. Hiện tất cả case dưới đây đều **NOT RUN**.

### 6.1 Account Administration – A01–A20

Chi tiết: [PHASE6_1_MANUAL_UAT.md](./PHASE6_1_MANUAL_UAT.md). File đó hiện có snapshot/command lỗi thời như mục 3.4; trước UAT phải thay phần runtime và role state. Không dùng link :5060 hoặc Phase61 binary. Cases A07–A18 thay đổi QA fixtures, cần owner cho phép tạo fixture riêng; A19 không được chạy trên UserId 82.

| Case | Role / URL | QA data & thao tác | Expected | Evidence |
|---|---|---|---|---|
| A01 | Admin · /Admin/Users/Index | Account-list QA, đọc danh sách | Trang mở; không lộ credential/token/hash/stamp | Screenshot đã che dữ liệu + URL |
| A02 | Anonymous, Customer, Sales, Warehouse, Admin · /Admin/Users/Index | Account đúng single-role cho mỗi actor | Chỉ Admin được phép; anonymous yêu cầu login; role khác bị từ chối | Mỗi role, status/redirect |
| A03 | Admin · /Admin/Users/Details/{id} | Một QA account được duyệt | Hiển thị role/state/timestamp, không credential material | Screenshot redact |
| A04 | Admin · /Admin/Users/Index?search=... | Tên marker QA riêng | Search chỉ trả record phù hợp | URL query + kết quả redact |
| A05 | Admin · /Admin/Users/Index?role=...&isActive=...&page=1 | QA accounts có role/state phù hợp | Filter/pagination bounded; input sai không crash | URL + page result |
| A06 | Admin · /Admin/Users/CreateStaff | Không cần fixture ghi dữ liệu | Chỉ chọn Sales/Warehouse; không có password field | Screenshot form |
| A07 | Admin · /Admin/Users/CreateStaff, /dev/mailbox | Tạo Sales bằng địa chỉ test riêng được duyệt | Pending/inactive, Sales-only, chưa có password; mail trong mailbox giả lập | Fixture ID + trạng thái, không token |
| A08 | Admin · cùng URLs | Tạo Warehouse QA riêng | Pending/inactive, Warehouse-only; không email ngoài | Fixture ID + trạng thái |
| A09 | Admin · /Admin/Users/CreateStaff | Duplicate/malformed QA input | Validation từ chối, không tạo account dở dang | Request result + fixture ID |
| A10 | Admin đọc mailbox; người nhận · /account/ActivateStaff | Invitation QA hợp lệ | Fragment bị xóa khỏi address bar trước fetch; antiforgery POST; người nhận tự đặt password | Screenshot không chứa URL token |
| A11 | Người nhận · /account/ActivateStaff | Fixture link sai/hết hạn/đã dùng | Thông báo generic; account không đổi | UI + fixture state; không token |
| A12 | Admin · /Admin/Users/Details/{id}, /dev/mailbox | Pending fixture riêng | Resend không tạo account mới; link cũ fail, link mới hợp lệ | Fixture ID + mailbox metadata che token |
| A13 | Admin khóa; staff QA dùng phiên đã login · route protected theo role | Staff fixture active | Request protected kế tiếp từ chối stale cookie | Browser/status + thời điểm |
| A14 | Admin · /Admin/Users/Details/{id}; staff login mới | Fixture vừa khóa, đã có password | Unlock tường minh; login mới hoạt động; session cũ không tự phục hồi | Trạng thái + login result |
| A15 | Admin · /Admin/Users/Details/{id} | Pending fixture chưa password | Unlock bị từ chối, vẫn pending/inactive | UI + fixture state |
| A16 | Admin đổi role; Sales fixture · /Admin/Users/Details/{id} | Account Sales riêng | Sales→Warehouse nguyên tử; cookie cũ bị từ chối | Role trước/sau + stale-session result |
| A17 | Admin đổi role; Warehouse fixture · cùng route | Account Warehouse khác | Warehouse→Sales; cookie cũ bị từ chối | Role trước/sau + stale-session result |
| A18 | Admin · /Admin/Users/CreateStaff | Form/request QA có role bị sửa | Server từ chối Customer/Admin assignment | HTTP/UI; không elevated mapping |
| A19 | Admin · /Admin/Users/Details/{id} | Chỉ sau khi có Admin role sạch và được duyệt | Self-lock/self-demotion/last-admin action bị từ chối | Kết quả + trạng thái; không dùng UserId 82 trước CR |
| A20 | Admin/Customer/Sales/Warehouse/anonymous · /dev/mailbox | Development loopback mailbox | Chỉ Admin local trong Development được truy cập; role khác bị từ chối | HTTP/status; không chụp token |

### 6.2 Favorites và Customer Reviews – F01–F04, R01–R06, cross-user

Chi tiết thao tác và expected result: [PHASE6_2_MANUAL_UAT.md](./PHASE6_2_MANUAL_UAT.md). Cần QA product active; review cần hai Customer và Order COMPLETED có OrderDetail chưa review. Không sửa đơn hiện có; nếu fixture không có, xin phép tạo dữ liệu QA riêng trước.

| Case | Role / URL | Dữ liệu, thao tác | Expected | Evidence |
|---|---|---|---|---|
| F01 | Customer · /Catalog, /Favorites | Thêm favorite cho product active | Thêm một lần; status của chính Customer | Screenshot + product ID |
| F02 | Customer · /Catalog, /Favorites | Thêm lại cùng product | Không duplicate; phản hồi already saved | Count before/after |
| F03 | Customer · /Favorites | Xóa một favorite QA | Chỉ favorite được chọn biến mất | Screenshot + owner fixture |
| F04 | Customer · /Favorites | Account không favorite hoặc xóa hết fixture của chính nó | Empty state và link Catalog | Screenshot |
| R01 | Customer sở hữu đơn · /Reviews | COMPLETED order line chưa review; gửi rating 1–5/comment | Review PENDING, chỉ owner thấy ban đầu | Order/line fixture ID, UI |
| R02 | Customer · /Reviews | Dòng đơn không COMPLETED | Server từ chối, không review | Response + fixture state |
| R03 | Customer A · /Reviews | Đổi OrderDetailId sang line của B với antiforgery của A | Owner check từ chối; không đọc/sửa data B | Result, che PII |
| R04 | Customer · /Reviews, product detail | Rating ngoài 1–5, comment >3.000; harmless HTML literal | Validation đúng; output encode, script không chạy | Validation/UI capture |
| R05 | Customer owner + visitor/Customer khác · /Reviews, /Catalog/Details/{id} | Review PENDING rồi Admin duyệt ở Phase 6.3 | Owner thấy status; pending không public; APPROVED mới public | Trạng thái trước/sau |
| R06 | Customer · /Reviews | Xem review đã gửi | Không có edit/delete control/route hữu ích | Screenshot + access result |
| Cross-user | Hai Customer · /Favorites, /Reviews | Hai account QA riêng | Chỉ data của principal hiện tại | So sánh fixture IDs |

### 6.3 Review Moderation – M01–M08

Chi tiết: [PHASE6_3_MANUAL_UAT.md](./PHASE6_3_MANUAL_UAT.md). Cần Admin single-role sau remediation và review QA thật tạo theo R01; không tạo review giả hoặc sửa DB thủ công.

| Case | Role / URL | QA data & thao tác | Expected | Evidence |
|---|---|---|---|---|
| M01 | Admin · /Admin/Reviews | Review QA, filter/search/page | Queue/filter/paging đúng; content read-only | Query URL + screenshot |
| M02 | Admin · /Admin/Reviews | PENDING QA review → Approve | APPROVED; nội dung unchanged; public detail hiển thị | Status + detail screenshot |
| M03 | Admin · /Admin/Reviews | Hai review riêng PENDING/APPROVED → Hide | HIDDEN, không public, content retained | Status before/after |
| M04 | Admin · /Admin/Reviews | HIDDEN QA review → Restore | APPROVED/public; content unchanged | Status + detail |
| M05 | Admin · /Admin/Reviews | Lặp action cùng state; hai Admin session nếu có | Same-state no-op; race báo conflict/reload, không ghi đè sai | Hai response/time |
| M06 | Admin + public · /Admin/Reviews, /Catalog/Details/{id} | Chỉ fixture unknown-status đã tồn tại; không sửa DB | Unknown state cảnh báo; không transition/public | Screenshot + status read-only |
| M07 | Customer/Sales/Warehouse/Admin/anonymous · /Admin/Reviews | GET và POST thiếu/sai antiforgery theo cách QA duyệt | Chỉ Admin; role khác deny; antiforgery invalid bị từ chối | HTTP status theo role |
| M08 | Admin · /Admin/Reviews | Viewport 1440×900, 768×1024, 390×844 | Filter/actions usable; không overlap/page overflow | Screenshot mỗi viewport |

### 6.4 Dashboard và Reporting – P01–P08

Chi tiết: [PHASE6_4_MANUAL_UAT.md](./PHASE6_4_MANUAL_UAT.md). Dùng dữ liệu QA hiện có sau preflight; không tạo/đổi số liệu để làm report đẹp.

| Case | Role / URL | Dữ liệu & thao tác | Expected | Evidence |
|---|---|---|---|---|
| P01 | Admin · /Admin | Đối chiếu dashboard với catalog/consultation/QA orders | Số liệu từ dữ liệu hiện tại, không số giả | Screenshot + thời điểm |
| P02 | Admin · /Admin/Reports/Orders | Chọn khoảng có QA orders đã biết | Group theo OrderDate/status, không gọi count là revenue | URL range + result |
| P03 | Admin · /Admin/Reports/Revenue | Khoảng có COMPLETED và non-completed nếu có | Chỉ COMPLETED/CompletedAt; cộng TotalAmount đúng một lần | URL + phép đối chiếu |
| P04 | Admin · /Admin/Reports/Products | Khoảng có COMPLETED order, xem paging | Sum line quantity; loại non-completed; snapshot name | Page/result capture |
| P05 | Admin · /Admin/Reports/Inventory | Threshold 0 và số dương; tồn có sẵn | Available, LOW/OUT/NORMAL/MISSING/ANOMALY đúng; threshold không lưu | Query + result |
| P06 | Admin · Orders/Revenue/Products reports | Biên ngày/tháng/năm và range trống/sai | Ngày VN trọn vẹn; next midnight loại trừ; empty/range error rõ | URL + report output |
| P07 | Admin/Customer/Sales/Warehouse/anonymous · report URLs | Đọc report, thử mutation chỉ nếu có UI/API | Admin-only; read-only; không action ngoài phạm vi | Status + visible actions |
| P08 | Admin · /Admin và các report | Desktop/tablet/mobile; keyboard Tab/Enter | Không overflow/overlap; table responsive; focus/label dùng được | 3 viewport screenshots + note |

### 6.5 Regression Phase 1–5 – Rg01–Rg06

Các regression dưới đây chưa được ghi kết quả. Dùng URL/action hiện có, role QA single-role; data chỉ từ QA và chỉ tạo bằng fixture riêng được duyệt.

| Case | Role / URL | Dữ liệu & thao tác | Expected | Evidence |
|---|---|---|---|---|
| Rg01 Identity & public access | Anonymous/Customer · /, /Account/Register, /Account/Login, /Account/Logout, /workspace/Customer | Đăng ký QA mới nếu được duyệt; login/logout; thử profile ID khác | Public routes mở; auth đúng; profile chỉ principal; ID tampering bị từ chối | URL/status/screenshot, không password |
| Rg02 Catalog/Search/Compare | Anonymous/Customer · /Catalog, /Catalog/Details/{id}, /Catalog/Compare?ids=... | Product/spec/image có sẵn; search/filter/compare tối đa 3 | Query/dynamic specs đúng; giới hạn compare; không đổi data | Query + screenshots |
| Rg03 NLP consultation | Anonymous/Customer · /Consultation | Câu đủ/thiếu/mơ hồ; không fixture mới | Clarification/recommendation dựa QA product/spec, không bịa data | Prompt đã redact + result |
| Rg04 Cart/Checkout/Orders | Customer · /Cart, /Checkout, /Orders, /Orders/Details/{id} | Product có hàng; QA cart/order riêng | AJAX cart, server checkout, PENDING/order snapshots/ownership đúng | Order fixture ID + evidence |
| Rg05 Cancellation/Sales/Warehouse | Customer/Sales/Warehouse · /Orders; /Sales/Orders; /Warehouse/Orders; /Warehouse/Inventory | QA order/inventory fixture đã duyệt | Cancel request, Sales decision, Warehouse export/IN/ADJUST đúng state/reservation/authorization | IDs + state before/after |
| Rg06 Admin CRUD/Promotions | Admin · /Admin/Products, /Admin/Categories, /Admin/Brands, /Admin/Specifications, /Admin/Promotions | Ưu tiên đọc; write chỉ với QA fixture riêng được duyệt | CRUD validation, promotions, inventory separation, role boundary giữ nguyên | URL + fixture IDs |

## 7. Email provider – REAL EMAIL UAT BLOCKED

Code hiện chưa có provider implementation. Chỉ thêm secret vào cấu hình **không** làm sender hoạt động: non-Development đăng ký UnconfiguredEmailSender. Development dùng DevelopmentEmailSender/in-memory mailbox; đó là giả lập, không chứng minh email được nhận.

### Phương án để chủ dự án lựa chọn

| Provider | Chi phí công bố tại ngày rà soát | Điểm phù hợp / điều kiện |
|---|---|---|
| **Amazon SES Essentials** | Pay-as-you-go; bảng giá hiện công bố $0.16/1.000 email ở bậc 0–10M/tháng. SES sandbox theo region: chỉ gửi recipient đã verify/mailbox simulator, tối đa 200/ngày và 1/giây. | Chi phí gửi thấp; cần AWS account/region, verify sender identity; production access cần AWS duyệt. [SES pricing](https://aws.amazon.com/ses/pricing/), [verified identities](https://docs.aws.amazon.com/ses/latest/dg/verify-addresses-and-domains.html), [request production access](https://docs.aws.amazon.com/ses/latest/dg/request-production-access.html). |
| **Resend** | Free $0, 3.000 email/tháng, 100/ngày, 3 domain; Pro $20/tháng, 50.000/tháng, không giới hạn ngày, overage $0.90/1.000. Giá USD có thể đổi. | API/transactional workflow thuận tiện nhóm nhỏ; cần verify domain sở hữu, dùng API key riêng cho QA. [Resend pricing](https://resend.com/pricing), [verified domains](https://resend.com/docs/dashboard/domains/introduction), [API key management](https://resend.com/docs/dashboard/api-keys/introduction). |
| **Azure Communication Services Email** | Usage-based; cần Azure calculator/region để xác định tổng phí. | Hợp lý nếu owner đã có Azure; cần resource/domain verified, SPF/DKIM và credential/Entra config. [Microsoft domain and sender authentication](https://learn.microsoft.com/en-us/azure/communication-services/concepts/email/email-domain-and-sender-authentication), [pricing](https://azure.microsoft.com/en-us/pricing/details/communication-services/). |

**Đề xuất:** lượng invitation/reset nhỏ thì Resend Free có thể đủ cho QA nếu owner có domain QA và chấp nhận giới hạn ngày; nếu ưu tiên chi phí gửi và đã có AWS thì SES Essentials, nhưng phải xử lý sandbox/identity. Đây chỉ là đề xuất; chưa đăng ký, cấu hình hay gửi.

Khi owner chọn provider và phê duyệt mailbox QA: (1) implement adapter IEmailSender theo contract hiện tại; (2) dùng sender/domain riêng cho QA; (3) lưu secret bằng User Secrets local hoặc environment/secret store được quản lý, không đặt key trong appsettings/source/log/report; (4) chỉ gửi test invitation tới mailbox QA đã xác nhận; (5) xác nhận nhận được và kiểm tra token fragment, antiforgery, expiry/replay/resend/delivery failure. Chưa có provider/recipient approval nên **REAL EMAIL UAT BLOCKED / PENDING OWNER APPROVAL**.

## 8. Git, secrets và sao lưu source

### 8.1 Git status tại thời điểm review

- Branch master; HEAD ab5d945018b89d69beb72d9897594cf270e15949 (feat: complete Phase 5 sales and warehouse workflows).
- Đầu lượt remediation: **19 modified + 57 untracked = 76 status entries**, **0 staged**. Sau remediation: **23 modified + 59 untracked = 82 entries**, **0 staged**; 76 mục ban đầu vẫn được giữ. Hai file mới là policy và focused tests; bốn file tracked được bổ sung thay đổi connection safety.
- Thay đổi gồm source, tests và tài liệu Phase 6.1–6.5; untracked docs/PHASE4_SCHEMA_REVIEW.sql vẫn được giữ, không stage/xóa.
- git diff --check không có whitespace error; Git chỉ cảnh báo chuẩn hóa LF→CRLF. Không commit/push.
- Không có local appsettings, env, binary, log, TRX hoặc QA database dump trong status. Local Development appsettings đang ignored và cần giữ trên máy, không đưa vào backup upload/CI.

### 8.2 Backup đề xuất – chưa thực hiện

Tạo bản sao cục bộ ngoài repo trên ổ mã hóa/protected, gồm .git và uncommitted files để giữ status/history. Backup có thể chứa ignored appsettings.Development.json, key hoặc upload local; giới hạn ACL, không đưa lên cloud/share/CI và không xóa nếu chưa kiểm tra. Lệnh dưới đây chỉ là mẫu; **không chạy trong lượt này**. Chọn destination mới và xác nhận chưa tồn tại:

    $source = 'D:\Projects\NEXORA'
    $destination = 'D:\Backups\NEXORA-working-tree-20261022'
    if (Test-Path -LiteralPath $destination) { throw 'Backup destination exists; choose a new path.' }
    robocopy $source $destination /E /COPY:DAT /DCOPY:DAT /R:1 /W:1
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

/E không mirror/delete đích. Kiểm tra ACL và completeness trước khi dùng. Không upload backup ra ngoài.

## 9. Blockers và việc cần làm theo thứ tự ưu tiên

1. **P1 – Full regression runner:** Lần chạy 31 connection-policy/provisioning tests trước đây không có bằng chứng tuân thủ Code Integrity; không coi là nghiệm thu. Chạy fresh Unit/HTTP trên source snapshot trong runner được phê duyệt; SQL tests chỉ chạy sau target guard và QA preflight, lưu TRX mới. Nếu Code Integrity chặn, xử lý signing/allowlisting qua IT, không bypass policy.
2. **P1 – Admin account:** Chủ dự án review/phê duyệt Change NEXORA-P61-QA-ROLE82 riêng. Chỉ ở lượt được duyệt mới preflight QA lại, transactionally xóa đúng Customer mapping nếu precondition khớp, hậu kiểm Admin-only/active, rồi triển khai guard. Nếu dữ liệu khác expected, dừng. Chưa thực hiện.
3. **P1 – QA DB preflight:** Trước test/site start xác nhận PC01\SQLEXPRESS / NEXORA_IntegrationTests, ONLINE, migration/schema/role counts bằng truy vấn chỉ đọc; không truy cập database chính. Đảm bảo role seeder không phải thêm role.
4. **P1 – Sửa hướng dẫn runtime 6.1:** Bỏ hướng dẫn :5060 và Phase61 binary; ghi source output hiện tại, prerequisites, single-role policy và trạng thái UserId 82. UAT chưa chạy trước khi cập nhật.
5. **P1 – Owner UAT:** Sau runner, Admin single-role hoạt động và QA preflight, chạy A01–A20, F01–R06, M01–M08, P01–P08, Rg01–Rg06; ghi bằng chứng thật từng test. Hiện tất cả NOT RUN.
6. **P1 – Email provider:** Owner chọn vendor, xác nhận mailbox/domain và đồng ý gửi; sau đó implement/configure adapter an toàn và thực hiện real-email UAT.
7. **P2 – FR45/NFR10:** Owner chốt structured operational log tối thiểu cho thay đổi giá. Không thêm bảng/lịch sử khi chưa duyệt.
8. **P2 – Connection policy operationalization:** Đặt QA connection tường minh bằng secret/environment phù hợp trước khi chạy QA; local ignored Development config hiện sẽ bị từ chối. Production cần cấu hình tường minh và phê duyệt triển khai riêng.
9. **P2 – Report query verification:** SQL integration QA kiểm tra AT TIME ZONE, date boundaries, fan-out, missing/anomaly, query plan và latency; quyết định có sửa label ProductName snapshot hay chấp nhận giới hạn hiện tại.
10. **Owner acceptance:** Chỉ sau khi blockers đóng và UAT evidence được chủ dự án xem xét mới đánh dấu Phase 6/prototype sẵn sàng nghiệm thu. Production deployment và main database migration là quy trình riêng chưa được cho phép.

## 10. Trạng thái cuối

- **Phase 6.1:** implemented trong source; acceptance **BLOCKED** bởi test evidence mới và UserId 82 single-role remediation/approval; manual UAT NOT RUN.
- **Phase 6.2–6.4:** **DONE – BUILD VERIFIED**; automated tests, SQL runtime và browser/manual UAT source hiện tại **NOT TESTED**.
- **Database NEXORA chính và QA database:** **NOT ACCESSED / NOT CHANGED** trong lượt remediation; config tests chỉ parse chuỗi và tạo context đang Closed.
- **Real email:** **BLOCKED / PENDING OWNER APPROVAL**.
- **Manual UAT:** **NOT RUN**; không có case nào được đánh dấu PASS.
- **Source:** 76 mục ban đầu được bảo toàn; hiện có 82 mục chưa commit (23 modified, 59 untracked), 0 staged. Không commit/push.

**Kết luận: NEXORA hiện BUILD VERIFIED, chưa READY FOR OWNER ACCEPTANCE.** Ưu tiên trước là có runner hợp lệ và giải quyết Admin QA blocker theo Change Request được duyệt riêng; sau đó chạy QA preflight, test đầy đủ và manual UAT. Chưa triển khai Production và chưa đánh dấu dự án COMPLETED.
