# NEXORA – Phase 6: Quản trị và báo cáo

**Trạng thái tài liệu:** UPDATED FOR OWNER REVIEW.

**Trạng thái triển khai hiện tại (2026-10-22):** 6.1 vẫn có blocker nghiệm thu; 6.2–6.4 đã có code và build hiện tại thành công nhưng test execution/manual UAT chưa được xác minh; 6.5 đã chuẩn bị, chưa nghiệm thu; production NOT STARTED. Phần nội dung dưới đây vẫn là policy/design baseline đã duyệt, không tự mở rộng phạm vi.

**Kiến trúc:** Modular Monolith, ASP.NET Core MVC, Application/Domain/Infrastructure hiện hữu.

**Ranh giới:** giữ bốn role, 22 bảng nghiệp vụ, state machine và nghiệp vụ Sales/Warehouse Phase 5.

## 1. Mục tiêu và phạm vi

| Subphase | Chức năng dự kiến | FR/UC/BR |
|---|---|---|
| 6.1 | Tra cứu/khóa/mở tài khoản; tạo Sales/Warehouse; chuyển role nhân viên; invitation an toàn | FR34–FR36, UC-A01–A03, BR08, NFR02/NFR03/NFR10 |
| 6.2 | Customer thêm/xem/xóa favorites; gửi và xem review đủ điều kiện | FR12/UC-C10/BR09; FR17/UC-C15/BR02 |
| 6.3 | Admin kiểm duyệt review theo ba trạng thái được duyệt | FR44/UC-A11, NFR03/NFR10 |
| 6.4 | Dashboard, thống kê đơn, doanh thu, lượng sản phẩm hoàn tất và tồn khả dụng | FR46–FR50, UC-A13–A17, BR13/BR14 |
| 6.5 | Regression, bảo mật/log review, UAT và bàn giao | Tất cả phạm vi Phase 6 |

FR37–FR43 giữ trạng thái COMPLETED trong audit. Các màn quản trị Category, Brand, Product/Image, Specification, Consultation và Promotion được tái sử dụng; không viết lại nếu không có gap được chứng minh.

## 2. OWNER-APPROVED BUSINESS DECISIONS

Chủ dự án đã chốt **9/9 quyết định**. Phần này là business policy đã duyệt; những phần “đề xuất kỹ thuật” ở các mục sau vẫn cần kiểm chứng trong code/test. Việc duyệt policy không xác nhận chức năng đã được triển khai.

### Decision 01 — Admin role grant: APPROVED, Option A

- Giao diện Admin chỉ tạo tài khoản nhân viên và cấp `Sales` hoặc `Warehouse`; không cấp `Admin`.
- Cấp/khôi phục Admin chỉ qua quy trình đặc quyền nội bộ có giới hạn người thực hiện, xác nhận riêng và log an toàn; không có endpoint công khai, không hardcode credential.
- Luôn bảo vệ ít nhất một Admin đang hoạt động; cấm tự khóa hoặc tự gỡ quyền Admin; chống race giữa thao tác của nhiều Admin.

### Decision 02 — Account and role transition: APPROVED, Option A

- Mỗi tài khoản có đúng một trong bốn role `Customer`, `Sales`, `Warehouse`, `Admin`.
- Không chuyển tài khoản Customer hiện có thành nhân viên; không cho nhân viên thực hiện nghiệp vụ Customer. Sales/Warehouse dùng tài khoản riêng.
- Cho phép chuyển `Sales ↔ Warehouse` qua quy trình Admin có kiểm soát. Giao dịch phải gỡ role cũ, gán role mới nguyên tử và ngăn multi-role.
- Thu hồi/cập nhật phiên cũ để role claim cũ không tiếp tục cấp quyền. Bảo toàn order, inventory và actor history đã phát sinh.
- Không đổi role/quyền Phase 5. Không suy rộng thành chuyển `Admin ↔ staff` hoặc `staff → Customer`.

### Decision 03 — Account disable/session revocation: APPROVED, Option A

