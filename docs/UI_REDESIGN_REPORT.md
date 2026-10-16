# NEXORA – Báo cáo redesign UI/UX

**Trạng thái:** redesign và lượt final polish đã được triển khai trong source. Phase 3 vẫn chờ nghiệm thu thủ công bằng trình duyệt; kết quả build, test, SQL test và HTTP smoke được ghi theo từng lượt bên dưới, không thay thế xác nhận trực quan.

## 1. Audit và nguyên nhân gốc

`/Admin/Products` và `/Admin/Consultation/UsageNeeds` được render như HTML mặc định vì các Razor View trong `Areas/Admin/Views` không có `_ViewStart.cshtml`. `Views/_ViewStart.cshtml` chỉ đặt `Layout = "_Layout"` cho nhánh `Views`; nó không tự áp dụng cho Area Admin. Vì thế trang Admin trước khi sửa trả về nội dung View thuần, không có `<link rel="stylesheet">`, không có Bootstrap và không có `site.css`. Bằng chứng từ DOM của trang Products: `document.styleSheets` rỗng và font body là `Times New Roman`; trang Home cùng lúc có hai stylesheet và font system-ui.

Đã thêm `Areas/Admin/Views/_ViewStart.cshtml`, trỏ tới Layout riêng trong `Areas/Admin/Views/Shared/_AdminLayout.cshtml`. Đây là nguyên nhân trực tiếp đã xác định và đã sửa. Bootstrap trước đây được tham chiếu từ CDN ở Layout cửa hàng; cả hai Layout hiện dùng các file Bootstrap 5 đã có trong `wwwroot/lib/bootstrap`, để giao diện không phụ thuộc việc tải CSS/JS từ mạng ngoài.

Trong HTTP integration test mới cho Admin workspace, lần render đầu phát hiện `_AdminLayout` gọi `_AdminNavigation` bằng tên partial tương đối. Với trang RoleLanding, Razor chỉ tìm trong `Views/RoleLanding` và `Views/Shared`, khiến phản hồi thành HTTP 500. Đã đổi hai lời gọi sang đường dẫn ứng dụng `~/Areas/Admin/Views/Shared/_AdminNavigation.cshtml`; bài test render lại thành công.

## 2. Thay đổi theo các chặng UI-01–UI-07

- **UI-01 – Design system:** xây lại token màu, nền, chữ, trạng thái focus, nút, trường nhập, card, bảng và breakpoint trong `wwwroot/css/site.css`.
- **UI-02 – Admin shell:** tạo sidebar điều hướng theo module hiện có, topbar, tên và role Admin của phiên hiện tại, trạng thái môi trường, avatar chữ cái, đăng xuất POST, thông báo và menu offcanvas trên mobile.
- **UI-03 – Admin views:** áp dụng stylesheet Admin cho bảng Category, Brand, Product, Specification, CategorySpecification, UsageNeed và RecommendationRule; tiêu đề Products, phân trang, placeholder ảnh, xác nhận ngừng kinh doanh và form ảnh có preview; giữ nguyên route, model, form, antiforgery, validation và quyền `[Authorize]` hiện có.
- **UI-04 – Storefront:** làm mới header, wordmark, vùng nội dung, hero, danh mục, product card, lọc, chi tiết, so sánh và footer. Bộ lọc catalog dùng offcanvas Bootstrap trên mobile/tablet, giữ dạng sidebar ở desktop. Điều hướng Admin được rút gọn thành một lối vào riêng; thêm liên kết tư vấn.
- **UI-05 – Consultation:** làm mới hero, form nhập tự nhiên, gợi ý nhập nhanh, hồ sơ nhận diện, cảnh báo, empty/loading state và thẻ kết quả. Trang Admin UsageNeeds có số đếm tính từ danh sách đã truy vấn; Rules có GET filter theo UsageNeed, Category và trạng thái. Dữ liệu vẫn được đọc từ Application/SQL Server, không thêm sản phẩm hay quy tắc giả.
- **UI-06 – Responsive và accessibility cơ bản:** thêm skip link, focus rõ, nhãn/landmark, menu điều hướng có trạng thái accessible, tôn trọng `prefers-reduced-motion`; kiểm tra cuộn ngang ở các kích thước mục tiêu.
- **UI-07 – Hồi quy và tài liệu:** chạy restore/build/test, kiểm tra trình duyệt, ghi kết quả tại đây và cập nhật kế hoạch triển khai.

