# Nghiệm thu Phase 3 – Tư vấn sản phẩm bằng ngôn ngữ tự nhiên

## Trạng thái

**Trạng thái mới nhất: chủ dự án đã xác nhận nghiệm thu Phase 3 trong yêu cầu bắt đầu Phase 4 ngày 16/10/2026.** Xác nhận này không bổ sung bằng chứng cho các thao tác browser còn mở; các giới hạn lịch sử bên dưới vẫn được giữ nguyên và không được hiểu là đã kiểm thử.

## Phạm vi đã triển khai

- Parser rule-based tiếng Việt, chuẩn hóa danh mục, mục đích, tiền VND, thông số/đơn vị, ưu tiên và điều kiện cứng; không gọi API AI trả phí.
- Hồ sơ nhu cầu và luồng hỏi bổ sung cho thiếu danh mục/mục đích, nội dung mơ hồ hoặc điều kiện mâu thuẫn.
- Truy vấn candidate từ SQL Server; lọc trạng thái/category, ngân sách rõ ràng và ràng buộc số trước xếp hạng.
- Recommendation rules từ database, lọc `IsRequired`, weighted scoring, kết quả ổn định, xử lý thiếu thông số và zero-weight fallback.
- Lý do/giới hạn truy được về Product/Specification/ProductSpecification; link sang chi tiết và so sánh.
- Admin MVC cho UsageNeed và RecommendationRule, bảo vệ bằng role Admin và antiforgery.
- Development-only seeder additive/idempotent cho cấu hình demo.

## Phân loại bằng chứng

### Unit tests và authorization metadata tests

Được chạy trong `NEXORA.Tests`. Bao gồm parser và chuẩn hóa tiền, danh mục/alias, hỏi bổ sung, điều kiện cứng/mềm, unit không tương thích, mâu thuẫn, chuỗi rỗng/vô nghĩa/quá dài, điều kiện `IsRequired`, weighted ranking, thứ tự ổn định, thay đổi weight, giải thích theo thông số, giá trị thiếu, zero-weight fallback, metadata Admin-only/POST/antiforgery và kiểm tra giới hạn Weight invariant trong locale `vi-VN`.

### SQL Server integration tests

Chạy với `NEXORA_SQLSERVER_TEST_CONNECTION` trỏ đến Windows Integrated Authentication trên `PC01\SQLEXPRESS`, database `NEXORA`. Năm test bao phủ:

1. SQL candidate filter theo giá và dynamic Specification.
2. Đọc context rules từ SQL, từ chối cấu hình Specification sai Category.
3. Development seed additive/idempotent.
4. Admin management service create/edit/deactivate và dọn dữ liệu kiểm thử.
5. Thay trọng số rule đổi thứ tự sản phẩm demo lấy từ SQL, không thay đổi giá sản phẩm; xác minh tắt/bật rule và UsageNeed; rule/UsageNeed tạm được dọn trong `finally`.

### HTTP smoke tests

Đã gửi HTTP thực tế tới Web Development tại `http://127.0.0.1:5069` cho các POST tư vấn. Sau build cuối, ứng dụng được khởi động lại tại `http://127.0.0.1:5058` và các GET/authentication smoke được chạy lại:

- GET `/tu-van-san-pham` trả 200; POST anti-forgery trả kết quả từ database cho câu đầy đủ.
- Câu thiếu thông tin trả câu hỏi bổ sung, không trả gợi ý; gửi lại cùng mô tả với Category và UsageNeed đã chọn trả kết quả.
- Câu có ngân sách tối đa và RAM bắt buộc không thể thỏa trả empty state, không tự nới điều kiện.
- “20tr” và “20 triệu” được chuẩn hóa thành cùng ngân sách ước lượng; sản phẩm vượt giá tham khảo vẫn có thể xuất hiện, xác nhận ngân sách ước lượng không phải hard filter.
- Năm request lặp lại cho cùng câu cho thứ tự product ID như nhau.
- Trang kết quả hiển thị dữ liệu RAM 16 GB và trọng lượng 1.62 kg lấy từ sản phẩm demo; câu giải thích chỉ hiển thị thông số liên quan preference đã nhận diện.
- Đầu vào rỗng/vô nghĩa được xử lý an toàn.
- `/`, `/account/login`, `/account/register` trả 200; anonymous `/Admin/Consultation/Rules` được chuyển hướng đăng nhập.
- Trên lần khởi động cuối ở cổng 5058, `/`, `/tu-van-san-pham`, `/account/login`, `/account/register` trả HTTP 200 và anonymous `/Admin/Consultation/Rules` trả HTTP 302.

