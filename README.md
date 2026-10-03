# Monthly Parking System (MPS)

MPS là hệ thống quản lý hợp đồng gửi xe tháng tại ký túc xá. Ứng dụng hỗ trợ nhân sự nhà trường quản lý sinh viên, phương tiện, thời hạn hợp đồng và lịch sử thay đổi. Một backend tập trung có thể phục vụ nhiều trường; dữ liệu và tài khoản được phân tách theo trường.

## Tính năng

- Tạo, tra cứu, cập nhật và hủy hợp đồng gửi xe.
- Quản lý biển số xe và lưu lịch sử đổi xe, cập nhật, hủy hợp đồng.
- Dashboard thống kê hợp đồng đang hoạt động, sắp hết hạn và đã hết hạn; tìm kiếm và phân trang.
- Quét hạn tự động mỗi ngày và ghi nhật ký kết quả thông báo.
- Đăng nhập nhân viên theo trường với vai trò `Admin`, `Manager`, `Guard` và `Staff`.
- Mời, khóa và mở tài khoản nhân viên; giới hạn dữ liệu theo trường.
- Đăng ký trường và tạo Admin đầu tiên bằng email OTP khi SMTP đã được cấu hình.
- Mã hóa số điện thoại sinh viên ở tầng ứng dụng; che số điện thoại theo quyền người dùng.

MPS hiện là công cụ nội bộ cho nhân viên KTX, chưa có cổng sinh viên hay thanh toán trực tuyến. Email OTP và nhắc hạn SMS/Zalo cần cấu hình nhà cung cấp trước khi gửi thật.

## Công nghệ

- **Backend:** ASP.NET Core Web API trên .NET 10.
- **Database:** SQL Server; SQL Server LocalDB dùng cho phát triển trên Windows.
- **Frontend:** HTML/CSS và Vue 3; backend phục vụ giao diện từ cùng host.
- **Xử lý nền:** Background services, hàng đợi thông báo và Polly retry.

## Cấu trúc thư mục

```text
MonthlyParkingSystem.Api/   API, xác thực, truy cập dữ liệu và dịch vụ nền
MonthlyParkingSystem.Web/   Dashboard, đăng ký trường và trang vận hành nền tảng
Init_Database.sql           Khởi tạo schema MPS
Migrate_*.sql               Các script nâng cấp database theo Epic
System_Design_And_Requirements.md
PRODUCT REQUIREMENTS DOCUMENT.docx
Project_Summary.md
```

## Chạy trên máy Windows

### Yêu cầu

- .NET 10 SDK.
- SQL Server Express LocalDB và công cụ `sqlcmd`.
- PowerShell.

### 1. Tạo database mới