- Khóa nghiệp vụ bằng `AppUser.IsActive = false`, duy trì tới khi Admin mở thủ công; trạng thái này từ chối login mới.
- Cookie/session đã phát hành phải bị từ chối từ request xác thực kế tiếp sau khi khóa commit; không chỉ đợi security-stamp validation định kỳ.
- `IdentityUser.LockoutEnd` tiếp tục dành cho chống brute-force, không dùng làm cờ khóa Admin.
- Kiểm thử account đang có phiên, cập nhật đồng thời và lỗi cập nhật. Không tuyên bố hủy được request đã bắt đầu trước thời điểm commit thao tác khóa.

### Decision 04 — Staff invitation: APPROVED, Option A

- Tạo Sales/Warehouse chưa kích hoạt; không phát hành mật khẩu mặc định. Nhân viên nhận email invitation/reset link và tự thiết lập mật khẩu.
- Token có expiry, validation và chống tái sử dụng theo thiết kế được kiểm chứng. Chỉ kích hoạt sau khi hoàn tất xác thực/thiết lập mật khẩu.
- Email gửi thất bại thì tài khoản vẫn bất hoạt; Admin gửi lại lời mời trên cùng tài khoản, không tạo duplicate.
- Không ghi token/password/invitation URL vào log. Email provider thực tế và email nhận được là điều kiện nghiệm thu; UI gửi lời mời không phải bằng chứng email hoạt động.
- Công cụ QA provisioning hiện có không thay cho Admin invitation. Không tự thêm field/table nếu model chưa biểu diễn được trạng thái bắt buộc; báo cáo và xin duyệt trước.

### Decision 05 — Review workflow: APPROVED, Option A

- Chỉ Customer sở hữu Order được gửi review cho đúng `OrderDetail` và `Product` khi `Order.Status == COMPLETED`; mỗi OrderDetail tối đa một review; rating 1–5; không nhận owner/OrderDetail giả từ client.
- Review mới `PENDING`; chỉ `APPROVED` được công khai; `HIDDEN` không công khai. Customer xem review của mình cùng trạng thái.
- Admin được duyệt, ẩn và khôi phục hiển thị. Customer không sửa/xóa sau khi gửi; Admin không chỉnh rating/comment. Không seed review giả.

### Decision 06 — Moderation audit: APPROVED, Option B

- Giữ schema `reviews`; không thêm `moderated_by`, `moderated_at`, `moderation_reason`; không tạo review/moderation history table.
- Ghi structured `ILogger` event với actor ID, review ID, action, status trước/sau, result và correlation ID phù hợp.
- Không log credential, token, cookie hoặc dữ liệu nhạy cảm không cần thiết.
- Chấp nhận retention/durability/queryability của ILogger có giới hạn; **không gọi đây là persistent audit history đầy đủ**. Audit bền vững khác OUT OF SCOPE trừ khi được duyệt lại.

### Decision 07 — Product sales report FR49: APPROVED, Option A

- Chỉ order `COMPLETED`; thời gian theo `Order.CompletedAt`; số lượng là `SUM(OrderDetail.Quantity)` nhóm theo `ProductId`.
- Loại trừ `PENDING`, `CONFIRMED`, `PREPARING`, `EXPORTED`, `SHIPPING`, `CANCELLED`.
- Dùng snapshot OrderDetail khi cần hiển thị lịch sử; không dùng `Product.Price` hiện tại để suy ra doanh thu; tránh nhân Quantity do JOIN. Report chỉ đọc.

### Decision 08 — Low stock FR50: APPROVED, Option A

- Admin nhập threshold mỗi truy vấn; threshold là số nguyên không âm và không lưu database.
- `AvailableQuantity = QuantityInStock - ReservedQuantity`.
- `AvailableQuantity = 0`: hết hàng khả dụng; `0 < AvailableQuantity <= Threshold`: sắp hết; `AvailableQuantity > Threshold`: bình thường.
- Thiếu Inventory là “Chưa có dữ liệu tồn kho”, không diễn giải là 0. Admin chỉ đọc report; không thay đổi IN/OUT/RESERVE/RELEASE/ADJUST.
- Nếu gặp giá trị tồn không hợp lệ/âm, report gắn nhãn anomaly, không clamp hoặc phân loại như số bình thường; không tự sửa dữ liệu.

