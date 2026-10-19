# NEXORA Phase 4 – Checklist nghiệm thu giao diện thực tế

**Ngày chuẩn bị:** 19/10/2026<br>
**Trạng thái:** **6/6 PASS — người dùng xác nhận đã kiểm thử trình duyệt ngày 19/10/2026.** Không phải kết quả browser automation do Codex thực hiện.<br>
**Website:** http://127.0.0.1:5059<br>
**Database được cấu hình cho phiên Development:** chỉ `PC01\SQLEXPRESS / NEXORA_IntegrationTests`.

> Chỉ sử dụng dữ liệu và thông tin giả lập trong phiên nghiệm thu này. Không nhập địa chỉ giao hàng hoặc thông tin cá nhân thật. Website ở phiên chuẩn bị được cấu hình tới `NEXORA_IntegrationTests`; không đổi sang database `NEXORA` chính. Các số liệu dưới đây là snapshot trước Manual UAT; người dùng có thể đã thay đổi Cart/Order/Promotion khi kiểm tra.

## Kết quả Manual UAT được xác nhận

Ngày 19/10/2026, chủ dự án xác nhận đã trực tiếp kiểm tra trên trình duyệt và PASS cả sáu nhóm sau:

| # | Nhóm kiểm thử | Kết quả | Nguồn |
|---:|---|---|---|
| 1 | Catalog và hiển thị tồn kho | **PASS** | Xác nhận của người dùng |
| 2 | Giỏ hàng | **PASS** | Xác nhận của người dùng |
| 3 | Checkout | **PASS** | Xác nhận của người dùng |
| 4 | Danh sách, chi tiết và hủy đơn | **PASS** | Xác nhận của người dùng |
| 5 | Admin Promotions | **PASS** | Xác nhận của người dùng |
| 6 | Responsive desktop/tablet/mobile | **PASS** | Xác nhận của người dùng |

**Tổng: 6/6 PASS.** Người dùng không gửi ảnh minh chứng cùng xác nhận; Codex không tạo ảnh thay thế. Các bước chi tiết A–J bên dưới là checklist/rubric nghiệm thu, không phải bản ghi tự động hóa trình duyệt của Codex.

## Tài khoản và dữ liệu QA hiện có

| Mục | Giá trị đã xác minh |
|---|---|
| Customer QA | User ID `81`, `qa.customer.714c22496d@example.test`, role `Customer` |
| Admin QA | User ID `82`, `qa.admin.714c22496d@example.test`, role `Customer`, `Admin` |
| Customer phụ để kiểm tra quyền sở hữu | User ID `83`, `qa.other.714c22496d@example.test`, role `Customer` |
| Giỏ QA của Customer 81 | Cart ID `79`, hiện có 0 dòng hàng |
| Đơn QA đã có | Order ID `19`, thuộc Customer 81, trạng thái `CANCELLED` |
| Khuyến mại phần trăm | ID `81`, `PERCENTAGE`, giảm 10%, hiện tắt; thời gian 18–21/10/2026 |
| Khuyến mại số tiền cố định | ID `82`, `FIXED_AMOUNT`, giảm 750.000 ₫, hiện tắt; thời gian 18–21/10/2026 |
| Sản phẩm demo | 11 SKU; mỗi SKU có inventory `QuantityInStock=10`, `ReservedQuantity=0` |
| Dữ liệu thông số | 11 Specification, 18 CategorySpecification, 52 ProductSpecification |
| Fixture hết hàng chỉ dùng QA | Product ID `149`, SKU `NEXORA-QA-OOS-20261019`, trạng thái `ACTIVE`, tồn `0`, giữ `0`, có thể bán `0` |

Mật khẩu hiện tại của các tài khoản QA không được truy xuất. Hãy tự đặt mật khẩu trên trang web: mở `/account/forgotpassword`, nhập email QA chính xác, gửi liên kết, sau đó mở `/dev/mailbox` trong cùng phiên Development và bấm liên kết đặt lại mật khẩu vừa nhận. Chỉ nhập mật khẩu vào form cục bộ của NEXORA; không gửi mật khẩu qua chat. Thực hiện riêng cho Customer và Admin nếu chưa biết mật khẩu của cả hai.

Fixture hết hàng được tạo qua `ProductManagementService` trong database QA riêng. Không thay đổi tồn kho của 11 SKU demo. Không tạo đơn PENDING sẵn; hãy tạo đơn trong bước Checkout rồi hủy trong bước H để kiểm tra reservation/release thực tế.

## Cách đọc kết quả và ghi lỗi