Không thêm frontend framework. Không đổi controller, nghiệp vụ, authentication/authorization hay database schema. Không tạo migration và không chỉnh dữ liệu SQL.

## 3. Kiểm thử

### 3.1. Restore, build và test

- `dotnet restore NEXORA.sln`: thành công, mọi package đã cập nhật.
- `dotnet build NEXORA.sln --no-restore`: thành công, 0 warning, 0 error. Lượt build đầu gặp file DLL Web đang bị tiến trình website cũ giữ khóa; sau khi dừng đúng tiến trình Development do lượt trước khởi chạy, build lại thành công.
- `dotnet test NEXORA.sln --no-build --no-restore`: ở lượt kiểm tra 16/10/2026 đạt 94 passed, 0 failed, 5 skipped, tổng 99. Năm SQL Server integration test bị skip do `NEXORA_SQLSERVER_TEST_CONNECTION` không được cấu hình; không ghi nhận chúng là đã chạy. HTTP integration tests kiểm tra Admin layout và hồ sơ bằng principal/test store giả, không dùng dữ liệu tài khoản thật.

### 3.2. Browser test

Website chạy tại `http://127.0.0.1:5058` trong Development.

| Trang/thao tác | Kích thước | Kết quả quan sát |
|---|---:|---|
| Trang tư vấn, form ban đầu | 390×844 | Hero và form xếp dọc, điều khiển vừa viewport; không thấy lỗi console. |
| Danh mục/tìm kiếm chuỗi không khớp (trước lần đổi mobile filter sang offcanvas) | 390×844 | Hiện đúng trạng thái “Không tìm thấy sản phẩm”; không tràn ngang. |
| Menu điều hướng mobile | 390×844 | Nút menu mở/đóng được qua Bootstrap local; liên kết Trang chủ, Sản phẩm, Tư vấn và xác thực hiện ra. |
| Danh mục sản phẩm (trước lần đổi mobile/tablet filter sang offcanvas) | 768×1024 | Lưới sản phẩm dùng breakpoint tablet; không tràn ngang. |
| Trang chủ | 1440×900 | Hero, lối vào tư vấn, danh mục và sản phẩm dùng layout mới; không lỗi console và không tràn ngang. |
| Chi tiết sản phẩm | 1440×900 | Nội dung hai cột, khu vực ảnh dự phòng và bảng Specification hiển thị đúng. |
| So sánh hai sản phẩm cùng danh mục | 1440×900 | Bảng so sánh và trạng thái thông số khác nhau hiển thị; trang đọc được. |

Các lần đo kích thước thực tế cho `window.innerWidth` là 390, 768 và 1440; `documentElement.scrollWidth` không vượt `clientWidth` ở cả ba kích thước. Đã quan sát DOM/stylesheets sau khi chuyển Bootstrap sang file local. Đây là smoke test browser trực tiếp, không phải kiểm thử WCAG đầy đủ.

Sau đó đã chuyển bộ lọc catalog ở mobile/tablet sang Bootstrap offcanvas. Công cụ browser từ chối mở lại URL local theo chính sách truy cập; do đó thao tác mới này hiện chỉ được kiểm tra qua build, HTTP 200 của trang/assets và markup, chưa được xác minh tương tác bằng trình duyệt.

### 3.3. Phần cần đăng nhập Admin

Khi khởi động lại website để build mã mới, cookie phiên Admin cũ không còn hợp lệ; yêu cầu `/Admin/Products` chuyển về trang đăng nhập. Tôi đã dừng trước khi nhập thông tin tài khoản và yêu cầu chủ dự án tự đăng nhập trong tab trình duyệt. Vì vậy chưa tuyên bố đã xem trực tiếp layout Admin sau redesign hoặc kiểm tra tương tác Admin bằng browser. Khi chủ dự án báo đã đăng nhập, cần xác nhận tối thiểu Products, Consultation/UsageNeeds, Consultation/Rules và menu responsive. Không cần thay đổi dữ liệu để review giao diện.

