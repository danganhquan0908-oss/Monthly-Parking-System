# TỔNG KẾT TRIỂN KHAI HỆ THỐNG MPS

**Dự án:** Monthly Parking System — quản lý gửi xe tháng tại ký túc xá
**Cập nhật:** 03/10/2026
**Phạm vi báo cáo:** các hạng mục hiện có trong workspace; trạng thái chạy được kiểm tra cục bộ trong ngày cập nhật.

## 1. Mục tiêu và phạm vi

MPS số hóa việc đăng ký, theo dõi và cập nhật hợp đồng gửi xe tháng của sinh viên nội trú. Người dùng nghiệp vụ chính là Quản sinh/Quản lý KTX và nhân viên được phân quyền. Hệ thống được mở rộng theo hướng một máy chủ tập trung, phục vụ nhiều trường với dữ liệu từng trường được cô lập theo tenant.

Thanh toán trực tuyến và cổng tự phục vụ cho sinh viên chưa nằm trong phạm vi triển khai hiện tại. Nhắc hạn qua email dùng chung cấu hình SMTP với OTP đăng ký trường.

## 2. Các hạng mục đã triển khai

### Epic 1 — Hợp đồng và phương tiện

- Tạo, xem, cập nhật và hủy hợp đồng gửi xe.
- Đổi biển số trên hợp đồng còn hiệu lực và lưu lịch sử biển số cũ/mới, người thực hiện, ghi chú và thời điểm.
- Đồng bộ thông tin sinh viên dùng chung giữa các hợp đồng của cùng sinh viên trong cùng trường.
- Kiểm tra ngày hợp đồng, trùng biển số và hợp đồng đang hoạt động; các thao tác tạo/cập nhật trọng yếu dùng transaction.
- API chính: `POST /api/v1/contracts`, `GET /api/v1/contracts/{id}`, `PUT /api/v1/contracts/{id}`, `PUT /api/v1/contracts/{id}/change-vehicle`, `GET /api/v1/contracts/{id}/vehicle-history`, `PUT /api/v1/contracts/{id}/cancel`.

### Epic 2 — Dashboard và tra cứu

- Giao diện web dùng Vue 3, hiển thị danh sách hợp đồng và thống kê đang hoạt động, sắp hết hạn trong 0–3 ngày, đã hết hạn.
- Có tìm kiếm theo biển số, tên sinh viên, mã sinh viên hoặc phòng; danh sách hỗ trợ phân trang và huy hiệu trạng thái.
- API thống kê và danh sách: `GET /api/v1/contracts/stats` và `GET /api/v1/contracts/expiring`.

### Epic 3 — Quét hạn và thông báo

- `DailyScanWorker` quét khi ứng dụng khởi động rồi lặp lại mỗi 24 giờ; tự chuyển hợp đồng quá hạn sang `Expired`.
- Tạo thông báo nhắc trước hạn cho hợp đồng đang có hiệu lực, bắt đầu không muộn hơn hôm nay và hết hạn trong vòng 3 ngày.
- Lưu trạng thái gửi vào `NotificationLogs`, khử trùng theo hợp đồng/ngày hết hạn; gửi email qua SMTP khi địa chỉ sinh viên và cấu hình gửi đã có.
- `NotificationProcessor` dùng Polly thử lại lỗi SMTP tạm thời tối đa 3 lần, cách nhau 5 giây; nhật ký lưu địa chỉ email đã che.

### Epic 4 — Tài khoản nhân viên

- Đăng nhập bằng mã trường, tên đăng nhập và mật khẩu; mật khẩu được băm, JWT có thời hạn 60 phút.
- Vai trò: `Admin`, `Manager`, `Guard`, `Staff`. Quyền sửa hợp đồng, tạo nhân viên và xem dữ liệu được giới hạn theo vai trò.
- Hỗ trợ mời nhân viên bằng liên kết dùng một lần, khóa/mở tài khoản và nhật ký thao tác tài khoản.
- Khóa đăng nhập 15 phút sau 5 lần nhập sai liên tiếp. Tài khoản bị vô hiệu hóa hoặc trường bị tạm ngưng sẽ làm token hiện có mất hiệu lực ở yêu cầu tiếp theo.
- Số điện thoại sinh viên được mã hóa AES-GCM; vai trò không có quyền phù hợp chỉ nhận số đã che.
- Email sinh viên bắt buộc khi thêm/cập nhật hợp đồng; Admin/Manager xem đầy đủ, vai trò khác nhận địa chỉ đã che.

