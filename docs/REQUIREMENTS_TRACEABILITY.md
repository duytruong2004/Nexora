# Ma trận truy vết yêu cầu NEXORA

## 1. Mục đích và căn cứ

Ma trận này nối các yêu cầu chức năng Chương 2 với module triển khai và Use Case được liệt kê trong khóa luận. Nguồn là tài liệu [`NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx`](./NEXORA_KHOA_LUAN_DOT_2_TU_VAN_NGON_NGU_TU_NHIEN_BAT_BUOC.docx), trọng tâm mục 2.2–2.4 và 2.9–2.10.

Chương 2 có 50 FR (FR01–FR50), 10 NFR (NFR01–NFR10), 19 BR (BR01–BR19), bốn tác nhân và 46 Use Case. Tên FR, BR và Use Case dưới đây được rút từ tài liệu. “Module” là nhóm trách nhiệm kiến trúc trong `IMPLEMENTATION_PLAN.md`; một phần đã có triển khai source code ở Phase 1–2, các phần còn lại là kế hoạch giai đoạn sau.

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

Trạng thái TC-NLP01–TC-NLP09 và phân loại unit, SQL Server integration, HTTP smoke, browser/manual sau lượt nghiệm thu được ghi tại [`PHASE3_ACCEPTANCE.md`](./PHASE3_ACCEPTANCE.md). Browser đã kiểm tra luồng tư vấn và một phần Admin; responsive ở 390×844 và phiên Customer thực tế vẫn chờ kiểm tra thủ công.

## 8. Bằng chứng triển khai và nghiệm thu Phase 2

| Yêu cầu/Use Case | Module/điểm triển khai | Bằng chứng Phase 2 | Trạng thái/giới hạn |
|---|---|---|---|
| FR37 / UC-A04 | Admin Categories, category management service | Browser create/read/edit; từ chối tên trùng; ngừng Category khi đang được Product dùng | Browser đã kiểm tra; dữ liệu test đã dọn |
| FR38 / UC-A05 | Admin Brands, brand management service | Browser create/read/edit; từ chối tên trùng; ngừng Brand khi đang được Product tham chiếu | Browser đã kiểm tra; dữ liệu test đã dọn |
| FR39 / FR45 / UC-A06 / UC-A12 | Admin Products, ProductManagementService | Browser create/read/edit/deactivate; validation giá; từ chối Product ACTIVE thiếu Specification bắt buộc | Browser đã kiểm tra; không đo hiệu năng |
| FR40 / UC-A07 | ProductImage upload/storage | Browser chấp nhận PNG hợp lệ, từ chối `.txt` và >5 MiB; thay ảnh chính giữ tối đa một ảnh `IsPrimary`; xóa ảnh qua UI | Browser đã kiểm tra; JPEG/WebP codec chưa kiểm tra riêng |
| FR41 / UC-A08 | Admin Specifications, CategorySpecification, ProductSpecification | Browser create/edit/deactivate; gán NUMBER/GB, flags; nhập/hiển thị giá trị; server giữ context khi required-value lỗi | Browser đã kiểm tra; wrong-category ID được bao phủ bởi Domain policy test |
| FR05–FR07 / UC-C05–UC-C07 | Customer Catalog, dynamic Specification facets | Browser search, category/brand/price/NUMBER filter, product details and no-result empty state; query lấy SQL data | Browser đã kiểm tra; NFR01 chưa đo |
| FR11 / UC-C09 / BR07 | Catalog comparison | Browser chọn hai checkbox cùng Category, nút submit bật, form GET tới `Compare?ids=1&ids=2`; bảng so sánh hiện đủ hai sản phẩm; 4 ID bị từ chối | Browser selection và kết quả đã kiểm tra |
| BR08 / NFR03 | Identity & Access / Admin authorization | Admin dashboard mở trong phiên Admin (DB xác nhận ID 2); Customer QA tạm ID 3 chỉ có role Customer bị từ chối `/Admin/Products`; anonymous `/Admin/Products` chuyển về login; controller test yêu cầu Admin | Browser authorization đã kiểm tra; Customer QA user đã dọn sau khi không phát sinh dữ liệu phụ thuộc |