Chủ dự án sau đó thông báo đã đăng nhập lại. Tuy nhiên, công cụ browser vẫn từ chối truy cập URL local theo chính sách URL; không thử đường vòng hoặc bề mặt browser khác. Vì vậy chưa có bằng chứng browser trực tiếp cho Admin shell/CRUD hoặc thao tác mở/đóng offcanvas sau sửa. HTTP integration test thay thế cho phần render Admin: test host không dùng Program production, database hay tài khoản thật; test-only authentication handler cấp role Admin giả. Test xác nhận HTTP 200 cho workspace, UsageNeeds và Rules; layout/CSS/badge role/module links/logout; thống kê UsageNeeds lấy từ dữ liệu test và Rules lọc theo nhu cầu/danh mục/trạng thái. Đây không phải browser test và không kiểm thử thao tác CRUD ghi dữ liệu. Website Development đã được khởi động lại tại `http://127.0.0.1:5058`; HTTP GET `/`, `/Catalog`, `/Catalog/Index`, `/tu-van-san-pham`, `/account/login`, `/account/register` trả 200; anonymous GET `/Admin/Products` trả 302 về luồng đăng nhập; `site.css`, `admin.css`, Bootstrap CSS và JS trả 200.

## 4. Trạng thái và việc còn lại

- Source UI đã được cập nhật; build/test và browser smoke công khai đạt theo phạm vi trên. HTTP integration test cũng render Admin workspace thành công bằng principal giả trong host riêng.
- Admin shell và các màn quản trị sau redesign chưa được xem trực tiếp bằng browser: chủ dự án đã đăng nhập lại, nhưng browser tool chặn URL local. CRUD Admin và quyền Customer chưa được kiểm chứng qua giao diện trong lượt redesign này.
- Năm SQL Server integration test được skip trong lượt này; test SQL Server đã chạy trong lượt Phase 3 trước đó không bị coi là bằng chứng cho phiên build mới.
- Responsive tư vấn đã có bằng chứng 390×844; kết quả tư vấn nhiều đề xuất chưa được kiểm tra ở mobile. Catalog đã được đo ở 390×844 trước khi bổ sung offcanvas, nên phép đo cũ không xác nhận thao tác mở/đóng bộ lọc mới.
- Kiểm thử loading state chậm mạng và đánh giá WCAG chuyên sâu chưa thực hiện.
- Phase 3 vẫn đang chờ xác nhận của chủ dự án, kiểm tra Customer truy cập vùng Admin và xác nhận nút bật rule trên trang danh sách; xem [`PHASE3_ACCEPTANCE.md`](./PHASE3_ACCEPTANCE.md).
- Không commit, không push, không bắt đầu Phase 4; không đổi schema hoặc áp dụng migration.

## 5. Follow-up profile, bảng quản trị và test an toàn – 16/10/2026

Đã rà lại `Areas/Admin/Views/_ViewStart.cshtml`, `Areas/_ViewImports.cshtml`, `_AdminLayout.cshtml` và các Admin View trước khi chỉnh CSS. ViewStart đặt Admin Layout riêng; Tag Helpers được import ở nhánh Areas; stylesheet/Bootstrap được nhúng từ đường dẫn local. Không phát hiện Layout/ViewStart sai khiến trang rơi về HTML mặc định. Trang hồ sơ dùng `[Authorize]` và `UserManager.GetUserAsync(User)`, không có tham số user ID; truy vấn chỉ lấy roles và trường hồ sơ của principal hiện tại. Đọc SQL chỉ để kiểm tra role xác nhận user ID 2 có cả `Admin` và `Customer`; không đọc password hash.

Thêm HTTP integration test với hai người dùng QA giả lập trong store in-memory: Admin có đồng thời `Admin`/`Customer`; Customer khác có email và tên riêng. Test xác nhận hồ sơ chỉ render dữ liệu theo NameIdentifier của phiên Admin; thêm `?id=3` không chuyển sang Customer khác; menu Tài khoản được render; form Đăng xuất là POST và chứa antiforgery token; POST đăng xuất có token chuyển hướng; hồ sơ/đăng xuất ẩn danh bị chặn. Test không truy cập hay sửa SQL Server. Bổ sung kiểm tra HTTP 200 cho CSS/JS cục bộ.

