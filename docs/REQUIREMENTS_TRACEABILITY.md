# Ma trận truy vết yêu cầu NEXORA

## 1. Mục đích và căn cứ

Ma trận này nối các yêu cầu chức năng Chương 2 với module triển khai và Use Case được liệt kê trong khóa luận. Nguồn là tài liệu [`NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx`](./NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx), trọng tâm mục 2.2–2.4 và 2.9–2.10.

Chương 2 có 50 FR (FR01–FR50), 10 NFR (NFR01–NFR10), 19 BR (BR01–BR19), bốn tác nhân và 46 Use Case. Tên FR, BR và Use Case dưới đây được rút từ tài liệu. “Module” là nhóm trách nhiệm kiến trúc đề xuất trong `IMPLEMENTATION_PLAN.md`, không phải module đã tồn tại trong source code.

## 2. Vai trò và phạm vi Use Case

| Vai trò | Actor trong tài liệu | Dải Use Case | Số UC |
|---|---|---:|---:|
| Khách hàng | AC01 | UC-C01–UC-C15 | 15 |
| Nhân viên bán hàng | AC02 | UC-S01–UC-S07 | 7 |
| Nhân viên kho | AC03 | UC-W01–UC-W07 | 7 |
| Quản trị viên | AC04 | UC-A01–UC-A17 | 17 |
| **Tổng** | **4 tác nhân** |  | **46** |

## 3. Truy vết 50 yêu cầu chức năng

### 3.1. Khách hàng – FR01–FR17

| FR | Chức năng theo tài liệu | Module đề xuất | Use Case |
|---|---|---|---|
| FR01 | Đăng ký tài khoản | Identity & Access | UC-C01 – Đăng ký tài khoản |
| FR02 | Đăng nhập | Identity & Access | UC-C02 – Đăng nhập |
| FR03 | Khôi phục mật khẩu | Identity & Access | UC-C03 – Khôi phục mật khẩu |
| FR04 | Quản lý thông tin cá nhân | Customer Account | UC-C04 – Quản lý thông tin cá nhân |
| FR05 | Tìm kiếm sản phẩm | Catalog | UC-C05 – Tìm kiếm sản phẩm |
| FR06 | Xem sản phẩm/danh mục/chi tiết | Catalog | UC-C06 – Xem sản phẩm |
| FR07 | Lọc theo giá, thương hiệu, thông số | Catalog & Specification | UC-C07 – Lọc sản phẩm theo thông số kỹ thuật |
| FR08 | Nhập mô tả nhu cầu bằng tiếng Việt và làm rõ trường thiếu | Natural Language Consultation | UC-C08 – Tư vấn lựa chọn sản phẩm |
| FR09 | Lọc ràng buộc cứng, chấm điểm và xếp hạng gợi ý | Natural Language Consultation | UC-C08 – Tư vấn lựa chọn sản phẩm |
| FR10 | Hiển thị lý do gợi ý dựa trên dữ liệu; cho chỉnh yêu cầu | Natural Language Consultation | UC-C08 – Tư vấn lựa chọn sản phẩm |
| FR11 | So sánh thông số, giá và đặc điểm | Product Comparison | UC-C09 – So sánh sản phẩm |
| FR12 | Thêm/xem/xóa yêu thích | Favorites | UC-C10 – Quản lý danh sách yêu thích |
| FR13 | Thêm/cập nhật/xóa giỏ hàng | Cart | UC-C11 – Quản lý giỏ hàng |
| FR14 | Tạo đơn từ giỏ hàng | Order Management | UC-C12 – Đặt hàng |
| FR15 | Xem lịch sử, chi tiết và trạng thái đơn | Order Management | UC-C13 – Theo dõi đơn hàng |
| FR16 | Hủy đơn khi đủ điều kiện | Order Management | UC-C14 – Hủy đơn hàng |
| FR17 | Đánh giá sản phẩm đã mua sau khi hoàn thành đơn | Reviews | UC-C15 – Đánh giá sản phẩm |