Năm request hiệu năng đã làm nóng cho một truy vấn laptop, trong Development trên SQL Server Express với 11 sản phẩm tổng cộng, có trung bình **15 ms**, chậm nhất **17 ms** và một sản phẩm sau lọc. Đây là số đo HTTP cục bộ với dataset rất nhỏ, không chứng minh khả năng mở rộng hoặc hiệu năng production. Mục tiêu NFR01 của luận văn là 8 giây.

### Browser/manual tests

Thực hiện trên trình duyệt Codex tại `http://127.0.0.1:5058` bằng phiên Admin đã đăng nhập sẵn. Không nhập, đọc hoặc yêu cầu mật khẩu. Các thao tác đã quan sát trực tiếp:

- Câu đầy đủ `Laptop dưới 25 triệu, lập trình, RAM tối thiểu 16GB, ưu tiên máy nhẹ` trả Laptop Beta từ SQL với giá 23.490.000 ₫, RAM 16 GB, trọng lượng 1,62 kg; không vượt trần giá hoặc thiếu điều kiện RAM.
- Câu thiếu thông tin `Tôi cần máy cho công việc` hỏi làm rõ thay vì tự đoán; câu `Laptop dưới 10 triệu, lập trình, RAM tối thiểu 64 GB` báo không có sản phẩm và giữ nguyên điều kiện cứng.
- `20tr` và `20 triệu` được chuẩn hóa thành 20.000.000 ₫ dạng ngân sách tham khảo; kết quả và thứ tự giống nhau, sản phẩm trên mức tham khảo vẫn có thể xuất hiện.
- `SSD tối thiểu 512 GB` giữ yêu cầu SSD; câu ưu tiên máy nhẹ/pin tốt đưa giới hạn `Trọng lượng ≤ 1.8 kg` cạnh giá trị thật. Sau sửa code, Gamma 2,05 kg được nêu là chưa đạt tốt, không bị mô tả là phù hợp. Laptop không khai báo Specification dung lượng pin nên UI báo không thể đánh giá pin; không suy ra thời lượng pin.
- Mở chi tiết Laptop Beta từ kết quả tư vấn; so sánh từ chính kết quả mở được trang so sánh Beta/Gamma với RAM, SSD, trọng lượng và giá.
- Admin UsageNeed: mở danh sách, tạo UsageNeed QA riêng, sửa tên và xác nhận lại dòng đã cập nhật. Admin RecommendationRule: mở danh sách/form, tạo hai rule QA, sửa Specification/ngưỡng/trọng số, từ chối operator GTE cho ENUM, từ chối ngưỡng chữ trên NUMBER và từ chối Specification không thuộc Laptop. Dữ liệu QA được xóa sau test.
- Với rule QA, thứ tự SQL đổi từ Gamma đứng đầu (weight SSD 3, weight trọng lượng 1) sang Alpha đứng đầu (weight SSD 1, weight trọng lượng 3). Tắt rule trọng lượng làm điểm/rank đổi; bật lại qua checkbox trên form sửa thành công. Nút danh sách “Bật” được gửi qua automation nhưng trạng thái không đổi; hidden form values bị công cụ browser làm mờ, vì vậy nút này cần được xác nhận thủ công trong browser thông thường.
- Trang Admin mở được trong phiên Admin. HTTP request anonymous tới `/Admin/Consultation/Rules` trả 302 tới luồng đăng nhập. Không thực hiện đăng nhập Customer trong một phiên riêng, nên từ chối bằng phiên Customer chưa có bằng chứng browser trực tiếp; test metadata xác nhận controller bắt buộc role Admin.

Responsive 390×844 đã được kiểm tra lại sau UI redesign: trang `/tu-van-san-pham` hiển thị hero và form xếp dọc, trang danh mục ở trạng thái không kết quả không tràn ngang; menu mobile mở bằng Bootstrap local. Browser ghi nhận `window.innerWidth = 390`, `documentElement.scrollWidth <= clientWidth`. Chưa chạy recommendation để kiểm tra danh sách nhiều kết quả trên mobile. Loading state cũng chưa được xác minh bằng thao tác chậm mạng.

### Kiểm tra hồi quy UI ngày 2026-10-16