### Decision 09 — Report timezone and date ranges: APPROVED, Option A

- Persist timestamp theo UTC; UI nhập ngày theo `Asia/Ho_Chi_Minh`; bao trọn ngày cuối.
- Chuyển sang khoảng UTC nửa mở `[startUtc, endExclusiveUtc)`, query `Timestamp >= startUtc AND Timestamp < endExclusiveUtc`; không dùng `23:59:59`.
- FR47 dùng property `Order.OrderDate` (ngày tạo đơn trong model); FR48 chỉ COMPLETED, theo `CompletedAt`, `SUM(Order.TotalAmount)`; FR49 chỉ COMPLETED, theo `CompletedAt`, sum `OrderDetail.Quantity` theo ProductId.
- Kiểm tra range sai, biên tháng/năm, qua nửa đêm, `CompletedAt == null`, empty data. Không hiển thị UTC thô như giờ Việt Nam.

## 3. Ma trận quyền đã chốt

Authorization luôn thực thi server-side. View/menu chỉ hỗ trợ UX.

| Role duy nhất | Account/role admin | Favorites/review | Review moderation | Reports | Order/Warehouse Phase 5 |
|---|---|---|---|---|---|
| Customer | Không | Chỉ chính mình; review đủ điều kiện từ OrderDetail COMPLETED | Không | Không | Luồng Customer hiện tại |
| Sales | Không | Không được thay Customer | Không | Không theo FR46–50 | Quyền Sales hiện tại, giữ nguyên |
| Warehouse | Không | Không được thay Customer | Không | Không theo FR46–50 | Quyền Warehouse hiện tại, giữ nguyên |
| Admin | Tạo Sales/Warehouse; chuyển Sales↔Warehouse; không grant Admin qua UI | Không giả mạo hành động Customer | Duyệt/ẩn/khôi phục theo policy | Chỉ đọc | Giữ Admin read-only inventory; không mutate kho |

Tài khoản Customer không chuyển thành staff. Sales/Warehouse không có thêm role Customer; mỗi principal chỉ có đúng một role. Quy trình nội bộ đặc quyền Admin nằm ngoài public/Admin UI.

## 4. Model Identity và invitation — gap kỹ thuật cần kiểm chứng

Source hiện có `AppUser.IsActive`, Identity `EmailConfirmed`, Identity `PasswordHash` (nullable trong store model) và `LockoutEnd`. Chưa có property/trạng thái invitation riêng. AccountController hiện đăng ký Customer với password và gán role; Forgot/Reset chỉ xử lý user `IsActive`; production `IEmailSender` hiện là `UnconfiguredEmailSender`, còn Development dùng mailbox giả lập. Do đó Admin invitation chưa được triển khai và email production chưa sẵn sàng.

**Phương án kỹ thuật sơ bộ, chưa phải kết luận đủ model:** biểu diễn pending staff bằng `IsActive=false`, `EmailConfirmed=false`, chưa có PasswordHash; khi user hoàn tất link + đặt password, xác nhận email và chỉ kích hoạt sau khi toàn bộ điều kiện đạt. Identity token provider có thể cấp token purpose-specific có expiry; resend cần vô hiệu token cũ; consume phải được bảo vệ concurrency để token không dùng hai lần. Không tái sử dụng nguyên xi ResetPassword action vì hiện action từ chối `IsActive=false` và không thực hiện quy trình invitation.

Điểm cần xác minh khi code 6.1: Identity cho phép tạo user không password; token lifetime cấu hình riêng cho invitation; một lần dùng dưới replay/concurrent POST; cách `SecurityStamp`/`ConcurrencyStamp` invalidate token/session; atomicity khi set password + confirm + activate; account có thể gửi lại lời mời mà không duplicate. Model không lưu “đã gửi lúc nào”, delivery state hoặc token ID; nếu các thông tin đó trở thành điều kiện nghiệp vụ/audit bắt buộc thì dừng để xin duyệt model/schema, không tự thêm cột. Nếu SMTP/provider lỗi, giữ user inactive và cung cấp resend an toàn.