Kiểm thử trên browser không thay thế unit tests hoặc HTTP smoke. Số lần chạy, tình trạng 64/64 tests, database counts, schema/migration và các giới hạn xác thực của Phase 2 được ghi tại [`PHASE2_ACCEPTANCE.md`](./PHASE2_ACCEPTANCE.md). Consultation được triển khai trong Phase 3; bằng chứng và giới hạn nghiệm thu được ghi tại [`PHASE3_ACCEPTANCE.md`](./PHASE3_ACCEPTANCE.md).

## 9. Truy vết triển khai Phase 3

| Yêu cầu | Module/điểm triển khai | Bằng chứng | Trạng thái/giới hạn |
|---|---|---|---|
| FR08, BR15–BR16 / UC-C08 | `VietnameseRequirementParser`, requirement profile, clarification flow | Unit/HTTP smoke và browser: câu đủ, câu mơ hồ/thiếu thông tin | Browser hỏi làm rõ khi thiếu; rule-based, chưa dùng NLP mô hình thống kê |
| FR09, BR17–BR18 / UC-C08 | `ConsultationOrchestrator`, SQL candidate source, `ConsultationScoring` | Unit/SQL integration; browser max budget, RAM/SSD hard constraint, no-result và thay weight | Candidate truy vấn SQL; không nới điều kiện cứng; rank thay đổi theo trọng số DB |
| FR10, BR19 / UC-C08 | Explanation/result view model và Razor view | Unit + browser đối chiếu giá trị, ngưỡng và thiếu Specification pin | Không suy ra thời lượng pin; độ bao phủ phụ thuộc catalog |
| TC-NLP01–02 | Parser/normalizer | Unit + HTTP smoke + browser cho `20tr`/`20 triệu` | Đã kiểm tra; xem acceptance report |
| TC-NLP03–04, BR16–17 | Clarification/hard filter | Unit + SQL integration + HTTP/browser | Đã kiểm tra; không có kết quả giữ nguyên điều kiện |
| TC-NLP05, NFR09 | Deterministic rank/tie-break | Unit test + 5 HTTP lần lặp | Đã kiểm tra trên dataset hiện tại |
| TC-NLP06, NFR08 | Explanation | Unit + browser: giá trị/ngưỡng thật và thiếu Specification pin cho Laptop | Đạt trong giới hạn dữ liệu; không suy ra thời lượng pin |
| TC-NLP07 | DB-backed weights/scoring | Unit, SQL integration và browser Admin trên sản phẩm demo; xếp hạng đổi sau sửa weight | Đã kiểm tra; test config tạm được dọn, giá không đổi |
| TC-NLP08 | Input validation/parser | Unit + HTTP smoke | Đã kiểm tra chuỗi rỗng/vô nghĩa; không sinh kết quả giả |
| TC-NLP09 / NFR01 | Web consultation endpoint | 5 request warmed, avg 15 ms/max 17 ms, 11 products | Đo cục bộ; không phải benchmark production/load |
| Admin quản lý `usage_needs`/`recommendation_rules` | Admin Consultation MVC/service | Browser Admin create/edit; rule validation, toggle/edit checkbox, đổi weight và kiểm tra rank; SQL CRUD integration; anonymous HTTP 302 | Customer session và nút “Bật” qua automation chưa xác minh dứt điểm; responsive còn manual |

## 10. Truy vết triển khai Phase 4

