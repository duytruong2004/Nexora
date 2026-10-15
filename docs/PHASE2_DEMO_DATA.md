# Dữ liệu demo Phase 2

## Nguyên tắc

- Seed chỉ chạy khi `ASPNETCORE_ENVIRONMENT=Development`, sau khi schema đã có sẵn; ứng dụng không gọi `EnsureCreated`, `Migrate` hoặc tạo database.
- Dữ liệu seed có tính bổ sung và idempotent: so khớp Category gốc theo tên, Brand theo tên, Specification theo tên/kiểu, CategorySpecification theo cặp khóa, Product theo SKU và ProductSpecification theo cặp sản phẩm/thông số. Bản ghi khớp không bị cập nhật hoặc xóa.
- Sản phẩm và thương hiệu đều mang nhãn `NEXORA Demo`/`NEXORA Lab`; mô tả ghi rõ cấu hình là dữ liệu mô phỏng, không phải thông số hãng hay cam kết thương mại.
- Seed không tạo tài khoản, không gán role, không sửa orders/customers/inventory, không tạo ProductImage và không thay schema.

## Phạm vi có nguồn trong khóa luận

Mười Category gốc được chép theo phạm vi Chương 1: Điện thoại, Laptop, Máy tính bảng, Màn hình, Bàn phím, Chuột, Tai nghe, Đồng hồ thông minh, Máy ảnh, Phụ kiện công nghệ.

Khóa luận nêu phạm vi dữ liệu chi tiết tập trung vào Điện thoại, Laptop, Máy tính bảng; ví dụ Specification cho Laptop (CPU, RAM, SSD, GPU, trọng lượng, màn hình), Điện thoại/Máy tính bảng (chipset, RAM, bộ nhớ, kích thước màn hình, pin), và Phụ kiện (chuẩn kết nối, công suất). Vì vậy dữ liệu kỹ thuật demo chỉ được thêm cho bốn nhóm có ví dụ trực tiếp này; sáu nhóm còn lại có Category nhưng chưa tự suy đoán schema thuộc tính hay sản phẩm.

## Nội dung mô phỏng

- 2 Brand demo.
- 11 Specification động.
- 18 gán Category–Specification; bật lọc, không mặc định bắt buộc để quản trị viên có thể nhập dần dữ liệu.
- 11 Product mô phỏng (3 laptop, 3 điện thoại, 3 máy tính bảng, 2 phụ kiện) và tối đa 52 giá trị Specification.
- Không có ảnh demo; giao diện có empty state và quản trị viên có thể tải ảnh thật qua trang sản phẩm.

Khởi động website ở Development sẽ bổ sung các hàng còn thiếu vào database NEXORA hiện có. Chạy lại không nhân bản bản ghi. Không cần và không được chạy migration cho seed này.