UI tiếp tục được tinh chỉnh theo design system navy/blue/trắng: token màu dùng Primary `#2563EB`, Primary Dark `#1D4ED8`, nền `#F8FAFC`, đường viền `#E2E8F0`; menu Admin đánh dấu trang hiện tại cho công nghệ hỗ trợ; bảng nhiều cột có chiều rộng tối thiểu trong vùng `.table-responsive`; các thao tác trong bảng có khoảng cách; cột trạng thái/yêu cầu dùng badge; form Product chia nhóm thông tin, giá/trạng thái và ảnh mà không đổi model binding, route, validation, upload hay antiforgery. Các form Category, Brand, Specification, UsageNeed và Rules vẫn dùng chung lớp card/form Admin.

**Kết quả kiểm thử mới nhất:** `dotnet restore NEXORA.sln` thành công; `dotnet build NEXORA.sln --no-restore` thành công, 0 warning/0 error. Lần chạy không có SQL test connection đạt 94 passed, 0 failed, 5 skipped (tổng 99); nguyên nhân là `SqlServerFactAttribute` bỏ qua đúng 5 bài khi thiếu `NEXORA_SQLSERVER_TEST_CONNECTION`. Theo lựa chọn của chủ dự án, đã tạo database cô lập `NEXORA_IntegrationTests` trên `PC01\SQLEXPRESS`, áp dụng migration InitialFoundation đã duyệt chỉ vào database này và nạp dữ liệu demo qua seeder Development. Biến `NEXORA_SQLSERVER_TEST_CONNECTION` đã được lưu ở User scope trỏ tới database test; không ghi giá trị chuỗi kết nối vào tài liệu. Đếm xác nhận 27 bảng, một migration `20261015173046_InitialFoundation`, cùng 10 Category, 2 Brand, 11 Product, 11 Specification, 18 CategorySpecification, 52 ProductSpecification, 8 UsageNeed và 86 RecommendationRule. Chạy toàn bộ solution với connection string test đạt **99 passed, 0 failed, 0 skipped**. Truy vấn chỉ đọc xác nhận các số lượng Category/Brand/Product/UsageNeed/RecommendationRule trên database `NEXORA` vẫn là 10/2/11/8/86; không chạy test ghi trên database đó.

**Viewport/browser:** lượt follow-up này không chạy browser thật vì công cụ hiện hành đã từ chối URL local và không được phép dùng bề mặt khác để vượt rào đó. Các viewport 1440×900, 768×1024 và 390×844 chưa được xác minh lại sau chỉnh sửa này; chưa có bằng chứng về tương tác sidebar/offcanvas, tràn ngang, console hoặc thao tác CRUD qua browser trong lượt này. Cần chủ dự án tự xác nhận các trang Admin ở ba kích thước và liên kết hồ sơ/đăng xuất trong phiên thật. Không tuyên bố browser test thành công. Khi bắt đầu lượt này, cổng 5058 không có listener; app chỉ được khởi động ngắn hạn trên database test để chạy seed và đã dừng.

Không thay đổi authentication/authorization, schema của database `NEXORA` hoặc migration source; không tạo migration mới; không commit/push; Phase 4 chưa bắt đầu. Database test riêng đã được provision bằng migration hiện có để SQL integration tests có thể chạy an toàn.

## 6. UI Redesign final acceptance – 16/10/2026

Khi bắt đầu kiểm tra, không có listener trên các cổng NEXORA đã biết. Đã khởi động website Development tại `http://127.0.0.1:5058` để người dùng có thể nghiệm thu tiếp trong Chrome/Edge. Seeder báo thêm 0 Category, Brand, Specification, CategorySpecification, Product, ProductSpecification, UsageNeed và RecommendationRule; truy vấn SQL chỉ đọc sau khởi động vẫn cho 10 Category, 2 Brand, 11 Product, 8 UsageNeed và 86 RecommendationRule.

HTTP smoke trực tiếp trên website đang chạy: `/`, `/Catalog`, `/tu-van-san-pham` trả 200. Bootstrap CSS/JS, `site.css`, `admin.css`, jQuery, jQuery Validate và jQuery Validation Unobtrusive tại đúng đường dẫn có trong Layout đều trả 200. Khi chưa đăng nhập, `/Admin/Products`, `/Admin/Categories`, `/Admin/Brands`, `/Admin/Specifications`, `/Admin/Consultation/UsageNeeds`, `/Admin/Consultation/Rules` và `/account/profile` chuyển hướng 302 tới đăng nhập. Đây là HTTP smoke, không chứng minh giao diện đã hiển thị đúng trong browser.

