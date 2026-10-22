# NEXORA – Kế hoạch triển khai Phase 6

**Trạng thái:** DRAFT UPDATED FOR OWNER REVIEW.

**Chính sách nghiệp vụ:** 9/9 OWNER APPROVED.

**Trạng thái code (2026-10-22):** Phase 6.1 vẫn có blocker nghiệm thu; Phase 6.2–6.4 IMPLEMENTED – BUILD VERIFIED, TEST EXECUTION BLOCKED/NOT RUN, MANUAL UAT NOT RUN; Phase 6.5 PREPARED, NOT ACCEPTED.

**Production:** NOT STARTED.

Thiết kế nền ở [`PHASE6_DESIGN_DRAFT.md`](./PHASE6_DESIGN_DRAFT.md); kết quả audit/gap ở [`PHASE6_AUDIT_REPORT.md`](./PHASE6_AUDIT_REPORT.md). Các policy phê duyệt là đầu vào nghiệp vụ, không phải bằng chứng chức năng đã có.

## Cổng 6.0 — Audit và chính sách

- **Phạm vi:** đối chiếu FR12, FR17, FR34–FR50, BR liên quan, source, Identity, EF model/migrations, test và tài liệu Phase 2–5.
- **Quyết định:** chủ dự án chốt 9/9 policies; xem Decision Ledger trong Design Draft.
- **Bàn giao:** audit matrix, design draft, implementation plan; IMPLEMENTATION_PLAN phản ánh chính xác trạng thái.
- **Kiểm tra:** tài liệu thống nhất bốn role, 22 business tables, status/transition, report definitions; status implementation không tăng nếu chưa có code/test.
- **An toàn DB:** không truy cập DB, SQL, migration hoặc `database update`.
- **Tiêu chí hoàn tất:** audit docs hoàn chỉnh, policy 9/9 được ghi, chủ dự án review bản thiết kế. **Audit tài liệu đã hoàn tất; approval tài liệu không tự cho phép bắt đầu code.**

## 6.1 — Account Administration

**Trạng thái hiện tại:** BLOCKED – current-source security runtime tests have not been verified; the QA multi-role anomaly and separate owner approval remain unresolved. Earlier implementation and regression results below are historical and do not verify the later fail-closed identity guard.

### Phạm vi và policy

- FR34–FR36 / UC-A01–A03: tìm/xem user; khóa/mở bằng `IsActive`; tạo staff riêng Sales/Warehouse bằng invitation; chuyển role Sales↔Warehouse; không chuyển Customer thành staff; mỗi user đúng một role.
- Admin UI không grant Admin. Admin cuối cùng được bảo vệ; self-lock/self-demotion bị chặn; cấp/khôi phục Admin chỉ qua quy trình đặc quyền nội bộ.
- Invite user inactive; không mật khẩu mặc định; gửi link có expiry/replay protection; chỉ activate sau xác thực + password setup; email failure giữ inactive; resend dùng account hiện tại.

### Code components đã triển khai

- `NEXORA.Application/Identity/AccountAdministrationContracts.cs`: filters/DTOs, lifecycle policy, operation result và service contract.
- `NEXORA.Infrastructure/Identity/IdentityAccountAdministrationService.cs`: query/paging; create/resend/activate; lock/unlock; Sales↔Warehouse; Identity store dùng chung `AppDbContext`.
- `NEXORA.Web/Areas/Admin/Controllers/UsersController.cs` và `Areas/Admin/Views/Users`: Index/Details/CreateStaff, POST Lock/Unlock/ChangeRole/ResendInvitation; controller `[Authorize(Roles=Admin)]`, các POST có antiforgery.
- `StaffActivationController`, view và `staff-activation.js`: token Data Protection purpose-specific, hạn 24 giờ. Email/token ở URL fragment (không gửi trong request URL), được xác minh qua POST antiforgery; chỉ kích hoạt sau khi nhân viên tự đặt mật khẩu.
- Development dùng mailbox giả lập nội bộ, chỉ Admin/loopback truy cập; ngoài Development, `UnconfiguredEmailSender` từ chối gửi cho đến khi provider được cấu hình. Không gửi email thật trong lượt này.
- `AuthoritativeAccountCookieValidation`: mỗi request xác thực kiểm tra trạng thái active, security stamp và role hiện hành; lỗi đọc trạng thái thì reject cookie (fail closed).
- Structured `ILogger` ghi actor/target ID, action, role và outcome; không ghi email, password, token, password hash hoặc security stamp.