Nếu không chứng minh được token one-time/replay resistance bằng Identity token purpose, security/concurrency stamp và atomic state transition hiện có, đây là blocker thiết kế cho luồng invite: dừng trước migration/code mở rộng model và xin chủ dự án duyệt phương án. Không được tuyên bố quyết định 04 đã được đáp ứng chỉ dựa trên việc gửi được URL.

## 5. Account administration — workflow dự kiến

1. Admin tìm user theo email/tên/trạng thái/role với server paging; không đưa PasswordHash/SecurityStamp ra view.
2. Lock/unlock thay `IsActive`, cập nhật `UpdatedAt` và security stamp phù hợp; active check phải đọc authoritative state ở mọi request xác thực kế tiếp.
3. Staff create: tạo Sales/Warehouse user inactive; assign đúng role trong cùng transaction; gửi link sau commit. Email failure giữ account pending; resend cùng account.
4. Role transition duy nhất là Sales↔Warehouse; trong transaction gỡ role cũ/gán mới, bảo đảm đúng một role; invalidate cookie/role claims; bảo toàn lịch sử actor FK.
5. Không cho self-lock/self-demotion, không hạ Admin cuối cùng; không cấp Admin từ UI. Các operation cạnh tranh được serialize/compare-and-swap và có result rõ.

**Kỹ thuật đề xuất cần test:** dùng `UserManager<AppUser>`, `RoleManager<AppRole>` và `AppDbContext` cùng DI scope; transaction bên trong `Database.CreateExecutionStrategy()` vì SQL Server retry đang bật. Cân nhắc SQL app lock/serializable guard theo target user cho role transition, và guard dùng chung cho số Admin active. Retry phải idempotent/verify result, không nhân đôi user/role. Đây là giải pháp kỹ thuật đề xuất, cần thiết kế/kiểm tra riêng chứ không phải schema decision.

**Session validation:** hiện Login kiểm tra `IsActive`, nhưng middleware chưa có bằng chứng kiểm tra active state mỗi request sau khi cookie đã cấp; security-stamp định kỳ không đáp ứng policy “request xác thực kế tiếp”. Thiết kế cần cookie principal validation (hoặc cơ chế tương đương) đọc active state và role/stamp authoritative ở mỗi request; khi inactive thì reject cookie/sign out. Đo độ trễ DB/cache và kiểm thử trên target .NET/SQL. Cache chỉ được dùng nếu invalidation bảo đảm trong policy. Request đã bắt đầu trước khi lock transaction commit có thể hoàn thành.

## 6. Favorites và Reviews — workflow dự kiến

### Favorites

- Tái dùng `Favorite` và unique `(user_id, product_id)`; không thêm bảng/cột.
- Customer-only list/add/remove; user ID lấy từ ClaimsPrincipal; POST antiforgery; duplicate add idempotent, unique index là bảo vệ cuối khi có race. Không cho sửa favorite của người khác.

### Review submit/read

- Customer-only write; service tải Order + Detail theo identity hiện tại, validate owner, COMPLETED, matching OrderDetail/Product, no existing review; kiểm tra lại trong transaction. DB unique/FK là hàng rào cuối chống spoof/race.
- Review create mặc định PENDING; rating 1–5; comment nullable/max 3000 theo model hiện hữu; validation server-side. Customer chỉ đọc review mình cùng status. Customer không sửa/xóa sau submit; Admin không thay rating/comment.
- Public query chỉ trả `APPROVED`. Không trả `PENDING`, `HIDDEN`, unknown status hoặc dữ liệu của user không liên quan.

### Review status transition

`Review.Status` hiện là `string`, max 30, default `PENDING`; chưa có enum/CHECK constraint. Chuẩn hóa Domain bằng các hằng/enum (`PENDING`, `APPROVED`, `HIDDEN`) và policy transition; lưu chuỗi canonical vào cột hiện có, không tạo migration.

