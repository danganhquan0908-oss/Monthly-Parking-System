# BIÊN BẢN THIẾT KẾ HỆ THỐNG VÀ YÊU CẦU NGHIỆP VỤ
*(Kết quả từ buổi họp Party Mode giữa PM, SA, QA và Điều phối viên)*

## 1. MỤC TIÊU DỰ ÁN
**Hệ thống Gửi Xe Theo Tháng Tại KTX (Monthly Parking System - MPS)** 
- **Đối tượng sử dụng:** Chỉ dành cho nội bộ (Ban quản sinh, Bảo vệ, Nhân viên KTX). Không có portal dành cho sinh viên.
- **Mục đích:** Số hóa "sổ cái" quản lý xe lưu trú dài hạn (theo tháng) tại KTX. Hỗ trợ tra cứu nhanh trạng thái hợp lệ của xe, cảnh báo hết hạn và gửi tin nhắn tự động nhắc nhở sinh viên.

## 2. DANH SÁCH EPIC & USER STORY
Dựa trên PRD, hệ thống được chia thành 3 Epic chính:

### Epic 1: Quản lý Đăng ký & Thông tin phương tiện
- **Story 1.1:** Là Quản sinh, tôi có thể tạo Hợp đồng gửi xe (`ParkingContracts`). Hệ thống tự động kiểm tra trùng biển số và đảm bảo mỗi sinh viên chỉ có 1 xe đang `Active`.
- **Story 1.2:** Là Quản sinh, tôi có thể cập nhật thông tin hợp đồng hiện hành. Hệ thống đồng bộ thông tin Sinh viên cho tất cả các hợp đồng liên quan.
- **Story 1.3:** Khi sinh viên đổi xe, tôi có thể cập nhật biển số mới. Hệ thống lưu vết lịch sử cũ/mới vào `VehicleChangeLogs` để phục vụ tra cứu.
- **Story 1.4:** Là Quản sinh, tôi có thể hủy sớm (`Cancel`) hợp đồng khi sinh viên ngừng gửi.

### Epic 2: Theo dõi Thời hạn & Dashboard UI
- **Story 2.1:** Là Quản sinh, tôi có thể xem Dashboard (Vue.js 3) với 3 thẻ thống kê (Quick Stats) tự động cập nhật: `Đang hoạt động`, `Sắp hết hạn (<= 3 ngày)`, và `Đã hết hạn`.
- **Story 2.2:** Tôi có thể xem danh sách hợp đồng (có phân trang) với huy hiệu trạng thái (Xanh/Vàng/Đỏ) để nhận diện nhanh. Hỗ trợ tra cứu nhanh theo biển số, tên, mã sinh viên.

### Epic 3: Hệ thống Thông báo Tự động (Background Services)
- **Story 3.1:** Hệ thống có `DailyScanWorker` chạy ngầm để tự động quét CSDL mỗi ngày, tìm xe tới hạn và đẩy vào hàng đợi (`NotificationQueue`).
- **Story 3.2:** `NotificationProcessor` đọc hàng đợi để gọi API gửi tin Zalo/SMS nhắc nhở sinh viên. Mọi kết quả gửi đều được lưu vào `NotificationLogs`.

## 3. KIẾN TRÚC KỸ THUẬT (SYSTEM ARCHITECTURE)
*Do System Architect (SA) đề xuất:*
- **Backend:** ASP.NET Core Web API, sử dụng kiến trúc Clean Architecture / N-Tier phân tách rõ tầng Business Logic và Data Access.
- **Database:** SQL Server LocalDB.
- **Cấu trúc Dữ liệu (5 bảng chính):**
  - `Students` (Thông tin sinh viên, SĐT được mã hóa)
  - `Vehicles` (Biển số xe)
  - `ParkingContracts` (Hợp đồng gói tháng, trạng thái)
  - `NotificationLogs` (Lịch sử gửi thông báo)
  - `VehicleChangeLogs` (Lịch sử đổi biển số)
- **Xử lý Bất đồng bộ (Background Tasks):** Sử dụng In-memory Channels cho Hàng đợi (Queue) tin nhắn để không block luồng xử lý chính.
- **Resilience (Kháng lỗi):** Tích hợp thư viện **Polly** cho worker gửi tin nhắn (Tự động Retry 3 lần, mỗi lần cách nhau 5 giây nếu API rớt mạng).
- **Data Integrity:** Sử dụng Database Transactions cho các luồng cập nhật phức tạp và áp dụng Unique Filtered Index dưới DB để khóa chặt nghiệp vụ "1 sinh viên - 1 xe Active".