### Model/transaction design notes

`AppUser` hiện có `IsActive`; base Identity có `EmailConfirmed`, nullable `PasswordHash`, `LockoutEnd`, security/concurrency stamp; chưa có invitation-state property. POC/test phải chứng minh pending state bằng các trường hiện tại, token purpose/lifetime, invalidation khi resend, single-use kể cả race, activation atomicity và recovery khi commit/email outcome không rõ. Nếu cần persisted invite state/time/token để đáp ứng policy, dừng và xin duyệt schema; không tự thêm field.

Identity operations dùng cùng scoped `AppDbContext` cho `UserManager`, `RoleManager` và service. Toàn bộ transaction chạy qua `Database.CreateExecutionStrategy()`/`ExecuteInTransactionAsync`; `sp_getapplock` transaction-owned tuần tự hóa thay đổi account/role và bảo vệ Admin cuối cùng. Khi kết quả commit không rõ, code xác minh trạng thái đã lưu trước khi xem thao tác là thành công; tạo account kiểm tra duplicate theo normalized email/username. Email gửi sau commit; delivery failure để account pending và cho phép resend cùng account.

### Test plan

- Unit: allowlist role; đúng một role; Customer→staff/Admin grant bị từ chối; Sales↔Warehouse hợp lệ; self-lock/self-demotion/last-admin bị từ chối.
- SQL: create user+role atomic; failed email leaves inactive; resend không duplicate và vô hiệu link cũ; token expiry/replay/concurrent redeem; active-session lock rejected next request; role swap invalidates stale claim; two Admin đồng thời không thể gỡ Admin cuối; retry/commit ambiguity.
- HTTP: các endpoint Admin-only; mọi POST cần antiforgery; Customer/Sales/Warehouse/anonymous bị chặn; session validation áp dụng cả route không phải Profile.
- Browser/Manual: tạo staff, email nhận thật, hoàn tất setup, login đúng role, lock user đang login và kiểm tra request kế tiếp, resend/expired link.

### Kết quả regression 2026-10-22

**Lưu ý:** các kết quả trong mục này thuộc baseline trước thay đổi fail-closed guard, không phải regression của source hiện tại. Chi tiết blocker hiện tại ở [`PHASE6_1_MULTIROLE_REMEDIATION.md`](./PHASE6_1_MULTIROLE_REMEDIATION.md).

- `dotnet restore NEXORA.sln`: PASS.
- `dotnet build NEXORA.sln --no-restore`: PASS, 0 warning / 0 error; dùng output cô lập `tests/NEXORA.Tests/bin/Phase61` do website giữ khóa output thông thường.
- `dotnet test NEXORA.sln --no-build --no-restore`: **240 passed, 0 failed, 0 skipped**. TRX: 178 unit/domain/controller, 6 HTTP test-host, 56 SQL Server integration.
- SQL target đọc trực tiếp trước/sau test: `PC01\SQLEXPRESS / NEXORA_IntegrationTests`, ONLINE; 27 bảng (22 nghiệp vụ + 4 Identity + history), ba migration hiện có, bốn role chuẩn. User count 6 trước/sau; 0 fixture `qa.phase61.*` còn lại; anomaly một user multi-role cũ vẫn giữ nguyên.
- Regression bao phủ create+role rollback; invitation activate/replay/expiry/resend; concurrent redeem và create; delivery failure; pending/suspended/unlock; cấm Customer promotion; Sales↔Warehouse và từ chối stale cookie ở request kế tiếp; bảo vệ Admin cuối; HTTP Admin-only/antiforgery và invitation link fragment.
- `dotnet ef migrations has-pending-model-changes`: không có thay đổi model kể từ migration cuối; design-time connection trỏ tạm tới QA, lệnh không truy vấn/cập nhật dữ liệu. Không tạo migration.
- `node --check` file JS mới và `git diff --check`: PASS. Manual browser UAT: NOT RUN, chờ chủ dự án. Email thật: chưa thể UAT do chưa cấu hình provider/mailbox được duyệt.

### Acceptance, security và DB safety