| Yêu cầu | Module | Trạng thái Phase 4 |
|---|---|---|
| FR13 / UC-C11 | Cart – `carts`, `cart_items`, `products`, `inventories` | Implemented; unit + SQL integration đạt thêm/cộng/update/remove, ownership, live stock/promotion; Catalog và Cart user-confirmed manual PASS (2026-10-19) |
| FR14 / UC-C12 | Checkout, Order Pricing, Inventory Reservation – `orders`, `order_details`, `inventories`, `inventory_transactions` | SQL Server integration đạt reservation transaction, concurrency, idempotent replay và snapshot; Checkout user-confirmed manual PASS (2026-10-19) |
| FR15 / UC-C13 | My Orders – `orders`, `order_details` | SQL ownership xác nhận chủ đơn đọc được list/detail; user khác nhận null/NotFound. Danh sách/chi tiết user-confirmed manual PASS (2026-10-19) |
| FR16 / UC-C14, BR01 | Order Cancellation, Inventory Release | SQL integration đạt chỉ owner hủy PENDING; hủy lặp bị từ chối và chỉ tạo một `RELEASE`. Hủy đơn user-confirmed manual PASS (2026-10-19) |
| BR03 | Inventory availability/concurrency | Rowversion + CHECK + serializable transaction/retry; SQL race test đạt một order và một `InsufficientStock`; Catalog/tồn user-confirmed manual PASS (2026-10-19) |
| BR10 | Promotion calculation | Mức giảm lớn nhất, không cộng dồn; fixed amount một lần/dòng. Unit + SQL pricing và Admin create/toggle đạt; Admin Promotions user-confirmed manual PASS (2026-10-19) |
| BR11–BR12 | Order snapshot/pricing | SQL snapshot/đổi giá regression đạt: `Product.Price` đổi nhưng `OrderDetail.UnitPrice` cũ được giữ; Checkout/đơn user-confirmed manual PASS (2026-10-19) |
| Idempotency | `orders.checkout_request_id` + `ux_orders_checkout_request_id` | Migration `20261016133620_AddCheckoutRequestId` áp dụng trên `NEXORA_IntegrationTests` và Development `NEXORA`; cùng request của owner trả lại OrderId, không tạo đơn trùng |

Không có bảng nghiệp vụ mới. `NEXORA` và `NEXORA_IntegrationTests` đều ghi nhận `20261015173046_InitialFoundation` cùng `20261016133620_AddCheckoutRequestId`; mỗi database giữ nguyên tổng 27 bảng vật lý (22 bảng chính thức, 4 bảng Identity và migration history). Migration thêm một nullable column và unique filtered index. EF Core xác nhận model không có pending drift. Read-only verification ngày 2026-10-19 xác nhận history, cột `uniqueidentifier NULL` và unique filtered index trên cả hai database. Chủ dự án xác nhận Manual UAT 6/6 PASS ngày 2026-10-19: Catalog/tồn, Cart, Checkout, Orders/hủy, Admin Promotions, responsive desktop/tablet/mobile. Đây là xác nhận của người dùng; Codex không chạy browser automation và không tạo ảnh kiểm thử. **Phase 4: COMPLETED.**

## 11. Ghi chú về phạm vi Use Case

- Danh mục Use Case trong tài liệu có 46 UC; mã được dùng xuyên suốt đặc tả và sơ đồ.
- FR18 và FR26 yêu cầu nhân viên đăng nhập nhưng danh mục UC không có UC đăng nhập riêng cho Sales/Warehouse. Ma trận giữ nguyên danh mục gốc và liên kết hai FR với module xác thực dùng chung, không tạo mã UC mới.
- FR06 được UC-C06 bao phủ cho danh sách/danh mục/chi tiết; FR15 được UC-C13 bao phủ cho lịch sử/chi tiết/trạng thái; FR08–FR10 được UC-C08 bao phủ theo giải thích trong mục 2.3.
- Không suy ra số lượng Use Case phải bằng số lượng FR.


## 12. Truy vết triển khai Phase 5.2 – yêu cầu hủy sau xác nhận