### Epic 5 — Máy chủ tập trung, nhiều trường

- Có bảng `Schools`; dữ liệu nhân viên, sinh viên, xe, hợp đồng và nhật ký gắn với `SchoolId`.
- API lấy danh tính trường từ JWT; truy vấn hợp đồng và dashboard giới hạn theo trường. Khóa ngoại ghép giúp ngăn liên kết dữ liệu chéo tenant.
- Có giao diện vận hành nền tảng tại `/platform` để tạo/tạm ngưng trường và quy trình cấp lời mời tài khoản đầu tiên bằng khóa vận hành phía máy chủ.
- Mã lời mời lưu dạng hash, dùng một lần và có hạn dùng.

### Epic 5.1/5.2 — Tự đăng ký trường bằng email OTP

- Trang `/register` cho phép khai báo trường và tạo tài khoản `Admin` đầu tiên sau khi xác minh OTP qua email.
- OTP có hạn 5 phút, tối đa 5 lần nhập; có giới hạn số lần gửi theo email và IP. Việc tạo trường và tài khoản đầu tiên diễn ra trong một transaction.
- Email được gửi qua SMTP khi có cấu hình. Số điện thoại là tùy chọn, được mã hóa nhưng không được xác minh trong luồng đăng ký hiện hành.
- Đây là trạng thái triển khai hiện tại theo mã nguồn/hướng dẫn chạy. OTP đăng ký trường và nhắc hạn đều dùng email qua SMTP; không có luồng OTP SMS trong bản hiện tại.

## 3. Cơ sở dữ liệu và cấu trúc dự án

Database hiện dùng SQL Server LocalDB khi chạy phát triển. Các bảng chính:

- Nghiệp vụ: `Students`, `Vehicles`, `ParkingContracts`, `VehicleChangeLogs`, `AuditLogs`, `NotificationLogs`.
- Tài khoản và đa trường: `Schools`, `StaffUsers`, `StaffInvitations`, `AccountAuditLogs`.
- Đăng ký trường: `SchoolRegistrationChallenges`.

Toàn vẹn dữ liệu được hỗ trợ bằng khóa ngoại, check constraint, chỉ mục duy nhất theo trường, chỉ mục lọc cho một hợp đồng hoạt động mỗi sinh viên và transaction ở các luồng quan trọng.

Các script database/migration trong workspace:

- `Init_Database.sql`
- `Migrate_Epic3_Notification_DueDate.sql`
- `Migrate_Epic4_StaffUsers.sql`
- `Migrate_Epic5_MultiTenant.sql`
- `Migrate_Epic5_1_SelfServiceRegistration.sql`
- `Migrate_Epic5_2_EmailRegistration.sql`
- `Migrate_Epic6_EmailNotifications.sql`

Hướng dẫn khởi tạo và nâng cấp database nằm trong `MonthlyParkingSystem.Api/README.md`. Không chạy toàn bộ migration cũ lên database mới nếu README hướng dẫn một quy trình khởi tạo riêng.

## 4. Cấu hình dịch vụ và bảo mật

- CORS mở mọi origin trong Development; môi trường khác cần khai báo `Cors:AllowedOrigins`.
- Production cần khóa JWT và khóa mã hóa điện thoại ổn định, lưu trong secret store và có quy trình backup khóa.
- Đăng ký trường cần SMTP: `Email__SmtpHost`, `Email__SmtpPort`, `Email__SmtpUsername`, `Email__SmtpPassword`, `Email__FromAddress`.
- Email OTP và thông báo hết hạn dùng `Email__SmtpHost`, `Email__SmtpPort`, `Email__SmtpUsername`, `Email__SmtpPassword`, `Email__FromAddress`.
- LocalDB không cung cấp TDE; tài liệu yêu cầu dùng SQL Server phù hợp production nếu cần lớp mã hóa database đó.