- **Acceptance:** chỉ bốn role; mỗi account một role; các transition đúng policy; Admin UI không có option Admin; user chưa setup không login; lock có hiệu lực ở authenticated request kế tiếp sau commit; email invitation provider thật hoạt động; link hết hạn/không dùng lại; không duplicate khi resend; lịch sử order/inventory/actor không đổi.
- **Security risks:** stale cookie/claims, privilege escalation, multi-role race, last-admin race, open redirect/token leakage, email enumeration, link replay, log secrets. Mitigate bằng server policies, principal reload/validation, allowlist, antiforgery, local return URL validation, token purpose/expiry, structured logging redaction.
- **Database safety:** chỉ database QA test đã xác minh; không chạy test ghi trên `NEXORA` chính. Không migration dự kiến. Nếu model không đủ thì dừng để review schema. Production invitation chưa được nghiệm thu cho tới khi provider/prod secret cấu hình qua kênh an toàn và delivery được xác minh.
- **Điều kiện hoàn tất:** test suite đầy đủ không còn skip không giải thích; manual invitation/security UAT được ghi evidence; model/migration drift bằng 0 hoặc migration riêng đã được review/duyệt; acceptance owner sign-off.

## 6.2 — Favorites & Customer Reviews

**Trạng thái hiện tại:** IMPLEMENTED – BUILD VERIFIED; tests and manual UAT NOT RUN. Xem [`PHASE6_2_ACCEPTANCE.md`](./PHASE6_2_ACCEPTANCE.md) và [`PHASE6_2_MANUAL_UAT.md`](./PHASE6_2_MANUAL_UAT.md).

### Phạm vi và policy

- FR12/UC-C10/BR09: Customer add/list/remove; một product tối đa một lần trong favorites.
- FR17/UC-C15/BR02: chỉ Order owner, đúng OrderDetail/Product, Order COMPLETED; 1 review/OrderDetail; rating 1–5; mới `PENDING`; customer xem status của mình; không sửa/xóa review sau submit; không seed review.

### Code components dự kiến

- Domain review constants/transition-independent validation quanh `ReviewEligibilityPolicy`; Application `IFavoriteService`/`IReviewService` + DTOs; Infrastructure EF Core implementations; Customer `FavoritesController`/`ReviewsController`, view models và Razor views/actions.
- Identity UserId luôn từ ClaimsPrincipal. Unique index/FKs hiện có giữ nguyên; client không truyền owner ID đáng tin cậy.

### Test plan

- Unit: rating/comment, ReviewEligibilityPolicy, exact matching owner/order/detail/product/status.
- HTTP: Customer-only; anonymous/staff denied; anti-forgery; spoofed user/order detail rejected; own status visible; no edit/delete actions.
- SQL: duplicate favorites insert race handled idempotently; review unique race; FK composite rejects mismatch; completed-order ownership; review does not appear public before moderation.
- Browser/manual: add/remove/list favorites; submit review from completed order; pending state visible to owner only.

### Acceptance, security và DB safety

- **Acceptance:** FR12/FR17 flow usable; exact owner scope enforced server-side; duplicate cannot persist; review only from completed order line; PENDING private; public query never exposes PENDING/HIDDEN/unknown; rating valid; customer cannot mutate posted review.
- **Security risks:** IDOR, spoofed ownership, race duplicate, comments XSS/oversize, enumeration. Use principal-derived ID, service query authorization, output encoding, validation and unique constraints.
- **Database safety:** use current `favorites`/`reviews`; no table/column/migration. SQL tests only on verified QA DB; no cleanup or writes to main.
- **Điều kiện hoàn tất:** unit/HTTP/SQL suites, no skipped required cases, manual UAT and FR/BR traceability updated after evidence.

## 6.3 — Review Moderation

**Trạng thái hiện tại:** IMPLEMENTED – BUILD VERIFIED; tests and manual UAT NOT RUN. Xem [`PHASE6_3_ACCEPTANCE.md`](./PHASE6_3_ACCEPTANCE.md) và [`PHASE6_3_MANUAL_UAT.md`](./PHASE6_3_MANUAL_UAT.md).

### Phạm vi và policy

- FR44/UC-A11. Canonical states: `PENDING`, `APPROVED`, `HIDDEN`; new review PENDING; only APPROVED public; owner sees own status; Admin can approve/hide/restore; Admin cannot edit rating/comment.
- Transitions: PENDING→APPROVED/HIDDEN; APPROVED→HIDDEN; HIDDEN→APPROVED. Same-state command is no-op. Unknown stored status fails closed: not public, no mutation, Admin sees unknown-state warning for manual review.
- Decision 06: keep reviews schema; no moderator columns/history table; use structured `ILogger` and explicitly accept non-persistent audit limits.

### Code components dự kiến

