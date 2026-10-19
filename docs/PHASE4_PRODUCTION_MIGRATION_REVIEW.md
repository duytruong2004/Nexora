# Phase 4 – Production Migration Review

**Ngày rà soát:** 16/10/2026<br>
**Migration:** `20261016133620_AddCheckoutRequestId`<br>
**Trạng thái:** review và triển khai đã hoàn tất theo xác nhận của chủ dự án. Migration chỉ được áp dụng sau backup đã verify và preflight thành công; không chạy script SQL trực tiếp.

## 1. Kết luận tóm tắt

Migration là thay đổi cộng thêm, phạm vi nhỏ và không chứa lệnh sửa/xóa dữ liệu đơn hàng. Trước khi áp dụng, database `NEXORA` ONLINE trên `PC01\SQLEXPRESS`, có 0 đơn hàng và chỉ ghi nhận migration `20261015173046_InitialFoundation`. EF Core báo model khớp migration cuối hiện có trong source. Sau triển khai, hậu kiểm xác nhận schema và dữ liệu như dự kiến.

**Đánh giá:** thay đổi schema đã áp dụng an toàn sau khi có xác nhận riêng, backup được SQL Server verify và preflight chính xác. Không có lệnh rollback nào được chạy. Nghiệm thu browser sau đăng nhập của Phase 4 vẫn là việc riêng còn chờ.

## 2. Database đích và kiểm tra chỉ đọc

| Thuộc tính | Kết quả |
|---|---|
| SQL Server | `PC01\SQLEXPRESS` |
| Phiên bản/edition | `17.0.1000.7`, Express Edition (64-bit) |
| Database | `NEXORA`, trạng thái `ONLINE` |
| Compatibility level | 170 |
| Recovery model | SIMPLE |
| Cấu hình Web Development | `appsettings.Development.json` dùng `Database=NEXORA`, Windows Integrated Security và `Server=.\SQLEXPRESS`; máy hiện tại là `PC01`, không có process override `ConnectionStrings__DefaultConnection`; launch profile đặt `ASPNETCORE_ENVIRONMENT=Development` |
| Số đơn hiện tại | 0 |
| Migration history trước update | `20261015173046_InitialFoundation` (`ProductVersion` `10.0.11`) |
| Migration history sau update | InitialFoundation và `20261016133620_AddCheckoutRequestId` (`ProductVersion` `10.0.11`) |
| Bảng `orders` sau update | `completed_at` nullable `datetime2`; `checkout_request_id` nullable `uniqueidentifier` (16 byte) |
| Index idempotency sau update | `ux_orders_checkout_request_id`, unique nonclustered, filter `[checkout_request_id] IS NOT NULL` |
| Tổng bảng vật lý | 27 trước và sau update |

Preflight và hậu kiểm trên database chính dùng `SELECT`/đọc catalog SQL Server. Migration chạy đúng một lần bằng EF Core. Không tạo database, không chạy test ghi/xóa trên `NEXORA`, không thêm đơn hoặc thay đổi tồn.

## 3. Phạm vi chính xác của migration

`Up()` trong [`20261016133620_AddCheckoutRequestId.cs`](../src/NEXORA.Infrastructure/Persistence/Migrations/20261016133620_AddCheckoutRequestId.cs) chỉ thực hiện hai thay đổi cấu trúc:

1. Thêm `orders.checkout_request_id` kiểu SQL Server `uniqueidentifier`, nullable. Đây là UUID 16 byte; không có giới hạn độ dài dạng chuỗi.
2. Tạo unique index `ux_orders_checkout_request_id` trên cột trên, với filter `[checkout_request_id] IS NOT NULL`. Đây là nonclustered index mặc định của SQL Server.

Các hàng `orders` hiện có sẽ nhận `NULL` cho cột mới. Filter loại trừ mọi `NULL`, nên các hàng cũ không xung đột với unique constraint; mỗi request ID khác `NULL` chỉ được xuất hiện một lần. Migration không thêm FK, CHECK, bảng, default, trigger hoặc cột khác; không có `UPDATE`, `DELETE`, `TRUNCATE` hay thao tác ghi lên Order/OrderDetail. `Down()` sẽ drop index rồi drop cột, vì vậy không dùng rollback sau khi ứng dụng đã ghi request ID nếu chưa chấp nhận mất các khóa idempotency này.

