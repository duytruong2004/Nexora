# NEXORA – Phase 6 QA UAT and Remediation Execution Plan

**Ngày lập:** 22/10/2026
**Trạng thái:** `BLOCKED – NOT READY FOR OWNER ACCEPTANCE`
**Phạm vi:** Chuẩn bị kiểm thử Identity, hồ sơ Change Request `NEXORA-P61-QA-ROLE82`, kế hoạch fixture và checklist Manual UAT. Đây không phải phê duyệt thực thi Change Request.

## 1. Tóm tắt trạng thái

| Hạng mục | Trạng thái | Bằng chứng / giới hạn |
|---|---|---|
| QA database | `VERIFIED – snapshot trước đó` | Chủ dự án đã xác nhận kết quả chỉ đọc: `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`, `ONLINE`, 27 bảng vật lý, 3 migrations, 4 role chuẩn. Không truy vấn lại trong lần lập kế hoạch này. |
| Identity guard trên source | `SOURCE PRESENT; RUNTIME NOT TESTED` | Source fail-closed từ chối đăng nhập/cookie có zero, nhiều, role không chuẩn hoặc không khớp. Không triển khai guard. |
| Security regression tests | `BLOCKED / NOT RUN` | Chưa có CI runner/test host được IT xác nhận tuân thủ Windows Code Integrity. Không có TRX mới cho source hiện tại. |
| Change `NEXORA-P61-QA-ROLE82` | `NEEDS SEPARATE OWNER APPROVAL` | Phạm vi dự kiến là xóa đúng mapping Customer của UserId 82 trên QA; không thực thi SQL. Bằng chứng test bắt buộc hiện chưa đạt. |
| Source/build artifact | `SOURCE CURRENT; BINARY STALE` | HEAD `ab5d945018b89d69beb72d9897594cf270e15949`; Phase 6 nằm trong working tree chưa commit. Các DLL đang chạy ở cổng 5059/5060 được build trước những sửa đổi source mới nhất; không dùng làm UAT evidence. |
| QA fixture | `INSUFFICIENT FOR END-TO-END UAT` | Snapshot gần nhất không có đơn PENDING/CONFIRMED/COMPLETED, review PENDING hoặc promotion active; 12 sản phẩm active đều còn hàng. Không tạo hay sửa fixture. |
| Admin truy cập source Phase 6 | `BLOCKED` | UserId 82 là Admin active duy nhất và đang có cả Admin + Customer. Guard đơn role mới sẽ từ chối account này. Không sửa mapping và không tạo Admin khác. |
| Email thực | `BLOCKED / OWNER APPROVAL REQUIRED` | Chưa cấu hình và chưa xác nhận email provider/mailbox QA. Development mailbox chỉ mô phỏng gửi email; không chứng minh delivery thật. |
| Browser UAT | `NOT RUN` | Chưa mở website source hiện tại bằng browser. Không ghi PASS cho bất kỳ case UAT nào. |

Lượt này không kết nối SQL, không chạy website, không chạy test, không migration, không thay đổi dữ liệu, không dừng process và không thực hiện Git add/commit/push.

## 2. Identity security test prerequisite

### 2.1 Windows Code Integrity và phương án chạy hợp lệ

Bằng chứng trong các báo cáo trước ghi nhận Windows Code Integrity Event ID 3033/3077 chặn `testhost.exe` khi nạp các assembly test không ký từ output trong repository. Một lần chạy lịch sử 31 test từ `%TEMP%` không có bằng chứng cho thấy vị trí đó được allowlist; không được coi lần chạy đó là tuân thủ policy. Kết quả 240/240 của Phase 6.1 cũng thuộc source trước guard single-role. Cả hai đều không chứng minh source hiện tại.

Phương án hợp lệ:

1. **Ưu tiên CI runner được IT phê duyệt** có Windows Code Integrity policy tương ứng. Runner phải checkout/build đúng snapshot source hiện tại, chạy test từ build của chính runner và lưu TRX theo quy trình nội bộ.
2. Hoặc nhờ **quản trị viên ký/allowlist** các binary cần thiết theo quy trình được tổ chức phê duyệt. Các assembly ứng dụng tự build cũng có thể bị policy chặn; không giả định chỉ cần cho phép `NEXORA.Tests.dll`.
3. Ghi nhận tên runner/host, policy approval reference, SDK/runtime, commit HEAD, hash manifest source và TRX. Không copy DLL sang thư mục khác để tìm cách chạy, không tắt/bỏ qua policy, không sửa allowlist trong lượt này.

Snapshot kiểm thử phải chứa cả HEAD và toàn bộ thay đổi chưa commit: tracked modifications, các file Phase 6 untracked và nội dung cần thiết khác của working tree. CI không được chỉ build Git HEAD. Trước khi chạy cần đối chiếu manifest SHA-256 với working tree hiện tại. Secrets, `appsettings.Development.json`, QA database dumps và dữ liệu cá nhân không được đưa vào artifact.

### 2.2 Test matrix bắt buộc – source hiện tại, chưa chạy

Các tên dưới đây là test/source thật đã rà soát. Kết quả hiện tại của từng test là `BLOCKED / NOT RUN` trên môi trường tuân thủ policy, không phải PASS.