- Domain constants/policy for state and transitions; Application moderation command/query DTOs; Infrastructure query/update service; Admin Reviews controller and paginated queue/details/actions. Public review query filters exactly APPROVED.
- `Status` remains string with existing max length; canonical values validated at application/domain layer. No schema changes.

### Test plan

- Unit: full transition matrix, idempotent same-state, unknown value fails closed.
- HTTP: only Admin mutations; Customer/Sales/Warehouse/anonymous denied; antiforgery; Admin update leaves Comment/Rating unchanged.
- SQL: persistence status and UpdatedAt; concurrency conflict does not overwrite another decision; public query selects APPROVED only; logger event payload allowlist test.
- Manual: approve, hide approved, restore, owner sees own status, other Customer/public cannot see non-approved review.

### Acceptance, security và DB safety

- **Acceptance:** every permitted transition works; disallowed/unknown status stays unchanged and hidden; review text/rating immutable through moderation; structured event captures allowed fields; docs never call ILogger a persistent audit history.
- **Security risks:** unauthorized publication, status tampering, comment XSS, log PII/token leakage. Enforce Admin at endpoint/service, canonical transition policy, Razor encoding and log allowlist.
- **Database safety:** no schema/table additions; pre-existing unknown status is not auto-migrated. QA read-only inspection before enabling moderation; any data cleanup requires separate approval.
- **Điều kiện hoàn tất:** transition/role/SQL/manual tests pass; moderation limitation displayed/documented; owner acceptance.

## 6.4 — Admin Dashboard & Reporting

**Trạng thái hiện tại:** IMPLEMENTED – BUILD VERIFIED; SQL/HTTP tests and manual UAT NOT RUN. Xem [`PHASE6_4_ACCEPTANCE.md`](./PHASE6_4_ACCEPTANCE.md) và [`PHASE6_4_MANUAL_UAT.md`](./PHASE6_4_MANUAL_UAT.md).

### Phạm vi và policy

- FR46: real DB-backed Dashboard; no fabricated metrics.
- FR47: filter by `Order.OrderDate`.
- FR48: only COMPLETED; filter by `Order.CompletedAt`; sum `Order.TotalAmount` once (BR13–BR14).
- FR49: only COMPLETED; filter by `Order.CompletedAt`; sum `OrderDetail.Quantity` by `ProductId`; no current Product.Price revenue inference; no JOIN multiplication.
- FR50: Admin enters integer threshold >=0; `AvailableQuantity = QuantityInStock - ReservedQuantity`; zero=out, positive <= threshold=low, higher=normal; missing Inventory distinct; invalid negative/inconsistent row=anomaly, not clamped; no threshold persistence; read only.
- Date input uses `Asia/Ho_Chi_Minh`, both boundaries converted to UTC half-open `[startUtc, endExclusiveUtc)`. No `23:59:59`.

### Code components dự kiến

- `NEXORA.Application.Admin` report query contracts, date/threshold filters and DTOs; `NEXORA.Infrastructure.Admin` SQL-side `AsNoTracking` aggregations; Admin Reporting controller, view models, Razor summary/tables/charts only if justified.
- Keep aggregates at correct grain: Orders-only count/revenue queries separately from OrderDetails product units. Page detail lists; no entire-table client aggregation. Inventory query distinguishes missing/valid/anomalous.
- Time-zone adapter canonicalizes IANA `Asia/Ho_Chi_Minh`; on Windows resolve IANA where ICU mode permits, otherwise explicit `SE Asia Standard Time` fallback; startup health check must fail clearly if unavailable. Do not use `TimeZoneInfo.Local`.

### Test plan

- Unit: DateOnly conversion, StartDate > EndDate, inclusive local end day, month/year rollover, UTC start/end exact boundaries, timezone resolver Windows modes; threshold boundaries 0 and N; anomaly/missing classification.
- SQL: FR47 uses OrderDate; FR48 CompletedAt and only COMPLETED; correct TotalAmount with multiple order lines; CompletedAt null excluded; FR49 sums Quantity by ProductId only COMPLETED, all six other statuses excluded; historical Product.Price changes do not alter results; no duplicate quantities; missing Inventory distinct; bad legacy values anomaly; empty DB.
- HTTP: Admin-only read endpoints, invalid range/negative threshold rejected, pagination/filter validation, no mutation verbs.
- Manual: Admin verifies daily/monthly/year spanning reports, timezone labels and zero/empty state; non-Admin denied.

### Acceptance, security và DB safety

