# NEXORA – Phase 5.2 Schema Change Review

**Trạng thái:** Đã duyệt; migration được tạo và áp dụng chỉ trên database QA ngày 2026-10-20. Không truy cập hoặc thay đổi database Development NEXORA chính trong Phase 5.2.

## Mục tiêu và phạm vi

Lưu tối đa một yêu cầu hủy sau xác nhận cho mỗi Order, không tạo bảng nghiệp vụ mới và không thêm trạng thái mới vào orders.status. Order giữ CONFIRMED hoặc PREPARING trong lúc yêu cầu ở PENDING. Tổng mô hình vẫn có đúng 22 bảng nghiệp vụ; sau migration QA có 27 bảng vật lý gồm 22 bảng nghiệp vụ, bốn bảng Identity và __EFMigrationsHistory.

## Migration

20261019174632_AddOrderCancellationRequestFields

Các thao tác schema:

| Cột orders | SQL Server | Null | Mục đích |
|---|---|---:|---|
| cancel_request_status | nvarchar(20) | Có | PENDING, APPROVED, REJECTED |
| cancel_request_reason | nvarchar(1000) | Có | Lý do do Customer gửi |
| cancel_request_id | uniqueidentifier | Có | Khóa idempotency do client tạo |
| cancel_requested_at | datetime2 | Có | Thời điểm UTC gửi yêu cầu |
| cancel_reviewed_by | int | Có | FK tới users.id |
| cancel_reviewed_at | datetime2 | Có | Thời điểm UTC duyệt/từ chối |
| cancel_decision_note | nvarchar(1000) | Có | Ghi chú quyết định; bắt buộc khi từ chối |

Ràng buộc/index:

- ck_orders_cancel_request_consistent: cho phép toàn bộ trường request cùng NULL để giữ tương thích đơn cũ; nếu có request thì status chỉ nhận ba giá trị hợp lệ, reason/request ID/request time phải có; PENDING không có reviewer/time/note; APPROVED phải có reviewer/time và Order đã CANCELLED; REJECTED phải có reviewer/time và decision note không rỗng.
- FK_orders_users_cancel_reviewed_by tới users.id, ON DELETE NO ACTION.
- ux_orders_cancel_request_id: unique filtered index trên cancel_request_id IS NOT NULL.
- ix_orders_cancel_pending_review: filtered index trên cancel_request_status = 'PENDING', gồm thời gian yêu cầu và Order ID.
- IX_orders_cancel_reviewed_by: index hỗ trợ FK do EF Core tạo.

Migration chỉ thêm cột/ràng buộc/index và ghi migration history. Không thêm/xóa bảng, không cập nhật dữ liệu Order, không sửa inventory_transactions, completed_at, bảy giá trị Order status hoặc dữ liệu nghiệp vụ. Bảy cột đều nullable nên các Order cũ tiếp tục có giá trị NULL.

## Xác minh QA sau khi áp dụng

Đích duy nhất: PC01\SQLEXPRESS / NEXORA_IntegrationTests, database ONLINE.

- Migration history có 20261015173046_InitialFoundation, 20261016133620_AddCheckoutRequestId và 20261019174632_AddOrderCancellationRequestFields.
- Có đúng 27 bảng vật lý: 22 nghiệp vụ, bốn Identity và history; không có bảng nghiệp vụ mới.
- Bảy cột nullable có đúng kiểu/độ dài phía trên; CHECK, FK NO ACTION, unique filtered index, pending index và index FK đều tồn tại.
- Hai Order QA có trước migration vẫn còn; cả hai có trường request NULL.
- Sau toàn bộ SQL integration test, truy vấn chỉ đọc xác nhận không còn Order/Product/User mang marker fixture Phase 5.2; tổng Order vẫn là 2.
- Database NEXORA chính không được truy vấn, migration hoặc cập nhật trong lượt triển khai này.

File [PHASE5_2_CANCELLATION_REQUEST_PREVIEW.sql](./PHASE5_2_CANCELLATION_REQUEST_PREVIEW.sql) là bản preview phục vụ review. **Không chạy trực tiếp file này**; thay đổi đã được áp dụng bằng EF Core migration tới đúng QA database sau xác minh mục tiêu.

## Hợp đồng nghiệp vụ và giới hạn

- Customer có thể gửi một yêu cầu khi Order thuộc chính họ và ở CONFIRMED/PREPARING. Request ID lặp cho cùng Order được xử lý idempotent; request ID khác bị từ chối sau khi slot đã được dùng.
- Yêu cầu đang chờ không thay Order status, reservation hay tồn vật lý. Nó chặn xuất theo policy, nhưng endpoint/export transaction thực tế chưa được triển khai.
- Sales/Admin có thể duyệt hoặc từ chối. Atomic compare-and-swap chỉ cho một reviewer thắng. Duyệt chuyển Order sang CANCELLED, lưu audit fields, release reservation và ghi RELEASE cùng transaction; từ chối giữ nguyên Order/inventory và ghi reviewer/time/note.
- Warehouse không có quyền review. Không thay đổi Identity roles.
- SQL CHECK/FK/index giữ tính nhất quán cấu trúc nhưng không cung cấp lịch sử bất biến nếu có privileged SQL client xóa/sửa dữ liệu. Không có bảng lịch sử vì giữ nguyên 22 bảng nghiệp vụ.
- Hai reviewer race và rollback release được SQL integration test. Race thực tế giữa request/approval và warehouse export chưa thể kiểm thử vì Phase 5.3 export chưa có. Khi triển khai export, CAS phải cùng transaction/order row và điều kiện cancel_request_status IS NULL OR cancel_request_status <> 'PENDING'.

## Rollback và vận hành

Migration Down chỉ xóa FK, index, CHECK constraint và bảy cột mới. Không chạy rollback tự động; với QA data đã có request mới, rollback sẽ làm mất chính các trường cancellation nên phải cân nhắc trước. Không có rollback hoặc thao tác dữ liệu nào được thực hiện trong lượt này. Development NEXORA cần một lần review và phê duyệt riêng trước khi áp dụng migration.