- `dotnet restore NEXORA.sln`: thành công; `dotnet build NEXORA.sln --no-restore`: thành công, 0 warning/error.
- `dotnet test NEXORA.sln --no-build --no-restore`: 93 passed, 0 failed, 5 SQL Server integration tests skipped vì tiến trình kiểm thử hiện tại không có cấu hình kết nối SQL. Đây là lượt hồi quy UI; kết quả SQL Server integration trước đó (97 passed) vẫn là bằng chứng riêng, không được gộp vào lượt này.
- HTTP integration test mới render `/workspace/Admin`, `/Admin/Consultation/UsageNeeds` và `/Admin/Consultation/Rules` bằng test-only Admin principal cùng service data giả trong host Kestrel riêng; xác nhận Admin layout, CSS, badge role, điều hướng module, logout, thống kê danh sách và lọc Rules. Không dùng tài khoản thật/database và không kiểm tra thao tác CRUD ghi dữ liệu.
- Chủ dự án sau đó thông báo đã đăng nhập lại, nhưng browser tool vẫn chặn URL local theo chính sách truy cập. Admin shell/CRUD sau redesign và tương tác offcanvas mới vì vậy chưa được xác minh bằng browser trong lượt này; không dùng workaround. Public browser smoke trước đó được ghi ở [`UI_REDESIGN_REPORT.md`](./UI_REDESIGN_REPORT.md).

## TC-NLP01–TC-NLP09

| Test case | Bằng chứng | Trạng thái |
|---|---|---|
| TC-NLP01 | Parser unit test; HTTP POST câu laptop/ngân sách/lập trình/preference, kết quả SQL | Đã kiểm tra bằng unit + HTTP smoke |
| TC-NLP02 | Parser unit test cho “20tr”/“20 triệu”; HTTP smoke với hai cách diễn đạt và giá trị chuẩn hóa | Đã kiểm tra bằng unit + HTTP smoke |
| TC-NLP03 | Unit test và HTTP hỏi bổ sung; follow-up kèm category/usage tạo kết quả | Đã kiểm tra bằng unit + HTTP smoke |
| TC-NLP04 | Unit/SQL filtering và HTTP câu có max budget + RAM bắt buộc không khả thi | Đã kiểm tra; không nới điều kiện |
| TC-NLP05 | Unit test và 5 HTTP request lặp; thứ tự Product ID như nhau | Đã kiểm tra |
| TC-NLP06 | Unit test; browser hiển thị giá trị và ngưỡng trọng lượng thực, báo thiếu Specification pin cho Laptop | Đã kiểm tra; không suy ra thời lượng pin |
| TC-NLP07 | Unit/SQL integration và browser Admin đổi weights; thứ hạng Gamma → Alpha; giá không đổi | Đã kiểm tra trên dữ liệu demo SQL |
| TC-NLP08 | Unit test input rỗng/vô nghĩa/quá dài; HTTP empty/gibberish | Đã kiểm tra |
| TC-NLP09 | HTTP smoke năm request ấm: trung bình 15 ms, max 17 ms; 11 sản phẩm trong DB | Đo thực tế trên Development, không đại diện production |

## Kết quả build/test và dữ liệu

- `dotnet restore NEXORA.sln`: thành công, packages hiện up-to-date.
- `dotnet build NEXORA.sln`: thành công, 0 warning, 0 error.
- `dotnet test NEXORA.sln` với SQL Server test connection: **97 passed, 0 failed, 0 skipped**, gồm 5 SQL Server integration test.
- `dotnet ef migrations has-pending-model-changes --no-build --project src/NEXORA.Infrastructure --startup-project src/NEXORA.Web --context AppDbContext`: model không thay đổi kể từ migration.
- Database sau test: 27 bảng tổng cộng (22 nghiệp vụ + 4 kỹ thuật Identity + `__EFMigrationsHistory`); 10 Category, 2 Brand, 11 Product, 11 Specification, 18 CategorySpecification, 52 ProductSpecification, 8 UsageNeed và 86 RecommendationRule. Không còn UsageNeed/Rule QA tạm.
- SQL Server xác nhận `orders.completed_at` là `datetime2`, `inventories.row_version` là `timestamp` 8 byte, `products.price`/`discount_price` là `decimal(18,2)`. Migration history chỉ có `20261015173046_InitialFoundation`. Không tạo migration, không chạy `database update`, không thay đổi schema.
- Bộ dữ liệu catalog là demo; seed chỉ bổ sung cấu hình UsageNeed/RecommendationRule mô phỏng và không thêm/sửa sản phẩm.

## Giới hạn dữ liệu và việc cần làm trước khi đóng Phase 3