| Yêu cầu | Module / Use Case | Bằng chứng tự động | Trạng thái và giới hạn |
|---|---|---|---|
| FR16 / UC-C14 / BR01 | Customer Orders, CustomerOrderService, cancellation request fields on orders | SQL Server integration: chỉ chủ đơn; CONFIRMED/PREPARING eligibility; idempotent replay; một request tối đa; đơn/tồn/snapshot không đổi khi gửi request | Đạt trên NEXORA_IntegrationTests; pending request tách khỏi Order.status. PENDING direct-cancel flow của Phase 4 được giữ nguyên |
| FR24 / UC-S06 | Sales/Admin Orders, OrderCancellationReviewService | HTTP role/antiforgery tests; SQL race của reviewer; approval release đúng một lần; rejection không đổi tồn; rollback khi ghi ledger lỗi | Đạt qua automated tests. Không có browser UAT hoặc tài khoản Sales thật |
| NFR03 | Identity Authorization | HTTP test host xác nhận Customer/Warehouse bị từ chối review, Admin/Sales được phép qua endpoint theo role | Đạt qua HTTP integration; chưa manual browser test |
| NFR04 | Order / Inventory transaction | SQL Server tests xác minh request/handoff, reviewer, approval/release transaction, export/OUT, double-export, shared inventory, deadlock retry và rollback | Phase 5.2 + 5.3 automated SQL QA đạt trên NEXORA_IntegrationTests; chưa browser UAT |
| FR32 / UC-W06 / BR05 / NFR04 | Warehouse Fulfillment – `WarehouseFulfillmentService`, `Warehouse/Orders` | HTTP role/antiforgery test; SQL export success/replay, request-vs-export, approval-vs-export, same-order export race, shared-product multi-order race và ledger failure rollback | Automated QA đạt; chưa manual browser UAT bằng tài khoản Warehouse thật |
| FR30–FR31 / UC-W04–UC-W05 / BR04 | Warehouse queue and order preparation view | SQL queue filters PREPARING; HTTP test validates queue/detail and non-existent order 404; detail shows SKU, quantities, inventory/reservation and cancellation warning | Đạt trong automated HTTP/SQL; schema không lưu actor/time riêng cho tiếp nhận/chuẩn bị; không tạo trạng thái Order mới |
| NFR03 | Warehouse Authorization | HTTP host xác nhận Warehouse được phép GET/POST, Customer/Sales/Admin bị 403 và anonymous bị challenge; export POST yêu cầu antiforgery token | Automated HTTP test đạt; chưa browser UAT |

### Phase 5.3 – Warehouse fulfillment/export

| Yêu cầu | Module / Use Case | Bằng chứng tự động | Trạng thái và giới hạn |
|---|---|---|---|
| FR30 / UC-W04 / BR04 | Hàng đợi `PREPARING`, tìm theo Order/SKU/tên, phân trang | HTTP render với test principal; SQL service đọc dữ liệu QA | Automated test đạt; không có browser UAT |
| FR31 / UC-W05 | Chi tiết đơn và kiểm tra tồn/reservation phục vụ soạn hàng | SQL kiểm tra chi tiết, reservation ledger và điều kiện đủ hàng; view không hiển thị phone/email | Automated test đạt; schema không lưu người/thời điểm nhận hoặc chuẩn bị |
| FR32 / UC-W06 / BR05 | Export toàn đơn, `PREPARING → EXPORTED`, Inventory giảm tồn + reservation, ledger `OUT` | SQL Server transaction tests; forced ledger failure rollback; replay không ghi/trừ lần hai | Automated test đạt; không tuyên bố exactly-once ngoài CAS/transaction và xác minh status/ledger |
| BR01 / NFR04 | Request/approval cancellation tranh chấp export | Hai test race khởi động đồng thời qua barrier trên SQL Server thật; final state/status/inventory/ledger assertions | Automated race tests đạt trên QA; không dùng SQLite/InMemory |
| BR03 / NFR04 | Nhiều đơn dùng chung Inventory, không âm tồn, concurrency token | SQL race với hai Order cùng Product; rowversion + locked update + bounded deadlock retry | Automated test đạt; operation 1205 được retry tối đa 3 lần, sau verify; exhaustion báo conflict |