| Trạng thái hiện tại | Admin action | Trạng thái sau | Hệ quả |
|---|---|---|---|
| Review mới | Hệ thống tạo | `PENDING` | Chưa công khai |
| `PENDING` | Approve | `APPROVED` | Có thể hiển thị public |
| `PENDING` | Hide | `HIDDEN` | Không công khai |
| `APPROVED` | Hide | `HIDDEN` | Gỡ khỏi public |
| `HIDDEN` | Restore/approve | `APPROVED` | Hiển thị lại public |
| Bất kỳ status hợp lệ | Lặp lại action hiện tại | Không đổi | Idempotent no-op/result `AlreadyInState`; không sửa nội dung |
| Giá trị status lạ/không canonical | Bất kỳ mutation | Không đổi | Fail closed; ẩn public; Admin thấy “trạng thái không xác định”, ghi structured warning an toàn và yêu cầu xử lý thủ công |

Không tự normalize status lạ thành `PENDING`; đọc/điều tra dữ liệu trước. `UpdatedAt` cập nhật cho transition có thay đổi. Status lạ không được phép qua transition policy.

## 7. Review moderation và audit giới hạn

Admin queue có filter status canonical, product/date/search và paging. Chỉ Admin được quyết định; endpoint POST antiforgery và service kiểm tra transition. Không có cột moderator/time/reason và Decision 06 cấm bổ sung. Structured ILogger ghi `ActorUserId`, `ReviewId`, action, status-before/status-after, result/correlation ID; tránh email/comment nếu không cần. Đây là operational logging có retention/queryability phụ thuộc sink, không phải persistent moderation history. Các cơ chế audit bền vững khác OUT OF SCOPE.

## 8. Reporting — definitions và truy vấn

### Kiến trúc

- Application định nghĩa filter/DTO/read-only query contract; Infrastructure dùng EF Core `AsNoTracking` và SQL-side projection/group/aggregate; Admin MVC controller/view chỉ nhận DTO. Không tính báo cáo trong JavaScript hoặc materialize toàn bảng.
- Tách aggregate order-level khỏi joins 1:N để `Order.TotalAmount` không bị nhân. Paginate detail; validate range/threshold; empty result hiển thị zero/empty state, không mock.
- Giữ Dashboard metrics hiện có nếu đúng; mở rộng bằng truy vấn SQL thật. Admin report read-only; không gọi command stock/transition.

| FR | Bộ lọc/nguồn thời gian | Định nghĩa dữ liệu |
|---|---|---|
| FR46 / UC-A13 | Snapshot truy vấn tại thời điểm xem | KPI đọc từ các bảng hiện có: product/category/orders/completion/inventory. Không thêm metric giả; không mặc định biểu đồ/exports ngoài FR. |
| FR47 / UC-A14 | `Order.OrderDate` (property thực tế; map snake_case thành `order_date`) | Count theo từng status và/hoặc breakdown khoảng ngày; bao gồm trạng thái theo filter, không gọi đây là doanh thu. |
| FR48 / UC-A15 / BR13–14 | `Order.CompletedAt`; chỉ `COMPLETED` | `SUM(Order.TotalAmount)` đã chốt; query trực tiếp Orders, không JOIN OrderDetails để cộng tổng. `CompletedAt == null` bị loại. |
| FR49 / UC-A16 | `Order.CompletedAt`; chỉ `COMPLETED` | `SUM(OrderDetail.Quantity)` group ProductId. Source snapshot thật: `ProductId`, `ProductName`, `Quantity`, `UnitPrice`, `DiscountAmount`, `TotalPrice`; không có SKU snapshot. Giá hiện hành không dùng để tính. Nếu cần tên lịch sử, dùng `OrderDetail.ProductName`; group key vẫn ProductId. |
| FR50 / UC-A17 | Threshold Admin nhập cho từng request, số nguyên >= 0 | Available = physical − reserved. Bằng 0 hết hàng; >0 đến threshold sắp hết; lớn hơn threshold bình thường. Inventory thiếu hiển thị riêng, không chuyển thành 0. |