Trạng thái 6 nhóm ở đầu tài liệu được ghi theo xác nhận của người dùng. Các mục A–J dưới đây liên kết nhóm đó với các bước kiểm tra chi tiết; không xem đây là log do Codex thu thập. Nếu chạy lại UAT, hãy ghi kết quả mới và ngày chạy, không sửa nguồn xác nhận cũ.

- `[ ] PASS` – đạt kết quả mong đợi.
- `[ ] FAIL` – có lỗi; ghi bước, URL, nội dung lỗi và ảnh minh chứng.
- `[ ] NOT RUN` – chưa thực hiện.

Ảnh minh chứng đề xuất lưu ngoài source/database trong `docs/evidence/phase4-uat/`, đặt tên theo mã trường hợp, ví dụ `C-add-to-cart.png`. Không đưa mật khẩu, token hoặc thông tin cá nhân thật vào ảnh.

---

## A. Đăng nhập Customer QA

**Trạng thái riêng:** Không được người dùng chấm tách riêng trong xác nhận 6 nhóm; đăng nhập là tiền điều kiện của các luồng Customer/Admin đã PASS.<br>
**Tài khoản:** User 81 – `qa.customer.714c22496d@example.test`<br>
**Lỗi được báo cáo:** Không có lỗi trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không có ảnh kèm xác nhận; Codex không tạo ảnh thay thế.

1. Mở http://127.0.0.1:5059/account/login.
2. Đăng nhập bằng email Customer QA và mật khẩu QA do bạn tự đặt qua luồng quên mật khẩu nếu cần.
3. Kiểm tra header thể hiện trạng thái đăng nhập và có liên kết giỏ hàng, tài khoản.
4. Xác nhận Customer không thấy liên kết/menu quản trị.
5. Thử mở trực tiếp `/Admin/Promotions`; Customer phải bị chuyển tới trang từ chối quyền hoặc yêu cầu đăng nhập phù hợp, không xem được dữ liệu Admin.
6. Đăng xuất và xác nhận trở về trạng thái khách chưa đăng nhập.

**Mong đợi:** Customer đăng nhập được; không truy cập được Admin; đăng xuất xóa phiên xác thực. Xác nhận 6 nhóm không cung cấp kết quả login/logout tách riêng.

## B. Catalog, tồn kho và thông số kỹ thuật

**Trạng thái:** **PASS — người dùng xác nhận nhóm 1: Catalog và hiển thị tồn kho (19/10/2026).**
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Đăng nhập Customer QA, mở `/Catalog`.
2. Tìm SKU `NXD-LAP-001`; xác nhận sản phẩm demo có hàng. Trước khi đặt hàng, inventory QA đã được xác minh là `10 / 0 / 10` (tồn / đang giữ / có thể bán).
3. Mở chi tiết sản phẩm và xác nhận các thông số kỹ thuật như CPU, RAM, SSD và trọng lượng được hiển thị.
4. Tìm SKU `NEXORA-QA-OOS-20261019` (Product ID 149). Xác nhận Catalog hiển thị trạng thái hết hàng/không thể thêm vào giỏ theo giao diện hiện tại.
5. Không sửa tồn kho sản phẩm demo để tạo trạng thái hết hàng.

**Mong đợi:** Sản phẩm còn hàng và fixture hết hàng phân biệt rõ; thông số đọc từ dữ liệu QA. Việc tồn kho 10/0 cho sản phẩm demo và 0/0 cho fixture đã xác minh bằng truy vấn chỉ đọc trước nghiệm thu.

## C. Thêm sản phẩm vào Cart

**Trạng thái:** **PASS — người dùng xác nhận nhóm 2: Giỏ hàng (19/10/2026).**
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Từ Catalog, trên SKU `NXD-LAP-001`, nhấn **Thêm vào giỏ hàng**.
2. Xác nhận thao tác không điều hướng khỏi Catalog, không tải lại toàn trang và không làm mất vị trí cuộn.
3. Kiểm tra thông báo thành công và badge giỏ hàng tăng đúng.
4. Thử thêm fixture hết hàng Product 149 nếu giao diện cho phép bấm; server phải từ chối, thông báo lỗi hiển thị, badge không tăng.

**Mong đợi:** CartService/server là nguồn xác thực; lỗi tồn kho không tạo dòng hàng giả.

## D. Cập nhật số lượng trong Cart