## 4. QUẢN TRỊ RỦI RO & BẢO MẬT (QA NOTES)
*Do Quality Assurance (QA) đánh giá và yêu cầu:*
1. **Bảo mật Dữ liệu (Data Privacy):** Mã hóa toàn bộ Database (TDE). Đối với thông tin nhạy cảm của sinh viên (SĐT), áp dụng mã hóa Application-level (AES-256) và Data Masking (Che khuất số điện thoại đối với role Bảo vệ).
2. **Ngăn chặn DB Lock:** Background job không gây ảnh hưởng đến thao tác nghiệp vụ, sử dụng cơ chế Queue In-memory để tách biệt luồng quét DB (I/O) và luồng gọi API (Network).
3. **Audit Log chống gian lận:** Bảng `VehicleChangeLogs` và log gia hạn phải đảm bảo lưu vết chính xác để có căn cứ đối chiếu khi sinh viên đổi xe hoặc xảy ra khiếu nại.
4. **Bảo mật API (CORS):** Hiện tại cấu hình CORS `AllowAnyOrigin` phù hợp cho Local/Dev, nhưng khi lên Production bắt buộc phải config lại Domain Allowlist để chặn gọi API trái phép từ bên thứ ba.

## 5. PHỤ LỤC EPIC 5 — HOST TẬP TRUNG, NHIỀU TRƯỜNG

Epic 5 mở rộng sản phẩm thành ứng dụng/API host tập trung phục vụ nhiều trường. Mỗi trường là một tenant độc lập; nhân viên đăng nhập bằng mã trường, tên đăng nhập và mật khẩu. Quản trị viên nền tảng tạo hoặc tạm ngưng trường qua khóa vận hành riêng, đồng thời cấp lời mời một lần cho Quản sinh đầu tiên. Quản sinh quản lý tài khoản nhân viên trong phạm vi trường của mình.

- Mã trường, tên đăng nhập nhân viên, sinh viên, biển số và hợp đồng được duy nhất trong phạm vi trường phù hợp với nghiệp vụ.
- API xác định `SchoolId` từ JWT đã xác thực; không dùng `SchoolId` từ request do trình duyệt gửi làm quyền truy cập.
- Mọi danh sách, thống kê, tra cứu theo ID, lịch sử, log và thao tác nhân viên đều bị giới hạn theo trường; khóa ngoại ghép giữ cho dữ liệu liên quan không thể liên kết chéo tenant.
- Lời mời chứa token ngẫu nhiên dùng một lần, lưu dạng hash và có thời hạn. Tài khoản Quản sinh đầu tiên được tạo bằng lời mời, không dùng chung bootstrap secret.
- Dữ liệu single-school hiện có được backfill vào tenant `MPS-DEFAULT`; không xóa hợp đồng, tài khoản hoặc log.
- Host tập trung production dùng SQL Server phù hợp production, HTTPS, key/secrets store, backup/khôi phục đã diễn tập và CORS allowlist. LocalDB chỉ dùng phát triển.

Quản lý nền tảng vẫn có thể tạo tenant bằng khóa vận hành nội bộ. Giao diện đăng ký trường tự phục vụ bổ sung ở Epic 5.1 để trường tự tạo tenant và tài khoản Admin đầu tiên sau khi xác minh số điện thoại bằng OTP; khóa vận hành không được gửi cho trường. Gửi OTP cần cấu hình SMS gateway. Email delivery, SSO, billing và database riêng cho từng trường vẫn là các phần mở rộng sau.

### Epic 5.1: Tự đăng ký trường
- Người quản lý nhập tên/mã trường, số điện thoại, tên đăng nhập và mật khẩu tại `/register`.
- API gửi OTP SMS có hiệu lực 5 phút; tối đa 5 lần nhập và giới hạn số lần yêu cầu mã theo số điện thoại/IP.
- Xác minh thành công sẽ tạo tenant và tài khoản `Admin` đầu tiên trong một transaction. Số điện thoại lưu mã hóa AES-GCM; OTP và thông tin chống giới hạn được lưu an toàn.
- API tự đăng ký không yêu cầu khóa vận hành. Khóa nền tảng chỉ dùng cho quản trị vận hành phía server.
- OTP xác minh quyền truy cập số điện thoại, không tự xác nhận thẩm quyền đại diện trường. Khi cần kiểm soát tư cách tổ chức hoặc gói dịch vụ, cần bổ sung quy trình xác minh phù hợp.