### Tồn kho bất hợp lệ

Schema hiện có CHECK `[quantity_in_stock] >= 0 AND [reserved_quantity] >= 0 AND [reserved_quantity] <= [quantity_in_stock]`. Nếu phát hiện dữ liệu bất hợp lệ do bypass/constraint bị disable, phân loại thành anomaly riêng; không tính record đó vào bucket Available/low-stock và không clamp về 0. Hiển thị số anomaly cùng phạm vi tổng hợp và log product/inventory ID không kèm dữ liệu nhạy cảm. Report không sửa record; quy trình sửa kho vẫn theo Warehouse Phase 5.

### Date/time và Windows/.NET 10

- API nhận `DateOnly` start/end theo `Asia/Ho_Chi_Minh`; reject StartDate > EndDate. Tạo local start midnight và end-exclusive midnight của ngày kế tiếp, với `DateTimeKind.Unspecified`, rồi convert bằng timezone object sang UTC. Query `[startUtc, endExclusiveUtc)`.
- `Order.OrderDate` dùng FR47; `CompletedAt` dùng FR48/FR49. DateOnly giúp biên tháng/năm đi qua bằng lịch thay vì cộng giây; test ngay trước/sau nửa đêm Việt Nam và completedAt null.
- Không dùng `TimeZoneInfo.Local` hay `DateTime.ToLocalTime()` cho report vì timezone máy chủ không phải business timezone. Chuẩn IANA là `Asia/Ho_Chi_Minh`. Microsoft ghi nhận .NET trên Windows hỗ trợ IANA từ .NET 6 khi không bật NLS và globalization invariant; các API chuyển IANA/Windows ID cũng cần ICU. Resolver nên thử IANA trực tiếp, và trên Windows có fallback ID chuẩn `SE Asia Standard Time`; fail startup/health-check nếu không resolve được, không âm thầm dùng timezone local. Kiểm tra profile thực tế trên Windows CI và deployment image. Nguồn: [FindSystemTimeZoneById](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.findsystemtimezonebyid), [Globalization and ICU](https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu), [IANA to Windows ID conversion](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.tryconvertianaidtowindowsid?view=net-10.0), [Microsoft timezone ID mapping](https://learn.microsoft.com/en-us/sql/linux/configure/time-zone?view=sql-server-ver15).
- UI format timestamp/report labels bằng zone object Việt Nam, không hiển thị UTC raw là giờ Việt Nam. Biên UTC tính lại từ local midnight, không cố định offset `+07:00` ở code.

## 9. FR45 — price update hiện có và audit gap

FR45 cơ bản hiện **được đáp ứng trong source**: Admin `ProductsController` cho sửa `Price`/`DiscountPrice`; `ProductManagementService.SaveAsync` xác thực và cập nhật giá hiện hành. Khi tạo đơn, `OrderDetail` lưu `UnitPrice`, `DiscountAmount`, `TotalPrice` cùng `ProductName`, `ProductId`, `Quantity`; sửa `Product.Price` không viết lại snapshot đã đặt (BR12). Các test catalog/commerce hiện có bảo vệ snapshot.

FR45 vẫn giữ trạng thái **PARTIAL** trong audit vì NFR10 yêu cầu truy vết hành động giá: hiện service không ghi structured log cho old/new price/actor/result; controller `ILogger` hiện phục vụ lỗi ảnh, không phải audit giá. Không có price-history persistence. Không thêm `product_price_histories`; Phase 6.5 sẽ đưa việc xác định và xin duyệt phạm vi structured operational log cho price change vào review. Chỉ code sau khi scope được duyệt; nếu cần persistent history thì ngoài phạm vi này và phải xin duyệt schema riêng.

## 10. Database/migration safety

- Phase 6.0 không đổi schema. 6.2, 6.3 moderation, 6.4 reports được thiết kế dùng 22 bảng hiện có; không thêm review history, moderation audit, threshold hoặc price history table.
- 6.1 invitation tạm thiết kế với thuộc tính Identity hiện có; nếu phát hiện điều kiện bắt buộc không thể biểu diễn/kiểm chứng thì dừng xin duyệt, không tự thêm cột.
- Không sửa migration Phase 1–5, không tạo/apply migration, không truy cập database chính/QA trong Phase 6.0. Mọi yêu cầu index/schema mới cần proposal, pre-production review và duyệt database đích riêng.
- Giữ nguyên state machine bảy trạng thái Phase 5 và cancellation request riêng; báo cáo chỉ đọc. Inventory checkout RESERVE; cancellation RELEASE một lần; export giảm physical/reserved + OUT cùng transaction; receipt/adjustment IN/ADJUST. Không thay đổi luồng đó.

## 11. Kiểm thử dự kiến

- **Unit:** single-role policy; cấm Customer→staff; chỉ Sales↔Warehouse; last active Admin/self-lock/self-demotion; invitation pending/activated/token expiry/replay; review eligibility/status transition/unknown status; date boundary; threshold buckets/anomaly; order/revenue/product aggregate definitions.
- **HTTP:** anonymous/Customer/Sales/Warehouse bị chặn Admin; Admin không thao tác customer review thay owner; favorites/review ownership; antiforgery; role stale cookie; inactive cookie rejected on next request; invitation create/resend/send-failure; chỉ APPROVED public; status unknown fail-closed.
- **SQL Server integration (QA DB đã xác minh):** account+role atomicity, exactly one role dưới race, last Admin concurrent operations, lock active session, invitation duplicate/resend/commit ambiguity; unique favorite/review races; owner/completed/detail composite keys; report excludes all non-completed statuses, uses correct timestamp, no multiplication from joins, timezone ranges, null completedAt, threshold and missing/invalid inventory.
- **Manual UAT:** Chrome/Edge với Customer, Admin, Sales, Warehouse theo đúng role scope; invitation phải chứng minh email thực tế tới mailbox và link hết hạn/không dùng lại. Không đánh dấu pass chỉ từ mock email hoặc HTTP test.
- **Regression:** full Phase 1–5 suite trên QA; bảo vệ checkout, cancellation, reservation, export, IN/ADJUST, promotions, snapshot và permissions.

## 12. Rủi ro được chấp nhận / policy-vs-technical status

**Đã chấp nhận trong business policy:** moderation chỉ có ILogger, không có persistent audit/history; giá không có lịch sử bền vững; email provider phải thực sự hoạt động để nghiệm thu invitation; Admin chỉ read-only tồn; không có quyền Admin grant từ UI; Customer không chuyển thành staff; mỗi user một role.

**Còn là đề xuất kỹ thuật cần chứng minh:** per-request active-account validation và độ trễ; atomic role swap/last-admin serialization với SQL retry; invitation token purpose/lifetime/replay; cách map IANA timezone khi Windows chạy NLS/invariant; hiệu năng report/paging trên QA-size data. Các mục này không làm thay đổi policy; nếu implementation cho thấy cần schema hoặc thay policy thì dừng xin duyệt.

**Cập nhật POC/triển khai 6.1 (2026-10-22):** QA chứng minh các thuộc tính Identity hiện có (`IsActive`, `EmailConfirmed`, `PasswordHash`, `SecurityStamp`, role mapping) đủ biểu diễn pending invitation và suspended account; không cần thêm cột/bảng. Automated regression đạt 239/239, bao gồm race/replay/session cases; chi tiết và giới hạn manual/email UAT ở [`PHASE6_1_ACCEPTANCE.md`](./PHASE6_1_ACCEPTANCE.md). Không có schema change/migration.

**Trạng thái tổng:** 9/9 business decisions APPROVED; Phase 6.1 acceptance BLOCKED; 6.2–6.4 IMPLEMENTED – BUILD VERIFIED, tests and manual UAT NOT RUN; 6.5 PREPARED, NOT ACCEPTED; Phase 6 production NOT STARTED. Implementation evidence is recorded in `PHASE6_2_ACCEPTANCE.md` through `PHASE6_5_HARDENING_PLAN.md`.