### 3.2. Nhân viên bán hàng – FR18–FR25

| FR | Chức năng theo tài liệu | Module đề xuất | Use Case |
|---|---|---|---|
| FR18 | Đăng nhập khu vực nhân viên bán hàng | Identity & Access | Chức năng đăng nhập dùng chung; danh mục UC không có UC đăng nhập riêng cho Sales |
| FR19 | Xem danh sách đơn hàng | Order Management | UC-S01 – Xem danh sách đơn hàng |
| FR20 | Tìm đơn theo mã/khách hàng/trạng thái | Order Management | UC-S02 – Tìm kiếm đơn hàng |
| FR21 | Xem thông tin khách hàng và sản phẩm trong đơn | Order Management | UC-S03 – Xem chi tiết đơn hàng |
| FR22 | Xác nhận đơn hợp lệ | Order Management | UC-S04 – Xác nhận đơn hàng |
| FR23 | Cập nhật trạng thái đơn | Order Management | UC-S05 – Cập nhật trạng thái đơn |
| FR24 | Kiểm tra và xử lý yêu cầu hủy | Order Management | UC-S06 – Xử lý yêu cầu hủy đơn |
| FR25 | Chuyển đơn đã xác nhận cho kho | Order Management / Warehouse Handoff | UC-S07 – Chuyển đơn sang kho |

### 3.3. Nhân viên kho – FR26–FR33

| FR | Chức năng theo tài liệu | Module đề xuất | Use Case |
|---|---|---|---|
| FR26 | Đăng nhập khu vực nhân viên kho | Identity & Access | Chức năng đăng nhập dùng chung; danh mục UC không có UC đăng nhập riêng cho Warehouse |
| FR27 | Theo dõi số lượng tồn | Inventory | UC-W01 – Xem tồn kho |
| FR28 | Tra cứu sản phẩm trong kho | Inventory | UC-W02 – Tìm kiếm hàng trong kho |
| FR29 | Cập nhật tồn kho khi có nghiệp vụ phù hợp | Inventory | UC-W03 – Điều chỉnh tồn kho có lý do |
| FR30 | Nhận đơn bán hàng đã xác nhận | Warehouse Fulfillment | UC-W04 – Tiếp nhận yêu cầu xuất kho |
| FR31 | Xác nhận lấy và chuẩn bị sản phẩm | Warehouse Fulfillment | UC-W05 – Chuẩn bị hàng |
| FR32 | Ghi nhận hàng xuất khỏi kho | Inventory & Warehouse Fulfillment | UC-W06 – Xác nhận xuất kho |
| FR33 | Xem lịch sử nhập/xuất/điều chỉnh | Inventory | UC-W07 – Xem lịch sử giao dịch kho |

### 3.4. Quản trị viên – FR34–FR50