Đối chiếu được thực hiện giữa migration `Up/Down`, EF model trong [`EntityConfigurations.cs`](../src/NEXORA.Infrastructure/Persistence/EntityConfigurations.cs), `AppDbContext`, migration Designer và [`AppDbContextModelSnapshot.cs`](../src/NEXORA.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs). Cấu hình EF xác nhận `Guid?`, unique index, đúng tên và đúng filter. `dotnet ef migrations has-pending-model-changes` trả về: “No changes have been made to the model since the last migration.”

## 4. SQL script xem trước — chỉ phục vụ review

Script do EF Core sinh từ `InitialFoundation` tới đúng `AddCheckoutRequestId` được lưu tại [`PHASE4_PRODUCTION_MIGRATION_PREVIEW.sql`](./PHASE4_PRODUCTION_MIGRATION_PREVIEW.sql). Đây là tài liệu review, không chạy trực tiếp. Chỉ áp dụng migration qua EF Core sau khi xác minh database đích, backup/preflight và có phê duyệt triển khai riêng. Nội dung migration tương ứng:

```sql
BEGIN TRANSACTION;
ALTER TABLE [orders] ADD [checkout_request_id] uniqueidentifier NULL;
CREATE UNIQUE INDEX [ux_orders_checkout_request_id]
    ON [orders] ([checkout_request_id])
    WHERE [checkout_request_id] IS NOT NULL;
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261016133620_AddCheckoutRequestId', N'10.0.11');
COMMIT;
```

Script preview không được chạy trực tiếp. Đây là migration script thông thường, không idempotent để chạy lặp trực tiếp; EF Core `database update` đã được dùng để đối chiếu `__EFMigrationsHistory` và áp dụng đúng migration mục tiêu. Script không bao gồm InitialFoundation và không chứa các migration ngoài mục tiêu.

## 5. Idempotency ở ứng dụng

- Checkout tạo `RequestId` dạng `Guid` khi dựng form và gửi nó bằng hidden field cùng antiforgery token.
- `CheckoutService` kiểm tra request ID đã có trong `orders`; nếu thuộc cùng user thì trả lại OrderId cũ, nếu thuộc user khác thì từ chối.
- Khi tạo đơn, service lưu `CheckoutRequestId` trên Order trong cùng transaction `SERIALIZABLE` với OrderDetail và reservation. Unique filtered index là hàng rào cuối chống lưu hai đơn cùng request ID.
- Khi gặp lỗi ghi hoặc kết quả commit chưa rõ, service truy vấn lại theo request ID và user; chỉ trả thành công khi xác minh được Order đã commit. Nếu chưa thể xác minh, kết quả là `CommitResultUnknown`; client có thể gửi lại cùng RequestId thay vì tạo request mới.
- SQL integration test trên `NEXORA_IntegrationTests` đã kiểm tra replay cùng request trả cùng OrderId, không tạo đơn trùng. Không chạy test này trên `NEXORA`.

## 6. Rủi ro và biện pháp