- Laptop có RAM (GB), SSD (GB), trọng lượng (kg), CPU/GPU enum và màn hình (inch), nhưng không có thông số pin/thời lượng sử dụng. Điện thoại/máy tính bảng có dung lượng pin mAh, không có thời lượng dùng. Không thể tư vấn thời gian pin chính xác từ mAh.
- Sản phẩm và cấu hình rule là dữ liệu demo để kiểm tra thuật toán; cần nhập dữ liệu kiểm chứng trước khi trình bày tư vấn như thông tin thương mại chính thức.
- Còn cần xem kết quả tư vấn nhiều sản phẩm ở 390×844 và thử truy cập Admin bằng một phiên Customer thực tế; anonymous đã bị chuyển hướng 302 và metadata role Admin đã được kiểm tra.
- Nút “Bật” trên bảng rule không được xác minh dứt điểm bằng automation; đổi checkbox `IsActive` trong form sửa đã bật lại rule thành công. Cần thử nút này trong browser thông thường.
- Admin tạo/sửa UsageNeed và tạo/sửa/tắt rule đã được browser kiểm tra. Xóa cứng không phải chức năng được cung cấp; rule được ngừng sử dụng bằng `IsActive`.
- Khi cần thể hiện “pin tốt” trên laptop hoặc thời lượng pin, cần duyệt nguồn dữ liệu và quyết định có bổ sung Specification/dữ liệu hay không. Không có thay đổi schema trong Phase này.
- Trước khi đóng Phase 3: hoàn tất các thao tác manual ở trên; chủ dự án xác nhận nghiệm thu; sau đó quét diff/secrets và tạo commit `feat: complete Phase 3 NLP recommendation`. Chưa commit, chưa push và Phase 4 chưa bắt đầu.

## UI Redesign final acceptance follow-up – 16/10/2026

Website Development được khởi động để nghiệm thu thủ công tại `http://127.0.0.1:5058`. HTTP smoke trả 200 cho trang chủ, Catalog, tư vấn, Bootstrap CSS/JS, `site.css`, `admin.css`, jQuery và hai thư viện validation. Các route Admin Products/Categories/Brands/Specifications/UsageNeeds/Rules và Profile trả 302 tới đăng nhập khi gọi ẩn danh. Đây là kiểm tra HTTP, không phải browser test.

Rà soát source xác nhận Admin Layout/ViewStart/Tag Helpers và static paths đúng; breakpoint dùng desktop sidebar và mobile offcanvas; table nằm trong vùng cuộn nội bộ. Không sửa code trong lượt này. SQL read-only sau startup vẫn ghi nhận 10 Category, 2 Brand, 11 Product, 8 UsageNeed, 86 RecommendationRule; seeder không thêm UsageNeed hay rule. Test suite gần nhất vẫn là 99 passed, 0 failed, 0 skipped, gồm các SQL Server test trên database riêng `NEXORA_IntegrationTests`.

**Chưa nghiệm thu browser:** chưa kiểm tra trực quan Admin Products, Categories, Brands, Specifications, UsageNeeds, Rules, Profile và trang tư vấn tại 1440×900, 768×1024, 390×844; chưa thao tác nút Bật/Tắt trên trang Rules bằng browser; chưa đăng nhập Customer thực để xác nhận từ chối. Chủ dự án phản hồi chưa thể kiểm tra thủ công tại lượt này. Website vẫn đang chạy tại `http://127.0.0.1:5058`; khi thuận tiện, cần tiếp tục checklist trong Chrome/Edge thông thường và báo kết quả. Giữ nguyên database schema/migration; chưa commit/push; Phase 3 và UI Redesign chưa được xác nhận hoàn tất; Phase 4 chưa bắt đầu.

## Chốt nghiệm thu và Git checkpoint – 16/10/2026

Chủ dự án xác nhận Phase 3 đã được nghiệm thu khi yêu cầu triển khai Phase 4. Đây là quyết định nghiệm thu của chủ dự án; không thay đổi các ghi chú rằng browser review sau UI polish, phiên Customer riêng, responsive đủ ba viewport và một số thao tác rule chưa có bằng chứng trong các lượt trước.

Chạy lại trên workspace hiện tại: `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln` thành công với 0 warning/0 error; `dotnet test NEXORA.sln` đạt **100 passed, 0 failed, 0 skipped**. Sáu SQL Server integration tests được chạy sau khi xác minh biến môi trường chỉ tới `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`; không dùng database `NEXORA` cho lượt này. Không tạo migration, không cập nhật schema và không chạy database update. HTTP smoke/browser kết quả trước đó vẫn được phân loại riêng như các mục trên.