| Yêu cầu | Test cụ thể | File | Ghi chú / điều kiện PASS |
|---|---|---|---|
| Login từng role hợp lệ | `SingleRoleLogin_StillIssuesCookieAndCanAccessItsRoleEndpoint` – bốn theory rows Admin, Customer, Sales, Warehouse | `tests/NEXORA.Tests/AccountLoginPolicyHttpTests.cs` | Mỗi role nhận cookie và truy cập endpoint chỉ dành cho role tương ứng. |
| Zero-role login bị từ chối | `ZeroRoleLogin_DoesNotIssueAuthenticationCookie` | cùng file | Không phát application cookie; protected endpoint quay về login. |
| Multi-role login bị từ chối | `MultiRoleLogin_DoesNotIssueAuthenticationCookie` | cùng file | Admin + Customer không nhận cookie; phản hồi bên ngoài vẫn chung chung. |
| Inactive login bị từ chối | `InactiveUserLogin_DoesNotIssueAuthenticationCookie` | cùng file | Không nhận cookie. |
| Multi-role cookie bị từ chối | `MatchingMultiRoleCookieAndPersistedRoles_AreRejected` | `tests/NEXORA.Tests/AccountAuthenticationCookieHttpTests.cs` | Cookie và persisted state cùng có nhiều role vẫn phải fail closed. |
| Cookie cũ sau đổi role | `AdminCookie_IsRejectedAfterPersistedRoleChangesToCustomer`; `SalesCookie_IsRejectedAfterPersistedRoleChangesToWarehouse` | cùng file | Request protected tiếp theo bị chuyển về login/không còn quyền cũ. |
| Inactive cookie bị từ chối | `Cookie_IsRejectedImmediatelyAfterAccountBecomesInactive` | cùng file | Từ chối ngay request protected tiếp theo. |
| Không có redirect loop | `RejectedCookieRedirectsToAnonymousLoginWithoutRedirectLoop` | cùng file | URL login trả response bình thường sau khi cookie bị thu hồi. |
| Policy đơn role và anomaly | `ActiveAccountWithOneKnownRole_CanIssueCookie`; `AccountWithoutRole_CannotIssueCookie`; `MultiRoleAccount_CannotIssueCookie`; `AccountWithUnknownRole_CannotIssueCookie`; `InactiveAccount_CannotIssueCookie`; `CookieAndDatabaseRoleMismatch_IsRejected`; `DuplicateRoleClaims_AreRejectedRatherThanDeduplicated` | `tests/NEXORA.Tests/AccountAuthenticationPolicyTests.cs` | Unit policy phải xác nhận đúng canonical role, cardinality và equality. |
| Invitation không regression ở HTTP | `StaffActivationKeepsInvitationSecretsOutOfRequestUrlAndValidatesWithAntiforgery` | `tests/NEXORA.Tests/AccountAdministrationHttpIntegrationTests.cs` | Test host fake; kiểm tra token fragment/antiforgery ở HTTP contract, không thay thế browser/email thật. |
| Role store test harness chạy thực tế | Các test login HTTP ở trên, nhất là bốn case `SingleRoleLogin_...` và `MultiRoleLogin_...` | `tests/NEXORA.Tests/AccountLoginPolicyHttpTests.cs` | `TestRoleStore` trong source implement `IRoleStore<AppRole>, IRoleClaimStore<AppRole>` và được đăng ký qua `.AddRoleStore<TestRoleStore>()`. Việc code interface tồn tại/biên dịch chưa chứng minh runtime DI thành công; bắt buộc chạy test host để xác minh. |
| Invitation expiry/replay/resend/concurrency | `ExpiredStaffInvitationToken_IsRejected`; `StaffInvitation_CreateActivateAndReplay_AreAtomicPurposeBoundAndOneUse`; `StaffInvitation_ResendInvalidatesOldToken_AndDoesNotCreateDuplicate`; `StaffInvitation_ConcurrentRedemption_AllowsOnlyOneActivation`; `StaffInvitation_ExpiredTokenAndSuspendedAccount_CannotActivateOrBeUnlockedAsPending` | `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | SQL integration có tạo/xóa fixture. Chỉ chạy trên database QA đã preflight và sau phê duyệt riêng cho nhóm test ghi dữ liệu. |
| Session revoke sau lock/role change | `AccountLock_RejectsExistingCookieOnTheVeryNextProtectedRequest`; `RoleChange_RejectsOldRoleCookieOnTheVeryNextProtectedRequest` | `tests/NEXORA.Tests/AccountSessionRevocationSqlServerIntegrationTests.cs` | SQL integration có tạo/xóa fixture; không chạy trên database chính. |

**Tiêu chí mở Change Request:** mọi test Unit/HTTP bắt buộc phía trên phải PASS trên runner được IT xác nhận; runtime `IRoleClaimStore` phải được chứng minh bởi test HTTP; phải có TRX mới gắn với source manifest hiện tại. Invitation/session SQL regression bắt buộc phải được chạy trên QA theo phê duyệt riêng, hoặc owner phải chấp thuận một ngoại lệ có căn cứ bằng văn bản trước khi đề nghị sửa mapping. Bất kỳ FAIL, BLOCKED hoặc thiếu TRX nào đều giữ Change Request ở trạng thái chưa đủ điều kiện thực thi.

### 2.3 Kế hoạch filter cho runner được duyệt

Không chạy `dotnet test NEXORA.sln` không chọn lọc trên máy đang bị policy chặn. Sau khi IT cấp runner, chạy các filter cụ thể bằng source snapshot đã xác minh; lệnh dưới đây là kế hoạch, chưa thực thi:

```powershell
dotnet test .\tests\NEXORA.Tests\NEXORA.Tests.csproj --no-build --no-restore `
  --filter "FullyQualifiedName~AccountAuthenticationPolicyTests|FullyQualifiedName~AccountLoginPolicyHttpTests|FullyQualifiedName~AccountAuthenticationCookieHttpTests|FullyQualifiedName~StaffAccountStatePolicyTests|FullyQualifiedName~DatabaseConnectionPolicyTests|FullyQualifiedName~Phase6AuthorizationTests|FullyQualifiedName~AccountAdministrationHttpIntegrationTests" `
  --logger "trx;LogFileName=Phase6IdentitySecurity-current-source.trx"