| FR | Chức năng theo tài liệu | Module đề xuất | Use Case |
|---|---|---|---|
| FR34 | Xem/khóa/mở khóa tài khoản | Identity & Access | UC-A01 – Xem/khóa/mở tài khoản người dùng |
| FR35 | Tạo và quản lý tài khoản nhân viên | Identity & Access | UC-A02 – Tạo/quản lý tài khoản nhân viên |
| FR36 | Gán quyền Customer/Sales/Warehouse/Admin | Identity & Access | UC-A03 – Phân quyền người dùng |
| FR37 | Thêm/sửa/xóa danh mục | Catalog Administration | UC-A04 – Quản lý danh mục |
| FR38 | Quản lý thương hiệu | Catalog Administration | UC-A05 – Quản lý thương hiệu |
| FR39 | Thêm/sửa/ngừng kinh doanh sản phẩm | Catalog Administration | UC-A06 – Thêm/sửa/ngừng kinh doanh sản phẩm |
| FR40 | Quản lý hình ảnh sản phẩm | Catalog Administration | UC-A07 – Quản lý hình ảnh sản phẩm |
| FR41 | Cấu hình thông số theo danh mục | Specification Administration | UC-A08 – Quản lý thông số kỹ thuật |
| FR42 | Cấu hình nhu cầu, điều kiện và trọng số tư vấn | Consultation Administration | UC-A09 – Quản lý quy tắc tư vấn |
| FR43 | Tạo/cập nhật khuyến mại | Promotion Administration | UC-A10 – Quản lý khuyến mại |
| FR44 | Theo dõi/xử lý đánh giá vi phạm | Review Administration | UC-A11 – Quản lý đánh giá |
| FR45 | Điều chỉnh giá bán hiện hành | Catalog Administration | UC-A12 – Cập nhật giá sản phẩm |
| FR46 | Xem tình hình hoạt động tổng quan | Reporting | UC-A13 – Xem Dashboard |
| FR47 | Thống kê số lượng/trạng thái đơn | Reporting | UC-A14 – Thống kê đơn hàng |
| FR48 | Tổng hợp doanh thu theo thời gian | Reporting | UC-A15 – Thống kê doanh thu |
| FR49 | Theo dõi sản phẩm bán ra | Reporting | UC-A16 – Thống kê sản phẩm |
| FR50 | Theo dõi tồn và hàng sắp hết | Reporting & Inventory | UC-A17 – Thống kê tồn kho |

## 4. Truy vết 19 quy tắc nghiệp vụ

| BR | Quy tắc trong tài liệu | Module chính | Use Case/điểm kiểm thử liên quan |
|---|---|---|---|
| BR01 | Khách chỉ tự hủy khi đơn Chờ xác nhận | Order Management | UC-C14; từ chối đơn đã xác nhận/chuyển kho |
| BR02 | Chỉ đánh giá sản phẩm thuộc đơn Hoàn thành | Reviews | UC-C15; kiểm tra quyền mua và trạng thái đơn |
| BR03 | Số lượng đặt không vượt tồn có thể bán | Cart, Order & Inventory | UC-C12; kiểm tra lại ngay trước khi ghi đơn |
| BR04 | Chỉ đơn Đã xác nhận chuyển sang kho | Order Management / Warehouse Handoff | UC-S04, UC-S07, UC-W04 |
| BR05 | Xuất kho cập nhật tồn và ghi giao dịch trong một nghiệp vụ nhất quán | Inventory & Warehouse Fulfillment | UC-W06; kiểm tra rollback nếu một bước lỗi |
| BR06 | Bộ lọc thông số thay đổi theo danh mục | Catalog & Specification | UC-C07 |
| BR07 | So sánh ưu tiên cùng danh mục/cấu trúc thông số tương thích | Product Comparison | UC-C09 |
| BR08 | Mỗi vai trò chỉ truy cập chức năng được cấp | Identity & Access | Kiểm thử authorization toàn bộ Areas/endpoints |
| BR09 | Một sản phẩm chỉ xuất hiện một lần trong yêu thích của khách | Favorites | UC-C10; unique(user_id, product_id) |
| BR10 | Khuyến mại chỉ có hiệu lực đúng thời hạn và khi hoạt động | Promotions | UC-C12; kiểm thử biên thời gian và trạng thái |
| BR11 | Lưu đơn giá thời điểm mua trong `OrderDetail.UnitPrice` | Order Management | UC-C12; kiểm tra snapshot giá |
| BR12 | Đổi `Product.Price` không đổi giá đơn đã tạo | Catalog & Order | UC-A12 và UC-C12; kiểm tra dữ liệu đơn cũ |
| BR13 | Chỉ đơn Hoàn thành được ghi nhận doanh thu | Reporting | UC-A15; lọc trạng thái `COMPLETED` |
| BR14 | Doanh thu tính giá trị giao dịch sau giảm giá, không lấy giá hiện hành | Reporting & Order | UC-A15; dùng số tiền snapshot và `completed_at` |
| BR15 | Chuyển yêu cầu ngôn ngữ tự nhiên thành dữ liệu có cấu trúc trước khi chấm điểm | Natural Language Consultation | UC-C08; kiểm tra hồ sơ đã chuẩn hóa |
| BR16 | Thiếu loại sản phẩm/mục đích bắt buộc thì hỏi bổ sung | Natural Language Consultation | UC-C08, TC-NLP03 |
| BR17 | Loại sản phẩm không đạt điều kiện bắt buộc trước khi tính điểm | Natural Language Consultation | UC-C08, TC-NLP04 |
| BR18 | Xếp hạng sản phẩm còn lại theo tiêu chí và trọng số nhu cầu | Natural Language Consultation | UC-C08, TC-NLP05/07 |
| BR19 | Mỗi gợi ý cần có lý do giải thích | Natural Language Consultation | UC-C08, TC-NLP06; đối chiếu ProductSpecification |