Phase 5.3 không tạo migration và không thay đổi schema. SQL QA hậu kiểm có 27 bảng vật lý và ba migration trước đó; không còn fixture mang marker Phase 5.3. Full solution: 187 passed, 0 failed, 0 skipped; phân loại kiểm thử và giới hạn browser được ghi tại [PHASE5_3_ACCEPTANCE.md](./PHASE5_3_ACCEPTANCE.md).

Migration 20261019174632_AddOrderCancellationRequestFields đã áp dụng chỉ trên PC01\SQLEXPRESS / NEXORA_IntegrationTests. Không thêm bảng nghiệp vụ; QA database có 27 bảng vật lý. Tổng test solution: 172 passed, 0 failed, 0 skipped; HTTP test host và SQL integration được phân biệt trong [PHASE5_2_ACCEPTANCE.md](./PHASE5_2_ACCEPTANCE.md). Browser/manual UAT chưa thực hiện.

### Phase 5.4 – Warehouse Inventory Management

| Yêu cầu | Module / Use Case | Bằng chứng tự động | Trạng thái và giới hạn |
|---|---|---|---|
| FR27 / UC-W01 / BR03 | `Warehouse/Inventory` list; physical, reserved, available; out-of-stock status | SQL Server query test với Inventory hiện hữu và Product thiếu Inventory; `Available = QuantityInStock - ReservedQuantity` | Đạt qua SQL/HTTP; record thiếu hiển thị rõ, không tự tạo |
| FR28 / UC-W02 | Tìm kiếm tên/SKU, lọc hết hàng, phân trang | Query projection/filter SQL Server; giới hạn page size server-side | Đạt qua SQL-backed service; browser UAT chưa thực hiện |
| FR29 / UC-W03 / BR03 | Nhập `IN`, điều chỉnh `ADJUST` tăng/giảm, có reference/reason | SQL transaction tests xác nhận Domain `Receive`/`AdjustStock`, tồn và ledger atomically; số lượng dương; giảm dưới reserved bị từ chối | Đạt qua SQL integration; adjustment direction mới lưu note canonical; schema giữ nguyên |
| FR33 / UC-W07 | Inventory transaction history | SQL query test loại giao dịch, sản phẩm, actor, note; legacy ADJUST không có hướng hiển thị “Không xác định” | Đạt qua service test; browser UAT chưa thực hiện |
| BR05 / NFR04 | Inventory update + IN/ADJUST ledger trong transaction | Forced ledger-insert SQL error; stock và ledger được xác minh rollback; IN/ADJUST và row lock dùng cùng SQL transaction | Đạt trên SQL QA; không kiểm thử ghi trên database Development chính |
| NFR03 | Warehouse writes; Admin read-only; Customer/Sales/anonymous denied | HTTP integration: Admin GET danh sách/history nhưng mutation GET/POST bị 403; Customer/Sales bị chặn; Warehouse forms render; antiforgery thiếu bị 400; POST hợp lệ lấy actor từ claim | Đạt qua HTTP test host; không phải browser/manual test |
| Idempotency / retry | Stable operation request ID trong ledger note, `sp_getapplock` theo transaction và marker lookup trước mutation | SQL Server race cùng token: một lần apply; same token payload khác bị từ chối; hai receipt/adjustment cùng SKU và adjustment-vs-checkout/export races | Đạt trong các đường application sử dụng applock; không exactly-once tuyệt đối vì không có unique constraint/schema operation ID; database unavailable sau ambiguous commit vẫn có thể trả CommitOutcomeUnknown |

Tại thời điểm kết thúc Phase 5.4 (2026-10-20), chưa thực hiện browser/manual UAT; đây là trạng thái lịch sử trước Phase 5.5. Final acceptance và bằng chứng UAT mới hơn được ghi ở mục Phase 5.5 bên dưới.

### Phase 5.5 – UAT evidence and QA access controls