Mở PowerShell tại thư mục gốc repo và chạy:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Init_Database.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Migrate_Epic5_MultiTenant.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Migrate_Epic5_1_SelfServiceRegistration.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Migrate_Epic5_2_EmailRegistration.sql
```

Nếu nâng cấp database đã có dữ liệu, hãy sao lưu trước rồi áp dụng các migration theo thứ tự và hướng dẫn trong [README của API](MonthlyParkingSystem.Api/README.md). Không chạy quy trình khởi tạo database mới lên dữ liệu production.

### 2. Cấu hình khóa mã hóa số điện thoại

Khóa phải là Base64 của ít nhất 32 byte ngẫu nhiên và cần giữ ổn định qua các lần khởi động. Nếu mất hoặc thay khóa, các số điện thoại đã mã hóa có thể không đọc được. Ví dụ dưới đây tạo khóa dùng cho phiên PowerShell hiện tại; hãy lưu khóa an toàn và nạp lại cùng giá trị ở những lần chạy sau. Không commit khóa vào Git.

```powershell
$keyBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
$env:MPS_PHONE_ENCRYPTION_KEY = [Convert]::ToBase64String($keyBytes)
```

Không tạo khóa mới cho database đã có số điện thoại được mã hóa. Hướng dẫn cấu hình JWT, khóa vận hành nền tảng, SMTP và gateway thông báo nằm trong [README của API](MonthlyParkingSystem.Api/README.md).

### 3. Khởi chạy hệ thống

```powershell
dotnet run --project MonthlyParkingSystem.Api --launch-profile http
```

Mở dashboard tại [http://localhost:5127](http://localhost:5127). Trang đăng ký trường là [http://localhost:5127/register](http://localhost:5127/register); trang vận hành nền tảng là [http://localhost:5127/platform](http://localhost:5127/platform).

Hệ thống không tạo sẵn tài khoản đăng nhập mặc định. Tài khoản Admin đầu tiên được tạo qua đăng ký trường sau khi xác minh email OTP; luồng này cần SMTP hoạt động. Nhân viên tiếp theo được mời từ tài khoản có quyền phù hợp. API bảo vệ bằng bearer token; mỗi lần đăng nhập cần mã trường, tên đăng nhập và mật khẩu.

## Cấu hình dịch vụ ngoài

Các giá trị bí mật được cung cấp qua biến môi trường hoặc secret store, không lưu trực tiếp vào mã nguồn:

Để cấu hình Gmail SMTP cục bộ cho OTP đăng ký trường, chạy script sau trong PowerShell ở thư mục gốc. Script yêu cầu địa chỉ gửi và nhập app password ở chế độ ẩn, sau đó lưu vào .NET User Secrets ngoài repository:

```powershell
.\Configure-Smtp.ps1
```

Backend phải chạy ở môi trường `Development` để nạp User Secrets. Cấu hình SMTP này hiện phục vụ OTP đăng ký trường; thông báo sắp hết hạn vẫn cần HTTP gateway SMS/Zalo theo luồng hiện tại.

| Dịch vụ | Cấu hình chính | Mục đích |
| --- | --- | --- |
| Database | `ConnectionStrings__MpsDatabase` | Chuỗi kết nối SQL Server |
| Mã hóa số điện thoại | `MPS_PHONE_ENCRYPTION_KEY` | Khóa AES-GCM ổn định |
| JWT | `MPS_JWT_SIGNING_KEY` | Ký token đăng nhập |
| Vận hành nền tảng | `MPS_PLATFORM_PROVISIONING_KEY` | Tạo/tạm ngưng trường và cấp lời mời đầu tiên |
| Email | `Email__SmtpHost`, `Email__SmtpPort`, `Email__SmtpUsername`, `Email__SmtpPassword`, `Email__FromAddress` | Gửi OTP đăng ký trường |
| SMS/Zalo | `MPS_NOTIFICATION_ENDPOINT`, `MPS_NOTIFICATION_TOKEN`, `MPS_NOTIFICATION_CHANNEL` | Gửi nhắc hạn qua HTTP gateway |
| CORS production | `Cors__AllowedOrigins` | Danh sách origin frontend được phép |

Trong Production, dùng SQL Server phù hợp môi trường triển khai, HTTPS, secret store, CORS allowlist và quy trình backup/khôi phục đã kiểm chứng. LocalDB và cấu hình CORS Development chỉ dành cho phát triển.

## API và tài liệu

- [Danh sách route, quyền truy cập và cấu hình backend](MonthlyParkingSystem.Api/README.md)
- [Thiết kế hệ thống và yêu cầu nghiệp vụ](System_Design_And_Requirements.md)
- [PRD](PRODUCT%20REQUIREMENTS%20DOCUMENT.docx)
- [Tổng kết các Epic đã triển khai](Project_Summary.md)

Các API nghiệp vụ nằm dưới `/api/v1`. Dashboard và API cùng host tại cổng `5127` khi chạy profile `http`. Các route hợp đồng và tài khoản yêu cầu JWT; route đăng ký trường là công khai nhưng chỉ hoàn tất khi cấu hình SMTP và xác minh OTP.

## Bảo mật dữ liệu

- Không commit mật khẩu, JWT/platform/encryption keys, SMTP credentials, token gateway, file `.env`, log backend hoặc database cục bộ.
- Giữ an toàn và sao lưu các khóa mã hóa; thay khóa cần kế hoạch chuyển đổi dữ liệu.
- Dữ liệu nghiệp vụ được giới hạn theo `SchoolId` lấy từ danh tính đã xác thực.
- Trước khi dùng dữ liệu sinh viên thật hoặc triển khai công khai, cần cấu hình secrets/HTTPS/CORS và kiểm tra backup, phục hồi, quyền truy cập và cách ly tenant.