- **Acceptance:** all FR definitions exactly as above; SQL integration expected sums independently computed from fixture; local date boundaries correct; UI shows Vietnam time, not raw UTC; no stock mutation; responsive and empty/error states pass.
- **Security/performance risks:** report data leakage, expensive unbounded aggregates, join fanout, timezone/local clock drift, client-side manipulation. Use Admin authorization, server-side filters, range limits/paging, SQL aggregates, checked timezone resolver and read-only contracts.
- **Database safety:** no migration or data writes. Only read-only query tests plus isolated fixture setup/cleanup on verified `NEXORA_IntegrationTests`; never QA against production Development `NEXORA`. New index only with query-plan evidence and separate approval.
- **Điều kiện hoàn tất:** all report SQL definitions tested; no skipped cases; output matches independent fixtures; performance measured on stated QA dataset; owner reviews definitions/UI.

## 6.5 — Final Regression, UAT and Acceptance

**Trạng thái hiện tại:** PREPARED – REGRESSION/UAT BLOCKED; NOT ACCEPTED. Xem [`PHASE6_5_HARDENING_PLAN.md`](./PHASE6_5_HARDENING_PLAN.md).

### Phạm vi và policy

- Cross-role end-to-end regression of new Phase 6 plus Phase 1–5: four roles; single-role invariant; account/session policy; favorites/reviews/moderation; FR46–FR50; existing order state machine, reservation and stock ledger unchanged.
- Review NFR10 event coverage for role changes, lock/unlock, review moderation and product price updates. FR45 basic price edit already exists; persistent price history is not added.

### Code/document components dự kiến

- Test fixtures/helpers, authorization/security regression, manual UAT checklist, updated acceptance and traceability docs. No new business feature should be hidden in final hardening.
- **FR45 gap requiring scope review:** `ProductsController`/`ProductManagementService.SaveAsync` can update Price/DiscountPrice; `OrderDetail` stores ProductId, ProductName, Quantity, UnitPrice, DiscountAmount, TotalPrice and existing tests preserve purchase snapshot. No price history exists; service currently has no structured price-change event. Review whether add operational `ILogger` with actor/product/old-new price/result/correlation is accepted for NFR10; do not create history table. Any enhancement is a separately approved gap before code.

### Test plan

- `dotnet restore`, fresh `dotnet build`, full `dotnet test`; report unit/HTTP/SQL counts separately. SQL tests only against explicitly verified QA test DB.
- `dotnet ef migrations has-pending-model-changes` and inspect migration list; no migration expected. If a schema change becomes necessary, stop and seek separate review/authorization.
- `git diff --check`, secret/data scan, no QA fixture/credential committed.
- Manual UAT by role with no fabricated screenshots; record case-level evidence and viewport/device; distinguish owner-confirmed manual checks from Codex browser automation.

### Acceptance, security và DB safety

- **Acceptance:** all Phase6 rows have evidence; regression preserves Phase1–5 behavior; no unexplained skipped tests; docs align to implementation; owner signs off Development/QA separately from production deployment.
- **Security risks:** cross-role access, stale sessions, credential/log leakage, personal data in reports, test contamination. Run role matrix, redact logs, only sanitized test fixtures, exact QA DB checks.
- **Database safety:** no main-DB writes; QA only for tests; no migration unless reviewed/approved independently; preserve 22-table policy and all existing QA data outside named fixtures.
- **Điều kiện hoàn tất:** owner approves acceptance; commit/push require separate instruction; production remains NOT DEPLOYED until separate migration/deployment approval.

## Phase gate and current status

1. Phase 6.0 analysis is complete; 9/9 business decisions are approved and represented in the design.
2. Phase 6.1 remains unaccepted: current-source security runtime verification, QA multi-role remediation approval, manual UAT and real email delivery remain blocked/pending.
3. Phase 6.2–6.4 code is implemented and the current source builds; test execution and browser/manual UAT are not verified. Phase 6.5 has been prepared, not accepted. Production deployment is out of scope.
4. Every schema change, main DB access, production email setup, commit or push requires its own explicit scope/approval.

**Current status: Phase 6.0 BUSINESS POLICIES APPROVED (9/9); Phase 6.1 ACCEPTANCE BLOCKED; Phase 6.2–6.4 IMPLEMENTED – BUILD VERIFIED, TESTS NOT RUN, MANUAL UAT NOT RUN; Phase 6.5 PREPARED, NOT ACCEPTED; production NOT STARTED.**
