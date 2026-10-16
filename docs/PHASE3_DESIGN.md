# Thiết kế Phase 3 – Tư vấn sản phẩm bằng ngôn ngữ tự nhiên

## 1. Căn cứ và phạm vi

Phase 3 hiện thực UC-C08, FR08–FR10, BR15–BR19 và TC-NLP01–TC-NLP09 trong Chương 2 của tài liệu khóa luận. Chương 2 mô tả pipeline câu tiếng Việt → hồ sơ có cấu trúc → hỏi làm rõ → lọc điều kiện bắt buộc → chấm điểm có trọng số → giải thích có căn cứ. Công thức được khóa luận nêu là `Score(p) = Σ(wi × si(p)) / Σ(wi)`, với `wi ≥ 0`, `si(p) ∈ [0,1]`; điều kiện bắt buộc được áp dụng trước chấm điểm. Nếu tổng trọng số bằng 0, dùng thứ tự mặc định xác định.

Phạm vi ban đầu hỗ trợ Laptop, Điện thoại và Máy tính bảng theo dữ liệu Specification hiện có. Parser là rule-based, tiếng Việt, không phụ thuộc API AI. Phase này không triển khai giỏ hàng, đặt hàng hay kho.

## 2. Audit hiện trạng

- Entity `UsageNeed` và `RecommendationRule` đã có trong Domain và EF Core, ánh xạ tới hai bảng chính thức đã được tạo. Schema không cần thay đổi để xây dựng pipeline cơ bản.
- `RecommendationRule` lưu UsageNeed, Category, Specification, Operator, ThresholdValue, Weight, IsRequired và IsActive. FK kép tới `category_specifications` đã bảo vệ rằng thông số của rule thuộc danh mục được chọn. `RecommendationRulePolicy` kiểm tra toán tử, ngưỡng theo DataType và miền trọng số.
- `ProductSpecification` lưu `Value`, `NumericValue`, `DisplayValue`; giá lưu bằng `decimal`. Dữ liệu số dùng đơn vị tách riêng ở `Specification.Unit`.
- Đọc SQL Server Development `PC01\SQLEXPRESS`, database `NEXORA`, chỉ bằng truy vấn SELECT: 10 Category, 11 Product, 11 Specification, 18 CategorySpecification và 52 ProductSpecification. Trong ba danh mục tư vấn có ba sản phẩm demo mỗi danh mục. Tại thời điểm audit, `usage_needs` và `recommendation_rules` có 0 dòng; Development seeder sau đó đã thêm lần lượt 8 và 86 dòng.
- Laptop hiện có CPU/GPU (ENUM), RAM (NUMBER, GB), SSD (NUMBER, GB), trọng lượng (NUMBER, kg), màn hình (NUMBER, inch). Điện thoại và Máy tính bảng có Chipset (ENUM), RAM (NUMBER, GB), bộ nhớ lưu trữ (NUMBER, GB), màn hình (NUMBER, inch), dung lượng pin (NUMBER, mAh).
- Các sản phẩm có SKU `NXD-*` được seeder đánh dấu tên “NEXORA Demo …”; seeder ghi chú rõ dữ liệu mô phỏng, không phải thông số của nhà sản xuất. Không có dữ liệu UsageNeed/RecommendationRule để phục vụ tư vấn trước Phase 3.

## 3. Kiến trúc đề xuất

Giữ Modular Monolith hiện tại và bổ sung module `Consultation` qua các lớp:

1. **RequirementParser** (Application, thuần C#): làm sạch văn bản giới hạn độ dài; nhận dạng tên danh mục/từ đồng nghĩa, mục đích, ngân sách, định lượng và ưu tiên định tính; trả về kết quả có trạng thái nhận diện.
2. **RequirementNormalizer** (Application): chuẩn hóa dấu tiếng Việt, số tiền VND, toán tử, tên Specification và đơn vị theo alias tường minh.
3. **ClarificationService** (Application): yêu cầu xác nhận khi thiếu Category hoặc UsageNeed, khi tiêu chí mơ hồ, mâu thuẫn hoặc không thể đối chiếu với Specification/đơn vị trong Category. Không chạy xếp hạng khi còn trường thiết yếu cần hỏi.
4. **ConsultationQueryService** (Infrastructure): đọc sản phẩm, CategorySpecification, ProductSpecification, UsageNeed và RecommendationRule bằng EF Core/SQL Server; chỉ trả Product ACTIVE thuộc Category và Brand ACTIVE. Áp điều kiện giá cứng tại SQL; áp điều kiện động bằng `Any` trên ProductSpecification để SQL lọc trước khi materialize.
5. **HardConstraintFilter** (Application): kiểm tra lần cuối các giới hạn ngân sách rõ ràng và điều kiện kỹ thuật định lượng. Thiếu giá trị hoặc đơn vị không tương thích không được coi là đạt.
6. **RecommendationScorer** (Application): lọc `IsRequired` trước; tính từng điểm tiêu chí mềm trong `[0,1]`, nhân Weight lấy từ DB, chia tổng trọng số. Quy tắc GTE/GT tăng điểm tới ngưỡng; LTE/LT ưu tiên giá trị nhỏ hơn ngưỡng; EQ/NE dùng khớp chính xác; ENUM/TEXT dùng phép so khớp theo policy. Không có ProductSpecification thì tiêu chí đó không đóng góp điểm và được ghi là chưa có dữ liệu. Nếu tổng Weight bằng 0, sắp xếp theo giá hiệu lực tăng dần rồi ProductId tăng dần. Điểm bằng nhau được tie-break bằng giá hiệu lực, sau đó ProductId.
7. **RecommendationExplanationService** (Application): tạo lý do từ tên Specification, giá trị chuẩn hóa/hiển thị, giá hiện hành/giá khuyến mại và UsageNeed đã xác nhận. Không suy diễn đặc tính không có trong DB.
8. **ConsultationOrchestrator** (Application): điều phối parse → clarify → truy vấn → lọc → xếp hạng → giải thích; Controller chỉ bind request và render response.
9. **Admin Consultation**: controller và service riêng, `[Area("Admin")]`, `[Authorize(Roles = "Admin")]`; thao tác dùng `RecommendationRulePolicy`, xác thực Specification đang active và gán đúng Category. CRUD dùng POST có antiforgery; không xóa cứng rule đang tham chiếu/đang được dùng.
10. **Razor UI**: trang `/tu-van-san-pham`, Bootstrap hiện có, POST/Fetch có giới hạn request, giữ lại câu nhập và hiển thị hồ sơ/clarification/kết quả/empty state; liên kết tới chi tiết và so sánh.

## 4. Quy ước nhận diện và hồ sơ nhu cầu

Hồ sơ tách giá trị và nguồn nhận diện: `Explicit` (người dùng nêu rõ), `Normalized` (đã đổi alias/đơn vị), `Uncertain` (có thể hiểu nhưng cần xác nhận), `Missing` (chưa có). Mỗi field giữ nội dung gốc và giá trị chuẩn hóa; các điều kiện số giữ `Specification`, toán tử, `decimal` và đơn vị.

- Danh mục: `laptop`, `máy tính xách tay`, `notebook`; `điện thoại`, `smartphone`; `máy tính bảng`, `tablet`.
- UsageNeed: từ điển synonym được kiểm thử cho văn phòng, học tập/học online, lập trình, gaming, đồ họa, chỉnh sửa ảnh/video và thông thường. “Công việc” riêng lẻ là mơ hồ, không tự chọn văn phòng hay lập trình.
- Ngân sách: `dưới/không quá/tối đa` → Maximum; `từ X đến Y` → Range; `tầm/khoảng/~` → Approximate; số tiền trần không có chỉ dấu → Approximate/Uncertain, không làm cận cứng. Tiền dùng `decimal` VND, không dùng float/double. Khoảng sai thứ tự/ngoài miền hợp lý bị từ chối.
- Điều kiện kỹ thuật chỉ là hard constraint khi ngôn ngữ nêu rõ ngưỡng (ví dụ “RAM tối thiểu 16GB”, “SSD từ 512GB”). Một con số gắn Specification không có từ chỉ ngưỡng được đánh dấu Uncertain và hỏi xác nhận.
- Ưu tiên định tính như “RAM lớn”, “pin tốt”, “máy nhẹ”, “CPU mạnh”, “GPU rời”, “màn hình lớn” chỉ tạo soft preference; ngưỡng/điểm đến lấy từ RecommendationRule đang hoạt động, không tự tạo ngưỡng sản phẩm. `RAM/SSD/Storage` dùng GB, trọng lượng kg, pin mAh; không so sánh khi unit khác.

## 5. Xử lý ngân sách và chấm điểm

Maximum và Range là hard constraints đúng như đã nêu; Approximate chỉ được giữ làm mục tiêu tham khảo, tuyệt đối không loại sản phẩm theo đó. Do schema hiện không có trọng số cấu hình riêng cho “độ gần giá mục tiêu”, Phase 3 giữ approximate budget trong profile và đưa giá vào tie-break xác định sau điểm rule; không cộng một trọng số tự đặt vào công thức khóa luận. Cần xem xét bổ sung cách cấu hình trọng số ngân sách nếu người duyệt muốn ngân sách ước lượng ảnh hưởng trực tiếp điểm.

Điều kiện `IsRequired` hiện có trong recommendation rule được đánh giá trước scoring. Thông số chưa có dữ liệu không thể làm rule bắt buộc đạt; sản phẩm bị loại. Trong soft rules, thông số thiếu không nhận điểm. Điều kiện kỹ thuật do người dùng nêu cũng phải thỏa toàn bộ, không tự nới lỏng. Candidate query chỉ tải ứng viên của một danh mục được xác nhận; dữ liệu hiển thị dùng truy vấn DB thật.

## 6. Dữ liệu demo và giới hạn

Development seeder additive/idempotent chỉ chạy trong Development và không cập nhật/xóa dòng sẵn có. Database hiện có 8 UsageNeed và 86 RecommendationRule gắn với Category/Specification lấy từ DB. Rule được gắn nhãn cấu hình mô phỏng để kiểm thử, không phải tuyên bố kỹ thuật nhà sản xuất. Seed bỏ qua ánh xạ không hợp lệ; log chỉ ghi số lượng thêm, không log câu tư vấn hay thông tin tài khoản. SQL Server integration test chạy seeder lần hai và xác nhận số dòng không tăng.

Catalog hiện chỉ có 3 sản phẩm demo mỗi danh mục, Brand và thông số mô phỏng. Vì vậy có thể kiểm thử đúng thuật toán và các nhánh logic nhưng chưa thể đánh giá chất lượng tư vấn trên hàng hóa thực hoặc quy mô lớn. Battery chỉ có mAh (không có thời lượng sử dụng), nên “pin tốt” được diễn giải giới hạn là dung lượng mAh lớn hơn trong dữ liệu, không phải thời gian dùng thực tế.

## 7. Schema và quyết định còn mở

Phase 3 bắt đầu không cần thêm bảng hoặc cột. Không chỉnh InitialFoundation và không chạy database update. API query có thể dùng schema hiện tại; mọi yêu cầu dữ liệu vượt schema sẽ được báo trước và chờ duyệt migration.

Quyết định cấu hình cho Admin không có quyền xóa cứng; dùng ngừng sử dụng qua `IsActive`. Nếu hai rule cùng UsageNeed/Category/Specification đang active, Admin service phải từ chối bản trùng để kết quả có thể dự đoán được. Đổi cấu hình có hiệu lực ở lần tư vấn tiếp theo bằng truy vấn DB mỗi request, không cache.

## 8. Tiến độ triển khai và tiêu chí

| Phần | Trạng thái | Bằng chứng / việc còn lại |
|---|---|---|
| 3.1 | Hoàn tất | Đối chiếu Chương 2, schema và dataset SQL; thiết kế ghi tại tài liệu này; không migration |
| 3.2–3.3 | Triển khai; kiểm thử tự động đạt | Parser/normalizer/profile/clarification; unit tests cho danh mục, synonym, ngân sách, hard/soft constraint, mơ hồ/mâu thuẫn, đơn vị và input lỗi |
| 3.4–3.5 | Triển khai; unit + SQL integration đạt | Ứng viên từ SQL, hard filter, `IsRequired`, score có trọng số, missing value, zero-weight và tie-break; dữ liệu giá không đổi khi đổi trọng số |
| 3.6 | Triển khai; unit/HTTP/browser kiểm tra được | Lý do hiển thị giá trị và ngưỡng Specification thật; thiếu thông số pin laptop được nêu rõ |
| 3.7 | Triển khai; SQL integration và browser Admin một phần | Tạo/sửa UsageNeed/rule, validation và thay weight/rank đã kiểm tra; phiên Customer thực tế và nút “Bật” automation còn cần xác minh |
| 3.8 | Có Razor UI; browser kiểm tra một phần | Luồng tư vấn, chi tiết và so sánh đã kiểm tra; viewport 390×844 chưa áp dụng được, responsive còn manual |
| 3.9 | Chưa nghiệm thu hoàn toàn | 97/97 test và 5 SQL Server integration đạt; không có model delta; responsive/Customer session còn chờ, NFR01 chỉ đo trên dataset nhỏ |