## 5. Truy vết 10 yêu cầu phi chức năng

| NFR | Yêu cầu | Module/phạm vi xác minh | Tiêu chí tài liệu |
|---|---|---|---|
| NFR01 | Hiệu năng | Catalog, filter, consultation; ghi cấu hình thử | Tra cứu/lọc ≤ 3 giây; tư vấn ≤ 8 giây; ngưỡng dự kiến, cần đo trên bộ dữ liệu và môi trường được ghi rõ |
| NFR02 | Bảo mật | Identity & Access, toàn bộ module ghi dữ liệu | Mật khẩu được băm; vùng chức năng có kiểm soát truy cập |
| NFR03 | Phân quyền | Customer/Sales/Warehouse/Admin | Người dùng chỉ truy cập chức năng được cấp |
| NFR04 | Toàn vẹn dữ liệu | Order & Inventory transactions | Đơn hàng/tồn/giao dịch kho nhất quán |
| NFR05 | Khả dụng | Razor Views/Bootstrap | Dễ dùng trên máy tính và thiết bị di động |
| NFR06 | Khả năng mở rộng | Catalog & Specification | Thêm danh mục/thông số không cần thay cấu trúc cố định của Product |
| NFR07 | Khả năng bảo trì | Consultation, Order, Inventory | Tách thành module/service có trách nhiệm rõ |
| NFR08 | Khả năng giải thích | Natural Language Consultation | Mỗi gợi ý có lý do dựa trên thông số/ràng buộc thực |
| NFR09 | Tính ổn định tư vấn | Parser, rule engine, scorer | Cùng đầu vào chuẩn hóa/dữ liệu/quy tắc cho cùng kết quả; thông tin không xác định phải hỏi lại/báo |
| NFR10 | Ghi log | Cập nhật giá, xuất kho, thay đổi quyền | Thao tác quan trọng được ghi nhận |

## 6. Ma trận dữ liệu và module

Danh sách 22 bảng chính thức từ mục 2.9.2 được liên kết với module sở hữu chính như sau:

| Module | Bảng chính thức |
|---|---|
| Identity & Access | `roles`, `users`, `user_roles` |
| Catalog Administration / Catalog | `categories`, `brands`, `products`, `product_images` |
| Specification | `specifications`, `category_specifications`, `product_specifications` |
| Cart & Order | `carts`, `cart_items`, `orders`, `order_details` |
| Inventory | `inventories`, `inventory_transactions` |
| Favorites & Reviews | `favorites`, `reviews` |
| Promotions | `promotions`, `promotion_products` |
| Natural Language Consultation | `usage_needs`, `recommendation_rules` |

`orders.completed_at` là trường bắt buộc thuộc `orders`. `product_price_histories` là bảng mở rộng tùy chọn, không thuộc danh sách chính thức 22 bảng. Nếu thiết kế triển khai khác tài liệu, cần ghi nhận thay đổi trước khi áp dụng schema.