**Trạng thái:** **PASS — người dùng xác nhận nhóm 2: Giỏ hàng (19/10/2026).**
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Mở `/Cart` và xác nhận sản phẩm vừa thêm.
2. Nhấn `+`; xác nhận quantity, thành tiền, tạm tính, khuyến mại, tổng tiền và badge được cập nhật mà không reload.
3. Nhấn `−`; xác nhận số lượng không xuống dưới 1 và tổng được cập nhật.
4. Nhập trực tiếp một quantity hợp lệ, dừng nhập; xác nhận tự cập nhật sau debounce.
5. Thử quantity vượt khả năng tồn kho; server phải từ chối, giữ quantity hợp lệ trước đó và hiển thị lỗi.

**Mong đợi:** Giá/khuyến mại/tổng do server trả; không có giá trị tính giả ở client.

## E. Xóa sản phẩm và trạng thái Cart rỗng

**Trạng thái:** **PASS — người dùng xác nhận nhóm 2: Giỏ hàng (19/10/2026).**
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Nhấn **Xóa** trên một dòng hàng.
2. Xác nhận dòng biến mất không reload; tạm tính/khuyến mại/tổng/badge cập nhật.
3. Xóa dòng cuối cùng.
4. Xác nhận trang chuyển sang trạng thái **Giỏ hàng đang trống** mà không reload toàn trang.

**Mong đợi:** Không còn dòng hàng; badge/tổng bằng giá trị phù hợp với giỏ rỗng.

## F. Checkout và tạo đơn QA

**Trạng thái:** **PASS — người dùng xác nhận nhóm 3: Checkout (19/10/2026).**
**Order ID mới:** Không được nêu trong xác nhận.<br>
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Thêm một sản phẩm còn hàng vào Cart; không dùng fixture hết hàng.
2. Mở Cart và nhấn **Tiến hành thanh toán**.
3. Nhập thông tin giao hàng giả lập, ví dụ người nhận `NEXORA QA`, điện thoại `0900000000`, địa chỉ `QA local test – không giao hàng thật`.
4. Xác nhận tổng tiền ở Checkout khớp Cart phía server và gửi đơn một lần.
5. Ghi lại Order ID; xác nhận đơn ở trạng thái `PENDING` và Cart được xử lý đúng.
6. Không chạy lại submit nhiều lần để tránh tạo đơn trùng; có thể ghi nhận mã request/idempotency nếu UI hiển thị.

**Mong đợi:** Đơn QA PENDING được tạo trên `NEXORA_IntegrationTests`; tồn được reserve đúng; không phát sinh đơn trên database chính.

## G. Danh sách và chi tiết đơn hàng

**Trạng thái:** **PASS — người dùng xác nhận nhóm 4: Danh sách, chi tiết và hủy đơn (19/10/2026).**
**Order ID:** Không được nêu trong xác nhận.<br>
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Mở **Đơn hàng của tôi** (route `/Orders`).
2. Tìm đơn vừa tạo ở bước F và mở trang chi tiết.
3. Kiểm tra mã đơn, trạng thái PENDING, sản phẩm, quantity, giá snapshot và tổng.
4. Nếu có thể, thử URL của một đơn không thuộc tài khoản hiện tại; hệ thống không được tiết lộ dữ liệu Customer khác.

**Mong đợi:** Chỉ hiện đơn của Customer đang đăng nhập; giá snapshot không đổi theo giá sản phẩm hiện tại.

## H. Hủy đơn PENDING

**Trạng thái:** **PASS — người dùng xác nhận nhóm 4: Danh sách, chi tiết và hủy đơn (19/10/2026).**
**Order ID đã hủy:** Không được nêu trong xác nhận.<br>
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Từ chi tiết đơn PENDING do bước F tạo, chọn **Hủy đơn** và xác nhận.
2. Kiểm tra trạng thái chuyển sang `CANCELLED`.
3. Quay lại Catalog/Cart hoặc đơn hàng để xác nhận sản phẩm có thể mua lại nếu còn tồn thực tế.
4. Không hủy/sửa đơn hoặc tồn kho nào ngoài đơn QA vừa tạo.

**Mong đợi:** Reservation được giải phóng đúng một lần; không âm tồn/giữ; đơn cũ ID 19 vốn đã CANCELLED không bị thay đổi.

## I. Đăng nhập Admin và quản lý Promotions