```

Sau đó chạy SQL integration theo một lượt riêng, chỉ khi phê duyệt cho phép test fixture ghi/xóa QA và preflight vẫn khớp. `SqlServerFactAttribute` yêu cầu `NEXORA_SQLSERVER_TEST_CONNECTION` được xác nhận là Windows Authentication tới đúng QA; checkout tests còn yêu cầu `NEXORA_CHECKOUT_SQL_TESTS=1`. Guard trong test không thay thế preflight hoặc quyền ghi dữ liệu. Không chạy SQL tests trên database chính.

## 3. Hồ sơ Change Request `NEXORA-P61-QA-ROLE82` – dự thảo, chưa thực thi

### 3.1 Phạm vi chính xác

- **Target duy nhất:** `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`.
- **User:** `dbo.users.id = 82`.
- **Thay đổi duy nhất dự kiến:** xóa một mapping Customer tại `dbo.user_roles`; giữ mapping Admin.
- Không sửa hàng `users`, role row, password, trạng thái active, security stamp, đơn hàng, giỏ hàng, favorites, reviews, lịch sử kho hoặc dữ liệu người dùng khác. Không DDL/migration.
- **Trạng thái:** chưa phê duyệt thực thi; không có SQL DELETE nào chạy.

### 3.2 Preconditions phải kiểm lại ngay trước khi thực thi

Trong cùng kết nối và transaction, nếu một điều kiện không đúng thì `ROLLBACK` và DỪNG:

1. SQL Server chính xác là `PC01\\SQLEXPRESS`; `DB_NAME()` là `NEXORA_IntegrationTests`; database `ONLINE`.
2. Bảng/columns vẫn đúng model đã rà soát: `dbo.users(id, is_active)`, `dbo.roles(id, name, normalized_name)`, `dbo.user_roles(user_id, role_id)`; không suy ra database từ tên connection do người gọi cung cấp.
3. Tập role chuẩn có đúng một role cho mỗi canonical name `Admin`, `Customer`, `Sales`, `Warehouse`; resolve `role_id` bằng canonical `name`/`normalized_name`, không phụ thuộc hằng số ID.
4. UserId 82 tồn tại và active; mapping của account đúng chính xác hai role canonical `Admin` và `Customer`, mỗi mapping một lần, không role thứ ba. Không lấy preflight cũ làm thay thế kiểm tra transaction.
5. Admin mapping của UserId 82 là mapping duy nhất giữ lại; không tồn tại active Admin khác theo snapshot đã phê duyệt. Nếu precondition thực tế khác snapshot, không sửa rộng phạm vi để “làm cho chạy”.
6. Kết quả Unit/HTTP security matrix, Identity role-store runtime và SQL regression bắt buộc đã PASS trên runner/test setup được chấp thuận; source hash của test khớp code sắp triển khai.
7. Chủ dự án phê duyệt riêng đúng Change Request này sau khi xem precondition, test TRX, SQL preview cuối cùng và recovery plan. Phê duyệt chuẩn bị UAT hiện tại **không phải** phê duyệt thực thi.

### 3.3 Transaction, serialization và affected rows

Thiết kế thao tác phải dùng một transaction `SERIALIZABLE`, `XACT_ABORT ON`, cùng SQL application lock mà `IdentityAccountAdministrationService` đang sử dụng: `NEXORA:ACCOUNT:POLICY`, `Exclusive`, `LockOwner='Transaction'`, timeout hữu hạn. Sau lock, đọc/khóa user row và range `user_roles` bằng lock hints phù hợp (`UPDLOCK`, `HOLDLOCK`), xác minh toàn bộ preconditions, rồi xóa chỉ mapping Customer đã resolve theo canonical role.

- Expected affected rows: **1**, không phải “ít nhất một”. Khác biệt nào cũng rollback.
- Trước commit xác minh UserId 82 vẫn active, đúng một mapping Admin, không có Customer/role khác; active Admin tồn tại; không có thao tác khác nào được sửa.
- Bất kỳ mismatch, lock timeout, exception hoặc postcondition sai nào xảy ra trước commit: rollback toàn transaction.
- Kiểm tra lần cuối với source hiện hành và preflight mới. Không chạy script nháp trong `PHASE6_1_MULTIROLE_REMEDIATION.md`.

### 3.4 Commit không xác định, postcondition và recovery

Nếu mất kết nối quanh `COMMIT`, **không chạy lại DELETE tự động**. Mở kết nối mới chỉ đọc và phân loại:

- Mapping Customer đã hết, Admin còn đúng một, account active: thao tác có thể đã commit; lưu bằng chứng read-only và dừng để chủ dự án quyết định bước tiếp.
- Trạng thái đúng pre-state: transaction có thể đã rollback; không retry trong cùng lượt. Xin xác nhận lại sau khi xem logs/transaction state.
- Trạng thái khác/không thể xác minh: dừng, bảo toàn dữ liệu và xin DBA/owner review. Không tự khôi phục mapping, restore backup hoặc sửa user/role khác.

Sau commit có kế hoạch hợp lệ, postcondition read-only phải xác nhận account vẫn active/Admin-only, UserId 82 vẫn có thể là Admin cuối cùng duy nhất, mapping Customer không còn, không có thêm mapping thay đổi và history nghiệp vụ nguyên vẹn theo các count được phép. Không đưa email, hash, token hoặc security stamp vào evidence.

### 3.5 Kiểm tra phiên và triển khai guard

Sau khi Change Request riêng được thực thi và hậu kiểm thành công:

1. Giữ nguyên phiên cũ để thử một request protected; cookie Admin+Customer cũ phải bị từ chối, sign-out và không redirect loop.
2. Owner đăng nhập lại bằng chính tài khoản Admin hiện hữu; xác minh Admin-only login và `/Admin` protected page. Không gửi mật khẩu cho Codex.
3. Nếu login/access fail, DỪNG; không tạo Admin khác, không gán lại role, không hạ chính sách single-role.
4. Chỉ khi test bằng chứng, account state và phương án phục hồi đã được owner review mới triển khai binary guard mới lên site QA theo một bước được phê duyệt riêng. Xác minh binary/source version trước khi dùng browser.

## 4. Bộ fixture QA tối thiểu cho Manual UAT

Số liệu dưới đây là snapshot read-only đã được chủ dự án cung cấp trong lượt trước; **không truy vấn lại** trong lượt này. Chúng có thể thay đổi, nên phải preflight lại trước mọi fixture/startup:

| Dữ liệu | Snapshot gần nhất | Hệ quả |
|---|---|---|
| Roles/accounts | 4 canonical roles. Active membership: Admin 1, Customer 4, Sales 1, Warehouse 1. UserId 82 đang chiếm Admin và đồng thời Customer; không có Admin-only. | Customer-only có thể chọn từ account một role nếu owner biết credential; không dùng 82 làm Customer-only. Sales/Warehouse credentials phải được owner xác nhận cục bộ. Admin UAT bị chặn bởi Change Request. |
| Products/inventory | 12 active products, 12 inventory rows; mọi sản phẩm còn hàng; physical 111, reserved 0, available 111; không thiếu/âm inventory. ProductId 149 hiện còn 5, dù SKU có chuỗi `OOS`. | Dùng product đang còn hàng cho luồng Cart/Checkout. Không sửa Product 149. Cần tạo sản phẩm QA riêng có inventory 0 để test hết hàng. |
| Specifications | 11/12 active products có specification; 52 product specification values. | Có thể dùng product đã có spec để kiểm Catalog/Review/Comparison; cần chọn sau khi preflight lại. |
| Orders | 0 PENDING, 0 CONFIRMED, 0 COMPLETED; 3 CANCELLED, 1 EXPORTED. | Cần tạo đơn mới qua UI/workflow; không đổi trạng thái đơn cũ bằng SQL. |
| Reviews | 0 PENDING. | Cần tạo từ order COMPLETED của đúng Customer và OrderDetail hợp lệ. |
| Promotions | Một PERCENTAGE và một FIXED_AMOUNT, nhưng inactive; không có promotion áp dụng. | Cần tạo promotion QA mới qua Admin UI sau khi Admin access được khôi phục. |

### 4.1 Fixture plan theo workflow thật

Mọi fixture dưới đây cần Change/UAT authorization riêng trước khi ghi database. Ưu tiên UI/service; không SQL state fabrication.

1. **Customer-only:** chọn một Customer có đúng role Customer, credential owner đã biết; nếu không có, dùng luồng registration hiện tại để tạo account QA mới bằng địa chỉ test được phê duyệt. Không reset password tài khoản có sẵn trong bước chuẩn bị này.
2. **Sales/Warehouse:** dùng account hiện hữu single-role nếu owner có credential; nếu không, sau khi Admin-only được xác minh, tạo qua staff invitation trên Dev mailbox. Không tự provision.
3. **Product hết hàng:** tạo product có SKU/tên QA dễ nhận diện, inventory mặc định 0 từ Product flow hiện có; không điều chỉnh 11 SKU demo và không gán tồn giả. Ghi ProductId/SKU trong phiếu UAT.
4. **Order PENDING:** Customer checkout product còn hàng. Kỳ vọng reservation tăng theo đơn; không tạo order bằng `UPDATE`.
5. **Order CONFIRMED:** Sales xác nhận chính đơn PENDING; kiểm tra trạng thái, reservation và lịch sử nghiệp vụ không nhân đôi.
6. **Order COMPLETED:** tiếp tục workflow thật: Sales handoff → Warehouse export (OUT/reservation release trong transaction) → Sales mark shipping → Sales complete. Không đánh dấu COMPLETED thủ công.
7. **Review PENDING:** Customer sở hữu order COMPLETED gửi review trên OrderDetail đúng sản phẩm. Review mới phải PENDING cho tới Admin moderation.
8. **Promotions:** Admin tạo hai promotion QA, một PERCENTAGE và một FIXED_AMOUNT, gắn với sản phẩm QA phù hợp, có khoảng hiệu lực bao trùm UAT. Kiểm tra kết quả giá từ server; không sửa promotion hiện có.
9. **Báo cáo theo giờ Việt Nam:** dùng khoảng ngày `Asia/Ho_Chi_Minh` có chứa thời điểm hoàn thành đơn QA; so khớp đơn COMPLETED, doanh thu snapshot và số lượng OrderDetail. Không dùng ngày ngoài khoảng để kỳ vọng có dữ liệu.
10. **Cleanup:** ghi fixture IDs trong checklist. Không dọn dữ liệu cho tới khi owner duyệt riêng cleanup; không xóa đơn/lịch sử cần giữ cho bằng chứng.

Không cần real email để test luồng activation với Development mailbox giả lập. Nếu tiêu chí khóa luận yêu cầu chứng minh email ngoài hệ thống, email UAT vẫn bị chặn cho đến khi owner chọn provider/mailbox, cấu hình secrets cục bộ và phê duyệt gửi tới người nhận QA.

## 5. Startup và binary safety

### 5.1 Những gì đã xác minh source

- `Program.cs` gọi `DatabaseConnectionPolicy.Resolve(...)` trước đăng ký Infrastructure/container và trước RoleSeeder; policy fail-fast nếu connection thiếu/sai hoặc Development/QA không trỏ chính xác tới `PC01\\SQLEXPRESS / NEXORA_IntegrationTests` với Windows Authentication.
- `AppDbContextFactory` không fallback tới database chính; design-time EF tooling cần connection được cấu hình tường minh và chịu cùng policy. Không chạy EF tooling/migration.
- Không thấy `Database.Migrate()`, `EnsureCreated()` hoặc `EnsureDeleted()` trong startup.
- Startup bình thường gọi `RoleSeeder.EnsureRolesAsync()`. Seeder query `RoleExistsAsync` cho bốn role và tạo role thiếu qua `CreateAsync`. Vì vậy nó **có thể ghi** nếu thiếu role; chỉ khởi chạy sau read-only preflight mới xác nhận đủ bốn role. Không gọi command provisioning hoặc seeder trực tiếp.
- Demo seeders chỉ ở nhánh Development và chỉ chạy nếu `TargetsDevelopmentDatabase(...)` nhận diện database chính `NEXORA`; QA target bị bỏ qua theo code. Tuy vậy, QA startup vẫn có RoleSeeder và account/session queries.
- Không chạy website, không truy cập SQL, không gọi RoleSeeder trong lượt này.

### 5.2 Processes/ports hiện có (read-only process inspection)

| Port | Process | Loaded web binary | Product version / thời điểm | Cách sử dụng |
|---|---|---|---|---|
| `127.0.0.1:5059` | `dotnet.exe`, PID 4004 | `D:\NEXORA-CatalogCart-Build\bin\NEXORA.Web\debug\NEXORA.Web.dll` | `b53478c…` (Phase 4); DLL 21/10/2026 01:11, process từ 01:16 | Không dừng/không dùng làm bằng chứng Phase 6. |
| `127.0.0.1:5060` | `dotnet.exe`, PID 2520 | `D:\Projects\NEXORA\tests\NEXORA.Tests\bin\Phase61\Debug\net10.0\NEXORA.Web.dll` | `ab5d945…` (HEAD); DLL 22/10/2026 02:29, process từ 02:36 | Binary cũ trước sửa source mới nhất buổi sáng; không dùng làm bằng chứng Phase 6. |

Source hiện tại có thay đổi tới 22/10/2026 07:43; các `NEXORA.Tests.dll` cục bộ đã thấy trong output Phase61/Phase61SecurityRemediation/Phase62Dev có timestamp sớm hơn. Không có binary được xác minh là được build từ working tree đầy đủ hiện tại. Không process nào bị dừng hoặc thay đổi.

### 5.3 Lệnh chạy dự kiến — CHƯA THỰC THI

Chỉ dùng sau khi có phê duyệt startup riêng, SQL read-only preflight mới, tất cả role chuẩn đang tồn tại và Admin-only remediation hoàn tất. Port ví dụ `5071` phải được kiểm tra là đang trống ngay trước lần chạy. Nguồn connection phải là QA environment variable đã được owner/IT xác minh; không đọc/echo giá trị:

```powershell
if ([string]::IsNullOrWhiteSpace($env:NEXORA_SQLSERVER_TEST_CONNECTION)) {
    throw 'Chưa cấu hình connection QA được phê duyệt.'
}
$env:ConnectionStrings__DefaultConnection = $env:NEXORA_SQLSERVER_TEST_CONNECTION
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:StaffInvitation__PublicBaseUrl = 'http://127.0.0.1:5071'
dotnet run --project .\src\NEXORA.Web\NEXORA.Web.csproj --no-launch-profile --urls http://127.0.0.1:5071
```

Lệnh chỉ là mẫu chuẩn bị, không phải lời mời chạy ngay. `dotnet run` có thể build source và startup sẽ truy vấn RoleSeeder; chưa được thực thi trong lượt này. Trước khi chạy, owner/IT phải xác nhận source snapshot, connection nguồn, port, role preflight và binary build. Không dùng DLL cũ từ cổng 5059/5060.

## 6. Thứ tự Manual UAT

Các URL dưới đây được đối chiếu với MVC route/controller hiện có. Mọi kết quả bắt đầu `NOT RUN`; `BLOCKED` chỉ áp dụng do prerequisite đã biết, không có case nào được đánh PASS.

### 6.1 Customer (sau khi site source mới được phép chạy)

| ID / trạng thái | URL – role | Bước thao tác, dữ liệu | Kết quả mong đợi | Bằng chứng |
|---|---|---|---|---|
| C01 `NOT RUN` | `/account/login`, `/account/logout`, `/account/profile` – Customer-only | Đăng nhập bằng credential QA owner đã biết; mở hồ sơ; đăng xuất; mở hồ sơ bằng anonymous. | Login/logout hoạt động; anonymous không xem hồ sơ; hồ sơ chỉ thuộc người đang đăng nhập. | Ảnh sau login và anonymous redirect/denial; che PII. |
| C02 `NOT RUN` | `/account/register`, `/account/forgotpassword`, `/account/resetpassword` – anonymous/Customer | Tạo Customer QA bằng email test được duyệt; yêu cầu reset qua Development mailbox giả lập nếu phù hợp; không dùng mailbox thật khi chưa duyệt. | Registration chỉ sinh Customer; reset link hết hạn/invalid xử lý an toàn; không lộ account/token. | Ảnh form/result đã che email/link/token. |
| C03 `NOT RUN` | `/Catalog`, `/Catalog/Details/{id}`, `/Catalog/Compare?ids=...` – Customer hoặc anonymous | Xem sản phẩm có spec; tìm/lọc theo category, brand, giá/spec; so sánh tối đa 3 sản phẩm; xem sản phẩm QA OOS sau khi fixture được duyệt. | Query/filter từ database; spec không bị giả; OOS không thêm vào giỏ; giới hạn so sánh được giữ. | Ảnh Catalog, filter, compare và OOS message. |
| C04 `NOT RUN` | `/tu-van-san-pham` – Customer hoặc anonymous | Gửi câu đầy đủ, thiếu/mơ hồ, ngân sách không có kết quả, viết tắt giá, RAM/SSD tối thiểu, tiêu chí mềm. | Clarification khi thiếu; không gợi ý vi phạm điều kiện cứng; không bịa sản phẩm/spec/pin; lý do truy vết về spec. | Ảnh câu hỏi/kết quả đã xóa thông tin cá nhân. |
| C05 `NOT RUN` | `/Favorites` – Customer | Thêm rồi gỡ product QA; thử thao tác khi anonymous/role khác và thử URL của user khác nếu có ID. | Danh sách owner-scoped; server từ chối role sai; không lộ favorites người khác. | Ảnh danh sách trước/sau và trang denied. |
| C06 `NOT RUN` | `/Cart` – Customer | Thêm product còn hàng từ Catalog/Home; tăng/giảm quantity, nhập trực tiếp, xóa; thử product OOS. | Cart cập nhật server-side; không reload ngoài ý muốn; tồn khả dụng kiểm tra; badge/tổng tiền đúng. | Ảnh trạng thái trước/sau và OOS error. |
| C07 `NOT RUN` | `/Checkout` – Customer | Checkout cart QA có hàng bằng địa chỉ/ghi chú QA; lưu OrderId. | Một order PENDING; snapshot giá đúng; reservation tăng theo lượng; idempotency không tạo duplicate khi request được lặp theo tình huống được thiết kế. | Ảnh xác nhận đã che địa chỉ và số lượng giữ. |
| C08 `NOT RUN` | `/Orders`, `/Orders/Details/{id}` – Customer | Xem đơn của mình; thử ID của fixture khác; hủy đơn PENDING. | Owner-only; PENDING hủy được và reservation release đúng một lần. | Ảnh status trước/sau, không chụp PII. |
| C09 `BLOCKED UNTIL FIXTURE` | `/Orders/Details/{id}` – Customer | Với order CONFIRMED, gửi yêu cầu hủy; không thử hủy qua luồng này sau khi xuất kho. | Cancellation request độc lập với order status; Sales/Admin có thể xét; đơn đã xuất không nhận yêu cầu hủy. | Ảnh trạng thái và request result đã che thông tin. |
| C10 `BLOCKED UNTIL FIXTURE` | `/Reviews`, `/Catalog/Details/{id}` – Customer owner | Sau khi chính Customer có đơn COMPLETED và OrderDetail hợp lệ, gửi review đúng product. | Review mới PENDING, owner thấy; chưa APPROVED thì không hiển thị public. | Ảnh review cá nhân và product detail trước moderation. |

### 6.2 Sales

| ID / trạng thái | URL – role | Bước thao tác, dữ liệu | Kết quả mong đợi | Bằng chứng |
|---|---|---|---|---|
| S01 `NOT RUN` | `/Sales/Orders` – Sales-only | Xem hàng đợi, lọc đơn QA theo trạng thái và mở chi tiết. | Sales truy cập; Customer/Warehouse/anonymous không truy cập; hiển thị đúng state. | Ảnh hàng đợi/denied page. |
| S02 `BLOCKED UNTIL FIXTURE` | `/Sales/Orders/Details/{id}` – Sales | Xác nhận PENDING rồi bàn giao Warehouse qua các nút POST hiện có. | Transition hợp lệ theo state machine; order không bị xác nhận/handoff lặp; reservation còn đúng trước xuất kho. | Ảnh state/notification và OrderId giả danh. |
| S03 `BLOCKED UNTIL FIXTURE` | `/Sales/Orders/Details/{id}` – Sales | Với yêu cầu hủy đang chờ, lần lượt approve/reject trên hai đơn QA riêng nếu được duyệt. | Chỉ trạng thái trước xuất kho; approve release reservation đúng một lần; reject giữ đơn hợp lệ. | Ảnh decision/state và inventory summary. |
| S04 `BLOCKED UNTIL FIXTURE` | `/Sales/Orders/Details/{id}` – Sales | Sau xuất kho, mark shipping rồi complete đơn QA. | Chỉ state cho phép; CompletedAt được ghi đúng; completion trùng không nhân đôi. | Ảnh trạng thái hoàn tất và thời gian. |

### 6.3 Warehouse

| ID / trạng thái | URL – role | Bước thao tác, dữ liệu | Kết quả mong đợi | Bằng chứng |
|---|---|---|---|---|
| W01 `NOT RUN` | `/Warehouse/Orders` – Warehouse-only | Mở queue và chi tiết đơn đã bàn giao. | Chỉ đơn phù hợp xuất hiện; Sales/Customer không truy cập route Warehouse. | Ảnh queue/denied page. |
| W02 `BLOCKED UNTIL FIXTURE` | `/Warehouse/Orders/Details/{id}` – Warehouse | Xuất một đơn đã bàn giao; tải lại hoặc gửi thao tác lặp bằng UI bình thường. | Xuất một lần; inventory OUT và RELEASE/quantity thay đổi nguyên tử theo nghiệp vụ; lần lặp không trừ lần hai. | Ảnh kết quả và lịch sử transaction. |
| W03 `NOT RUN` | `/Warehouse/Inventory`, `/Warehouse/Inventory/History` – Warehouse | Tìm product QA, lọc lịch sử theo sản phẩm/ngày. | Stock/reserved/available đúng; lịch sử chỉ hiện transaction phù hợp. | Ảnh kết quả với fixture IDs. |
| W04 `BLOCKED UNTIL FIXTURE` | `/Warehouse/Inventory/Receive`, `/Warehouse/Inventory/Adjust` – Warehouse | Nhập kho và điều chỉnh nhỏ trên product QA riêng với lý do QA; không sửa SKU demo. | IN/ADJUST có actor/reason; available không âm; concurrent/data validation conflict báo an toàn. | Ảnh form/result/history. |

### 6.4 Admin

| ID / trạng thái | URL – role | Bước thao tác, dữ liệu | Kết quả mong đợi | Bằng chứng |
|---|---|---|---|---|
| A01 `BLOCKED – USER 82` | `/Admin/Users` – Admin-only | Chỉ sau remediation/guard approval, đăng nhập bằng account Admin-only hiện hữu. | Không có chức năng cấp Admin; không lộ hash/token; account management có audit log vận hành. | Ảnh role list đã che PII và status. |
| A02 `BLOCKED – USER 82` | `/Admin/Users/CreateStaff`, `/dev/mailbox` – Admin | Tạo fixture staff `@nexora.test`, xem activation bằng Dev mailbox. Không gửi email thật. | Pending invite không có password, role Sales/Warehouse đúng một; token fragment được loại khỏi URL trước POST antiforgery; resend làm link cũ mất hiệu lực. | Ảnh form/mailbox đã che email/link/token. |
| A03 `BLOCKED – USER 82` | `/Admin/Reviews` – Admin | Duyệt/ẩn review QA PENDING; mở Catalog detail để xác minh public rendering. | Chỉ APPROVED công khai; transition hợp lệ; Customer không vào moderation. | Ảnh queue và detail sau transition. |
| A04 `BLOCKED – USER 82` | `/Admin/Reports/Orders`, `/Admin/Reports/Revenue`, `/Admin/Reports/Products`, `/Admin/Reports/Inventory` – Admin | Chọn khoảng ngày Việt Nam bao trùm order QA CompletedAt; đối chiếu số liệu UI với dữ liệu fixture được phép. | Mỗi báo cáo đúng định nghĩa FR47–FR50; chỉ COMPLETED góp revenue/product sales; timezone range đúng; không có nút ghi tồn trong report. | Ảnh bộ lọc/ngày và kết quả đã che thông tin. |
| A05 `BLOCKED – USER 82` | `/Admin/Promotions`, `/Admin/Products`, `/Warehouse/Inventory` – Admin | Tạo PERCENTAGE/FIXED_AMOUNT cho product QA; kiểm tra Catalog giá; xem tồn kho qua route đọc được cho Admin. | Promotion đúng thời gian/loại, không cộng dồn; Admin không có quyền Receive/Adjust kho. | Ảnh cấu hình QA, giá hiển thị và form kho bị từ chối. |
| A06 `BLOCKED – USER 82` | `/Admin/Categories`, `/Admin/Brands`, `/Admin/Specifications`, `/Admin/ProductSpecifications` – Admin | Tạo/sửa dữ liệu catalog QA; gán spec cho category/product QA; kiểm tra input sai category. | CRUD validation đúng; không sửa sản phẩm thật ngoài fixture; dynamic spec không gán chéo category. | Ảnh danh sách/form/result; ghi fixture IDs. |
| A07 `BLOCKED – USER 82` | `/Admin/Consultation/UsageNeeds`, `/Admin/Consultation/Rules` – Admin | Xem/tạo/chỉnh/tắt rule và UsageNeed QA; thử operator/threshold/weight sai; gửi consultation sample sau thay đổi được duyệt. | Admin-only; validation từ server; toggle và ranking dựa trên database; không bịa kết quả. | Ảnh cấu hình và kết quả tư vấn tương ứng, không có dữ liệu giả. |
| A08 `BLOCKED – USER 82` | Admin route + `/Sales/Orders`, `/Warehouse/Inventory/Receive`, `/Warehouse/Inventory/Adjust` | Thử route chéo với Admin-only account; mở route inventory read-only được phép. | Role boundary theo controller; Admin được đọc inventory report/list nếu policy cho phép nhưng không nhập/điều chỉnh/xuất kho thay Warehouse. | Ảnh access denied và trang read-only. |

### 6.5 Regression và responsive

| ID / trạng thái | Role / trang | Bước và kết quả mong đợi | Minh chứng |
|---|---|---|---|
| R01 `NOT RUN` | Anonymous + Customer + Sales + Warehouse + Admin | Thử route chéo từng vai trò; không dùng tài khoản nhiều role để kết luận isolation. Anonymous được yêu cầu login, role không phù hợp bị từ chối, dữ liệu customer owner-scoped. | Ghi URL, role, HTTP/UI result; không lưu cookie. |
| R02 `NOT RUN` | Từng role | Chrome DevTools: 1440×900, 768×1024, 390×844; rà menu, sidebar, bảng, form, thông báo và focus. | Ảnh chụp thật cho mỗi viewport và route có lỗi. |
| R03 `BLOCKED UNTIL FIXTURE` | Customer → Sales → Warehouse → Admin | Thực hiện trọn luồng cart → checkout → confirm/handoff → export → shipping/completed → review pending → moderation → reports. | OrderId/productId QA giả danh, ảnh từng trạng thái; không chụp token/PII. |

Với mọi case, ghi ngày/giờ, browser/version, URL, actual result, status `PASS / FAIL / NOT RUN / BLOCKED`, đường dẫn screenshot thật. Không đưa password, token, cookie, hash, security stamp, địa chỉ thật hoặc email cá nhân vào ảnh/tài liệu.

## 7. Điều kiện để mở website Phase 6 mới và bắt đầu UAT

### Trước khi khởi chạy website source mới

1. Source snapshot hiện tại được manifest/hash đầy đủ; build mới chạy trong môi trường được chấp thuận. Không dùng binary ở PID 4004/2520.
2. Chủ dự án/IT phê duyệt read-only QA preflight mới; kết quả đúng server/database, ONLINE, migrations/schema, bốn role và trạng thái account. Tuyệt đối không truy cập database `NEXORA`.
3. Connection string được truyền tường minh bằng QA environment secret với Windows Authentication; không echo connection string. `DatabaseConnectionPolicy` phải chấp thuận exact QA target.
4. Xác nhận cả bốn role tồn tại ngay trước startup để `RoleSeeder` không phải tạo role. Nếu role thiếu, dừng; không tự seed.
5. Change Request UserId 82 được giải quyết theo approval riêng, postcondition Admin-only đạt, test login Admin-only mới PASS. Nếu chưa đạt, không triển khai guard và không mở Admin UAT.
6. Owner phê duyệt thời điểm/port riêng (ví dụ 5071 sau khi xác nhận còn trống), startup connection và khả năng RoleSeeder đọc QA. Không dừng/chuyển hướng các process 5059/5060.

### Trước khi bắt đầu Manual UAT

1. Security Unit/HTTP suite hiện tại PASS trên runner tuân thủ policy, TRX mới kèm source manifest.
2. SQL regression đã được chạy riêng với database guard đúng target và quyền fixture ghi/xóa được owner duyệt; mọi fixture cleanup giới hạn đúng IDs đã tạo. Nếu chưa phê duyệt SQL fixture tests, đánh dấu các case cần persistence là NOT RUN/BLOCKED.
3. Owner xác nhận cục bộ credential cho một Customer-only, Sales-only, Warehouse-only và Admin-only; không nhập mật khẩu trong chat.
4. Phê duyệt riêng fixture plan; tạo order/review/promotion qua UI/workflow, không sửa trạng thái lịch sử bằng SQL.
5. Nếu kiểm thử delivery email thật là tiêu chí, chọn provider và mailbox QA; cấu hình secrets cục bộ và xin phép gửi trước lần gửi đầu tiên.

## 8. Phê duyệt riêng còn cần

| Hành động | Trạng thái | Phê duyệt cần có |
|---|---|---|
| Chạy fresh Identity Unit/HTTP tests | `BLOCKED` | IT xác nhận CI runner hoặc quy trình signing/allowlisting; không thay security policy. |
| Chạy SQL integration có fixture ghi/xóa | `NEEDS OWNER APPROVAL` | Cho phép đúng nhóm tests trên `NEXORA_IntegrationTests`, kèm preflight và fixture cleanup scope. |
| Xóa mapping Customer của UserId 82 | `NOT APPROVED` | Change Request `NEXORA-P61-QA-ROLE82` riêng, sau khi test gate đủ PASS và owner duyệt bản SQL cuối. |
| Chạy website source Phase 6 mới | `NOT AUTHORIZED IN THIS TURN` | Connection/source/port/RoleSeeder preflight và Admin lockout blocker đã đóng. |
| Tạo dữ liệu UAT | `NOT AUTHORIZED IN THIS TURN` | Owner duyệt danh sách fixtures, loại workflow và cleanup boundary. |
| Gửi email thật | `BLOCKED / NEEDS OWNER APPROVAL` | Provider, mailbox QA, cấu hình secret an toàn và đồng ý gửi tới recipient QA. |

## 9. Kết luận và bước ưu tiên

Không đủ điều kiện đề nghị chạy SQL remediation hay khởi chạy website Phase 6. Trạng thái là **`BLOCKED`**, không phải `READY FOR OWNER CHANGE APPROVAL`.

Thứ tự tiếp theo:

1. Chủ dự án/IT chỉ định runner CI được Windows Code Integrity cho phép hoặc phê duyệt signing/allowlisting.
2. Chạy Identity unit + fake HTTP suite trên đúng working-tree snapshot; xác minh runtime role-store harness, lưu TRX mới.
3. Chủ dự án duyệt riêng SQL integration fixture suite; chạy trên target QA đã preflight; xem kết quả và xác nhận không còn fixtures ngoài phạm vi.
4. Trình lại Change Request `NEXORA-P61-QA-ROLE82` với bằng chứng test mới và SQL precondition/postcondition cuối; chờ phê duyệt thực thi riêng.
5. Sau khi mapping được sửa bằng quy trình đã duyệt, xác minh Admin-only login/cookie; chỉ khi đó mới xin phép khởi chạy binary source mới.
6. Owner duyệt dữ liệu UAT, tạo fixtures qua workflow, rồi chạy checklist Customer → Sales → Warehouse → Admin và regression/responsive.
7. Xử lý real email UAT riêng nếu nằm trong tiêu chí nghiệm thu.

Tới thời điểm tài liệu này được lập: không truy cập database NEXORA chính hoặc QA trong lượt này; không thay đổi UserId 82; không ghi fixture; không khởi chạy/dừng website; không chạy migration/test; không gửi email; không commit/push. `docs/PHASE4_SCHEMA_REVIEW.sql` được giữ nguyên.