Rà soát Razor/CSS không thấy sai Layout/ViewStart: Area Admin đặt `_AdminLayout`, Tag Helpers được import tại `Areas/_ViewImports.cshtml`, Layout tham chiếu đúng file Bootstrap và stylesheet local. CSS có sidebar desktop từ breakpoint `lg`, offcanvas cho màn nhỏ; table được bọc `.table-responsive` với min-width theo loại bảng; topbar ẩn user chip ở mobile và co gọn tiêu đề. Không thấy lỗi cấu trúc cần sửa và không thay đổi source trong lần final acceptance này. `ConsultationController` chỉ cho role Admin; các action thay đổi/toggle là POST có antiforgery. SQL integration test trước đó đã kiểm tra service bật/tắt rule; chưa thao tác toggle qua trình duyệt thật trong lượt này.

Browser automation local tiếp tục không khả dụng theo giới hạn đã gặp ở lượt trước. Vì vậy chưa có xác nhận trực quan các trang Admin/UsageNeeds/Rules/Profile/tư vấn ở 1440×900, 768×1024 và 390×844; cũng chưa xác nhận Customer bị chặn qua phiên browser thật. Không đánh dấu các mục này đạt. Chủ dự án phản hồi chưa thể kiểm tra thủ công ở lượt này. Website vẫn đang lắng nghe tại `http://127.0.0.1:5058` để có thể tiếp tục khi thuận tiện. Giữ nguyên dữ liệu nghiệp vụ; không thay đổi schema/migration; chưa commit/push; Phase 4 chưa bắt đầu.

## 7. Góp ý menu trang chủ – 16/10/2026

Ảnh browser ở khung rộng 1242px cho thấy header chỉ hiện logo và hamburger. Nguyên nhân là `navbar-expand-xxl` chỉ mở menu từ 1400px, nên các liên kết Trang chủ, Sản phẩm, Tư vấn và mục theo role bị thu vào menu ở khung desktop này. Đổi sang `navbar-expand-xl` để các liên kết xuất hiện từ 1200px; breakpoint CSS cho phần menu thu gọn được khớp ở 1199.98px. Tablet/mobile vẫn dùng hamburger.

Danh mục và thẻ sản phẩm không bị thiếu khỏi trang chủ: `HomeController` tải chúng bằng `GetHomeAsync`; `Views/Home/Index.cshtml` hiển thị các nhóm sau hero và banner tư vấn. Ảnh được chụp ở đầu trang, trước khi cuộn tới các nhóm này. Thay đổi lần này chỉ xử lý điều hướng header, không đổi truy vấn hoặc dữ liệu.

`dotnet restore` thành công. Build `Debug` ban đầu không thể ghi đè output do tiến trình website đang dùng khóa `NEXORA.Web.exe`; build `Release` thành công với 0 warning/0 error. Test đầu tiên chạy từ tiến trình không kế thừa biến môi trường User nên bỏ qua 5 SQL test; sau khi đưa biến vào phạm vi tiến trình và xác minh mục tiêu riêng `PC01\SQLEXPRESS / NEXORA_IntegrationTests`, toàn bộ **99 test đạt, 0 lỗi, 0 bỏ qua**. Chưa có browser visual check sau chỉnh sửa; cần xác nhận menu thực tế ở 1242px, và hamburger tại các kích thước tablet/mobile.

## 8. Final UI polish trước nghiệm thu Phase 3 – 16/10/2026

### Catalog và ProductImage