**Trạng thái:** **PASS — người dùng xác nhận nhóm 5: Admin Promotions (19/10/2026).**
**Tài khoản:** User 82 – `qa.admin.714c22496d@example.test` (role Customer + Admin)<br>
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Đăng xuất Customer rồi đăng nhập bằng Admin QA; nếu chưa biết mật khẩu, tự đặt qua `/account/forgotpassword` và `/dev/mailbox` như phần trên.
2. Mở `/Admin/Promotions`; xác nhận Admin vào được trang và menu Admin hiển thị.
3. Kiểm tra Promotion ID 81 (PERCENTAGE 10%) và ID 82 (FIXED_AMOUNT 750.000 ₫). Cả hai hiện đang tắt, thời hạn đến 21/10/2026.
4. Mở từng promotion, kiểm tra chỉnh sửa/bật/tắt và lưu. Thử từng chương trình riêng lẻ; không bật đồng thời để tránh nhầm quy tắc áp dụng mức giảm tốt nhất, không cộng dồn.
5. Thử dữ liệu không hợp lệ nếu phù hợp: phần trăm ngoài 0–100, giá trị âm, thời gian kết thúc trước bắt đầu. Xác nhận validation rõ và không lưu giá trị sai.
6. Kiểm tra Customer không thể truy cập `/Admin/Promotions` trực tiếp.

**Mong đợi:** Admin thao tác được; Customer bị từ chối; promotions test chỉ thay đổi trong database QA. Nếu chỉnh trạng thái/giá trị, ghi lại thay đổi để có thể khôi phục thủ công sau UAT.

## J. Responsive trên Chrome DevTools

**Trạng thái:** **PASS — người dùng xác nhận nhóm 6: Responsive desktop/tablet/mobile (19/10/2026).**
**Trang/viewport lỗi:** Không có lỗi được báo cáo; kích thước cụ thể không nêu trong xác nhận.<br>
**Chi tiết lỗi / URL / bước:** Không nêu riêng trong xác nhận của người dùng.<br>
**Ảnh minh chứng:** Không gửi kèm xác nhận; Codex không tạo ảnh thay thế.

1. Mở Chrome tại http://127.0.0.1:5059 và mở DevTools bằng `F12` (hoặc `Ctrl+Shift+I`).
2. Bật Device Toolbar bằng `Ctrl+Shift+M`.
3. Chọn `Responsive`, nhập lần lượt:
   - Desktop: `1440 × 900`.
   - Tablet: `768 × 1024`.
   - Mobile: `390 × 844`.
4. Ở mỗi kích thước, kiểm tra Catalog, Cart, Checkout, Orders và Admin Promotions:
   - Không tràn ngang hoặc chồng chữ.
   - Menu mobile mở/đóng được.
   - Bảng/biểu mẫu không bị cắt; nút đủ lớn để bấm.
   - Ảnh giữ tỷ lệ; thông báo không che thao tác.
5. Mở tab Console và Network; ghi nhận lỗi JavaScript, request CSS/JS lỗi hoặc status không phải 200.

**Mong đợi:** Các trang sử dụng được ở cả ba kích thước; ghi riêng lỗi từng route/viewport thay vì đánh dấu chung.

---

## Kết quả kiểm thử theo loại

- **Automated:** lần chạy gần nhất được xác nhận trước khi chốt tài liệu đạt **143 passed, 0 failed, 0 skipped**; build thành công. Bộ kiểm thử không chạy lại trong lượt này vì chỉ cập nhật tài liệu.
- **HTTP E2E:** **63 assertions PASS** theo báo cáo kiểm thử trước đó.
- **SQL Server integration:** chạy trên `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; bao gồm transaction, concurrency, idempotency, reservation, snapshot và cancellation. Không chạy lại trong lượt chốt tài liệu.
- **HTTP smoke trong lượt chuẩn bị website:** các route công khai `/`, `/Catalog`, chi tiết/so sánh, `/tu-van-san-pham`, đăng nhập/đăng ký/khôi phục, Development mailbox và static CSS/Bootstrap trả HTTP 200. Đây không phải browser UI test.
- **Manual browser:** 6/6 nhóm PASS theo xác nhận trực tiếp của người dùng ngày 19/10/2026. Codex không chạy browser automation. Không có ảnh minh chứng đính kèm.
- **Git/schema:** `git diff --check` không báo whitespace error (chỉ có cảnh báo chuẩn hóa LF/CRLF). Migration Phase 4 duy nhất là `20261016133620_AddCheckoutRequestId`, đã được duyệt; không tạo migration trong quá trình nghiệm thu. Website được cấu hình tới test DB; việc xác minh schema cuối kỳ chỉ dùng SELECT chỉ đọc trên hai database. Không ghi dữ liệu vào `NEXORA` chính.
- Các QA record không bị dọn sau UAT. Dữ liệu trong test DB có thể đã được người dùng thay đổi khi kiểm thử; không giả định Cart, Order hoặc Promotions còn đúng snapshot lúc chuẩn bị.
- Không commit hoặc push.