| Rủi ro | Đánh giá/biện pháp |
|---|---|
| Khóa schema trong lúc thêm cột/tạo index | Hai thao tác DDL cần khóa schema trên `orders`. Hiện bảng có 0 đơn, nên khối lượng xử lý dự kiến thấp, nhưng vẫn nên dừng ghi checkout trong cửa sổ triển khai và theo dõi thời gian khóa. |
| Migration thất bại giữa chừng/mất kết nối | Script EF dùng transaction quanh DDL và ghi history. Nếu lệnh lỗi, trước khi retry phải kiểm tra history, cột và index bằng truy vấn chỉ đọc; không chạy script lần hai theo phỏng đoán. Nếu transaction đã rollback, schema/history phải còn trạng thái ban đầu. Nếu trạng thái không nhất quán, dừng và xử lý thủ công có review. |
| Ứng dụng mới chạy trước migration | Code truy vấn/lưu cột mới sẽ lỗi `Invalid column name`. Thứ tự triển khai: backup → đưa ứng dụng vào maintenance/dừng checkout → xác minh target/history → áp dụng đúng migration → xác minh cột/index/history → chạy smoke read-only → bật lại phiên bản ứng dụng tương thích. |
| Rollback sau khi đã có checkout | `Down()` xóa index và cột, làm mất request ID đã lưu. Không rollback sau khi ứng dụng đã nhận checkout; ưu tiên sửa tiến về phía trước. Chỉ restore backup khi đã đánh giá tác động và dừng ghi. |
| Khả năng khôi phục | Database dùng recovery model SIMPLE; không dựa vào point-in-time log restore. Full COPY_ONLY backup có CHECKSUM đã tạo và `RESTORE VERIFYONLY` thành công trước khi cập nhật. |
| Đích sai hoặc migration ngoài phạm vi | Trước triển khai, xác minh lại máy `PC01`, instance `SQLEXPRESS`, database `NEXORA`, migration hiện tại là InitialFoundation và migration mục tiêu đúng `20261016133620_AddCheckoutRequestId`. Hủy nếu bất kỳ điều kiện nào sai. |

## 7. Lệnh đã sử dụng sau khi có xác nhận riêng

Lệnh đã chạy đúng một lần, với `ASPNETCORE_ENVIRONMENT=Development` và connection string process-scoped đã xác minh tới `PC01\SQLEXPRESS / NEXORA`:

```powershell
dotnet ef database update 20261016133620_AddCheckoutRequestId `
  --project src/NEXORA.Infrastructure `
  --startup-project src/NEXORA.Web `
  --context AppDbContext
```

EF Core báo `Applying migration '20261016133620_AddCheckoutRequestId'. Done.` Hậu kiểm xác minh migration mới được ghi, cột nullable/index/filter đúng, 27 bảng, `orders` vẫn 0 dòng và các số lượng nghiệp vụ không đổi. Không chạy script SQL riêng song song với lệnh EF.

## 8. Nhật ký backup

- **Vị trí lưu:** thư mục backup do SQL Server quản lý; tên file minh họa tổng quát là `NEXORA_Pre_AddCheckoutRequestId_<timestamp>.bak`. Đường dẫn tuyệt đối đặc thù của máy đã được lược bỏ khỏi tài liệu.
- **Kiểu:** full `COPY_ONLY` backup với `CHECKSUM`; SQL Server báo xử lý 929 pages.
- **Kết quả:** `RESTORE VERIFYONLY ... WITH CHECKSUM` trả “The backup set on file 1 is valid.” `msdb` ghi nhận backup size 7,684,096 bytes.
- SQL Server đã tạo và tự verify thành công media tại thư mục backup cấu hình. Không đổi ACL.

## 9. Hậu kiểm và phạm vi còn chờ

- Database `NEXORA` ONLINE trên đúng instance; 27 bảng, 36 FK, 21 CHECK. History có InitialFoundation và AddCheckoutRequestId.
- Các số lượng trước/sau: users 2, products 11, specifications 11, product_specifications 52, usage_needs 8, recommendation_rules 86, roles 4, orders 0.
- `dotnet restore` thành công; `dotnet build` 0 warning/0 error; `dotnet test` **117 passed, 0 failed, 0 skipped**. SQL tests chỉ dùng `NEXORA_IntegrationTests`.
- HTTP smoke tại `http://127.0.0.1:5058`: Home, Catalog, Details, Compare, tư vấn, Login/Register, CSS/Bootstrap trả 200; Cart/Checkout/Orders/Admin Promotions chuyển Login khi anonymous. Temporary web process đã dừng.
- Không tạo đơn thử nghiệm, không đổi tồn, không rollback, không tạo migration mới, không commit/push và không bắt đầu Phase 5. Browser test sau đăng nhập và kiểm tra thao tác nghiệp vụ thực tế vẫn chờ chủ dự án.