Truy vấn chỉ đọc trên `PC01\SQLEXPRESS / NEXORA` xác nhận có 11 sản phẩm đang kinh doanh và cả 11 chưa có bản ghi trong `product_images` (tổng ProductImage = 0). Tên là các bản demo NEXORA Alpha/Beta/Gamma, không có ảnh sản phẩm chính thức để xác thực; không tải ảnh không rõ nguồn hoặc gắn ảnh mạng như ảnh thật. Thêm partial Razor `_ProductImage.cshtml` dùng chung cho thẻ Catalog, chi tiết, so sánh và kết quả tư vấn. Khi không có ảnh, partial hiển thị minh họa placeholder chung, có nhãn rõ “Hình ảnh đang được cập nhật”; ảnh được giữ tỷ lệ và dùng `object-fit: contain`. Query Catalog, Comparison và Recommendation ưu tiên ảnh đại diện, sau đó dùng ảnh phụ có thứ tự nếu ảnh đại diện chưa có; nếu không có ảnh nào thì dùng placeholder.

Thẻ sản phẩm hiển thị tối đa ba Specification đang bật được chiếu trực tiếp từ dữ liệu `ProductSpecification`; không hardcode ảnh hoặc tên thông số. Giá hiệu lực, tên, mã, danh mục và thương hiệu được căn chỉnh nhất quán. Nút chi tiết và vùng chọn so sánh hiện có vẫn giữ nguyên; các bộ lọc động không đổi.

### Tư vấn bằng tiếng Việt

Giữ nguyên parser, truy vấn ứng viên, điều kiện, trọng số, điểm xếp hạng và các Use Case. Giảm chiều cao Hero/form, làm nổi nút tư vấn, thêm trạng thái loading có `aria-live`, hỗ trợ gợi ý nhanh với trạng thái `aria-pressed`, và chia kết quả thành nhu cầu nhận diện, điều kiện bắt buộc, tiêu chí ưu tiên, sản phẩm, lý do và giới hạn theo output thật. Clarification, không có kết quả và lỗi input có trạng thái riêng. Không chèn kết quả hoặc thông tin sản phẩm giả vào runtime. Kết quả đề xuất dùng cùng partial ảnh như Catalog.

### Admin Dashboard

Thêm `IAdminDashboardQueryService` và triển khai SQL-backed qua EF Core cho năm số: tổng sản phẩm, sản phẩm đang kinh doanh, tổng danh mục, UsageNeed và RecommendationRule. Dashboard không hiển thị doanh thu, đơn hàng hoặc tồn kho. SQL chỉ đọc trên database chính tại lượt kiểm tra trả về lần lượt **11, 11, 10, 8, 86**; integration test SQL riêng kiểm tra các kết quả service với chính các bảng của `NEXORA_IntegrationTests`. Các card điều hướng module, sidebar và topbar được giữ nguyên.

### Mascot

Tìm kiếm source ngoài `wwwroot/lib` không thấy mascot, floating widget hoặc chat launcher của NEXORA. Source chỉ có dấu N thương hiệu/trang trí; không thêm CSS hoặc JavaScript để tác động thành phần không thuộc website.

### Kiểm thử và giới hạn nghiệm thu

- `dotnet restore NEXORA.sln`: thành công.
- `dotnet build NEXORA.sln --no-restore`: thành công, 0 warning, 0 error.
- `dotnet test NEXORA.sln --no-restore --no-build`: **100 passed, 0 failed, 0 skipped**. Đã xác minh biến SQL test trỏ đúng `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; có thêm SQL Server integration test cho truy vấn metric dashboard.
- HTTP smoke trên website Development tại `http://127.0.0.1:5058`: Home, Catalog, Product Detail, Comparison và `/tu-van-san-pham` trả 200. Anonymous `/workspace/Admin` và `/Admin/Products` chuyển 302 như chính sách xác thực hiện tại. `site.css`, `admin.css`, Bootstrap CSS/JS, jQuery và hai thư viện jQuery Validation trả 200. SQL log của query Catalog cho thấy EF sinh truy vấn correlated `TOP(1)` chọn ảnh cùng truy vấn collection có `ROW_NUMBER()` giới hạn ba Specification.
- Chưa thực hiện browser trực quan sau polish. Các kích thước 1440×900, 768×1024, 390×844, loading qua thao tác người dùng, menu mobile, console/network, màn kết quả nhiều sản phẩm và layout Admin cần chủ dự án kiểm tra thủ công; không ghi nhận các mục này là đã đạt.
- Không đổi database schema, migration hoặc dữ liệu sản phẩm; không tạo migration, không commit/push; Phase 4 chưa bắt đầu. Website Development đang chạy tại `http://127.0.0.1:5058` để chủ dự án tự review.