## 5. Trạng thái chạy hiện tại

Trong lần kiểm tra ngày 03/10/2026, backend đã được khởi động lại tại `http://localhost:5127` trong môi trường Development:

- `GET /` trả về HTTP 200 và phục vụ dashboard.
- `GET /api/v1/contracts/stats` khi chưa đăng nhập trả về HTTP 401; đây là kết quả phù hợp với API được bảo vệ bằng JWT.
- Log khởi động cho thấy worker truy cập được database LocalDB và hoàn tất lượt quét; hiện không có thông báo nào được đưa vào hàng đợi.
- Sau migration Epic 6, 5 hồ sơ sinh viên hiện có chưa có email; worker bỏ qua các hồ sơ này nên chưa gửi thư tự động.
- Backend đang chạy cục bộ, không phải một bản triển khai production trên máy chủ công khai.

## 6. Những phần chưa hoàn tất hoặc còn phụ thuộc cấu hình

- Chưa có triển khai production trên Azure hoặc máy chủ khác, tên miền/HTTPS production hay quy trình vận hành chính thức.
- SMTP đã được cấu hình cục bộ trong User Secrets; đã gửi email nhắc thử và người nhận xác nhận nhận được thư. Thông tin này chỉ xác nhận môi trường local.
- Sinh viên/hợp đồng cũ chưa có địa chỉ email. Quản lý cần bổ sung email qua chức năng cập nhật hợp đồng trước khi các hồ sơ đó được nhắc.
- Chưa có bằng chứng trong workspace về trường đang sử dụng MPS trong production.
- Đã thêm `MonthlyParkingSystem.Tests` với 18 test cho quy tắc ngày, chồng lấn, trạng thái, đổi/hủy xe, cửa sổ nhắc email và dịch biểu thức sang SQL Server SQL; chưa có integration test database, role/tenant isolation hoặc SMTP.
- Hợp đồng bắt đầu trong tương lai được lưu `Pending`; worker chuyển sang `Active` khi tới ngày bắt đầu. Production vẫn cần đặt rõ múi giờ nghiệp vụ thay vì phụ thuộc timezone của máy chủ.
- Cần bổ sung/đánh giá nhật ký trước-sau cho sửa đổi hợp đồng và người thực hiện, cùng quy trình retry thủ công khi thông báo thất bại.
- Cần diễn tập backup/restore, kiểm thử phân quyền và cách ly tenant, kiểm thử đồng thời khi trùng biển số, và đánh giá bảo mật trước khi dùng dữ liệu sinh viên thật.

## 7. Lệnh chạy cục bộ

Khởi chạy API từ thư mục workspace:

```powershell
dotnet run --project MonthlyParkingSystem.Api --launch-profile http
```

API phục vụ giao diện dashboard tại `http://localhost:5127/`. Nếu backend dừng, cần khởi động lại tiến trình rồi tải lại trang.

Chạy unit test bằng `dotnet test MPS.sln --configuration Release`. Các test hiện dùng quy tắc domain thuần, không kết nối database và không gửi email.

## 8. Tài liệu tham chiếu trong workspace

- `docs/System_Analysis_Design.md` — sơ đồ, ERD và ma trận truy vết yêu cầu.
- `docs/Production_Readiness_Checklist.md` — các bước vận hành còn lại trước production.
- `MonthlyParkingSystem.Api/README.md` — hướng dẫn chạy, API, migration và biến cấu hình.
- `MonthlyParkingSystem.Web/` — giao diện dashboard, đăng ký trường và vận hành nền tảng.
- `MonthlyParkingSystem.Api/` — backend ASP.NET Core, database context, API controllers và background services.