| Requirement / use case | Route / module | Evidence in this turn | Status and limits |
|---|---|---|---|
| FR15–FR16 / UC-C13–UC-C14 | Customer Orders `/Orders` | SQL/HTTP ownership and cancellation tests; owner manual UAT C01 confirms post-confirmation request | Automated QA PASS; manual summary PASS (owner-confirmed) |
| FR21–FR24 / UC-S03–UC-S06 | Sales Orders `/Sales/Orders` | SQL workflow/CAS/concurrency tests; owner manual UAT S01/S02/S03 | Automated QA PASS; manual summary PASS (owner-confirmed) |
| FR27–FR33 / UC-W01–UC-W07 | Warehouse Orders/Inventory `/Warehouse/Orders`, `/Warehouse/Inventory`, `/Warehouse/Inventory/History` | SQL transaction/race tests and HTTP role/antiforgery tests; owner manual UAT W01/W02/W03 | Automated QA PASS; manual summary PASS (owner-confirmed) |
| FR24 / UC-A12 and NFR03 | Admin Orders/Inventory read-only access | Admin role is enforced server-side; automated HTTP tests reject inventory writes; owner manual UAT A01 | Automated QA PASS; manual summary PASS (owner-confirmed) |
| NFR03 / server authorization | Customer, Sales, Warehouse, Admin | Controller `[Authorize]` boundaries, HTTP test-host role checks, and owner manual UAT R01 | Automated QA PASS; cross-role summary PASS (owner-confirmed) |
| NFR04 / inventory/order consistency | Checkout, cancellation, review, export, receipt, adjustment | Final full solution: 209 passed, 0 failed, 0 skipped; SQL Server target verified as `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; postflight retained role counts and had no marked Phase 5 fixtures | PASS on QA; main `NEXORA` database not accessed/updated |
| NFR05 / responsive experience | Customer/staff Areas | Owner manual UAT R01 confirms responsive summary check | PASS at summary level only; no Codex browser automation or viewport-specific evidence claimed |

### Phase 5.5 owner-confirmed manual UAT summary (2026-10-21)

| Result | Case | Requirement / use-case coverage | Summary confirmed by project owner |
|---|---|---|---|
| PASS | S01 | Sales authentication and order list; FR21 / UC-S03 | Sales signs in and views orders |
| PASS | S02 | Order confirmation/handoff; FR22–FR23 / UC-S04, UC-S07 | Sales confirms and hands off an order |
| PASS | C01 | Customer cancellation request; FR16 / UC-C14 / BR01 | Customer requests cancellation after confirmation |
| PASS | S03 | Cancellation review; FR24 / UC-S06 | Sales/Admin reviews the request |
| PASS | W01 | Warehouse queue/export; FR30–FR32 / UC-W04–UC-W06 / BR05 | Warehouse views queue and exports an order |
| PASS | W02 | Inventory receipt/adjustment; FR27–FR29 / UC-W01–UC-W03 | Warehouse receives and adjusts stock |
| PASS | W03 | Inventory ledger; FR33 / UC-W07 | Warehouse views transaction history |
| PASS | A01 | Admin read-only inventory permissions; NFR03 | Admin views stock but cannot edit it |
| PASS | R01 | Cross-role authorization/responsive UI; NFR03/NFR05 | Cross-role access and responsive UI checked |

Evidence source: the project owner's explicit confirmation in this conversation. No screenshot was attached or created. These are manual/browser checks reported by the owner, not Codex browser automation. Only the nine summary items are recorded as PASS; detailed scenarios and viewport-by-viewport assertions in `PHASE5_5_UAT_PLAN.md` remain unconfirmed unless separately evidenced.

Final regression and QA postflight are documented in [`PHASE5_5_ACCEPTANCE.md`](./PHASE5_5_ACCEPTANCE.md). Phase 5 is **COMPLETED for Development/QA acceptance and READY FOR FINAL OWNER SIGN-OFF**. Applying migration `20261019174632_AddOrderCancellationRequestFields` to the main `NEXORA` database is outside this acceptance and still requires separate approval. Known limits remain: application-layer `IN`/`ADJUST` replay protection is not absolute exactly-once for direct SQL/bypass; warehouse preparation has no persisted actor/time audit fields; returns/refunds after export and automatic pending-order expiry are out of scope.