## 7. Truy vết Use Case trọng tâm và bộ nghiệm thu tư vấn

| Use Case | FR/BR/NFR liên quan | Bằng chứng/điểm kiểm thử cần có |
|---|---|---|
| UC-C08 – Tư vấn lựa chọn sản phẩm | FR08–FR10; BR15–BR19; NFR01, NFR08, NFR09 | TC-NLP01–TC-NLP09; hồ sơ trích xuất; tập ứng viên sau lọc cứng; điểm/xếp hạng; lý do đối chiếu thông số thật; thời gian; cấu hình dữ liệu và quy tắc |
| UC-C12 – Đặt hàng | FR14; BR03, BR11, BR12 | Kiểm tra tồn/giá lần cuối; Order và OrderDetail nguyên tử; snapshot đơn giá/giảm giá/tổng tiền; các nhánh lỗi rollback |
| UC-C14 – Hủy đơn | FR16; BR01 | Đúng chủ sở hữu và trạng thái; từ chối tự hủy khi đã xác nhận |
| UC-S04 – Xác nhận đơn | FR22; BR04 | Chỉ xác nhận đơn hợp lệ; chuyển bước tiếp theo cho kho đúng trạng thái |
| UC-W06 – Xác nhận xuất kho | FR32; BR05; NFR04 | Tồn giảm, bản ghi OUT và trạng thái đơn cùng commit hoặc cùng rollback |
| UC-A12 – Cập nhật giá | FR45; BR11–BR12; NFR10 | Giá hợp lệ, chỉ admin, log thao tác; đơn cũ giữ nguyên giá đã chốt |

Bộ kiểm thử tư vấn do khóa luận đề xuất gồm:

| Ca | Mục tiêu |
|---|---|
| TC-NLP01 | Trích xuất laptop, ngân sách, Programming, ưu tiên RAM/pin từ một câu đầy đủ |
| TC-NLP02 | Chuẩn hóa cách viết tương đương như “20tr” và “20 triệu” |
| TC-NLP03 | Hỏi bổ sung khi mô tả thiếu loại sản phẩm/thông tin bắt buộc; không tự tạo ngân sách |
| TC-NLP04 | Không có kết quả thì giữ nguyên điều kiện cứng và báo rõ |
| TC-NLP05 | Cùng hồ sơ/dữ liệu/quy tắc cho cùng thứ hạng |
| TC-NLP06 | Lý do gợi ý khớp giá trị thông số; không bịa thông tin |
| TC-NLP07 | Đổi trọng số chỉ ảnh hưởng xếp hạng theo rule; không sửa giá và đơn cũ |
| TC-NLP08 | Đầu vào rỗng/vô nghĩa/parser lỗi cho phép nhập lại hoặc dùng bộ lọc, không sinh gợi ý giả |
| TC-NLP09 | Câu thuộc phạm vi danh mục thử đạt ngưỡng thời gian NFR01 trong môi trường được ghi |

Các ca trên là kế hoạch chưa chạy. Chỉ ghi nhận pass/fail sau khi triển khai và lưu kết quả thực tế.

## 8. Ghi chú về phạm vi Use Case

- Danh mục Use Case trong tài liệu có 46 UC; mã được dùng xuyên suốt đặc tả và sơ đồ.
- FR18 và FR26 yêu cầu nhân viên đăng nhập nhưng danh mục UC không có UC đăng nhập riêng cho Sales/Warehouse. Ma trận giữ nguyên danh mục gốc và liên kết hai FR với module xác thực dùng chung, không tạo mã UC mới.
- FR06 được UC-C06 bao phủ cho danh sách/danh mục/chi tiết; FR15 được UC-C13 bao phủ cho lịch sử/chi tiết/trạng thái; FR08–FR10 được UC-C08 bao phủ theo giải thích trong mục 2.3.
- Không suy ra số lượng Use Case phải bằng số lượng FR.

