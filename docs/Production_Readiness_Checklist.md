# Checklist đưa MPS lên production

**Trạng thái hiện tại:** Chưa đủ điều kiện deploy production. MPS hiện dùng SQL Server LocalDB trên máy phát triển; cần có SQL Server mà máy chủ ứng dụng truy cập được trước khi tạo môi trường production. File `.env.example` chỉ là mẫu tên biến, không phải cấu hình thật.

## 1. Hạ tầng bắt buộc

- [ ] Cấp SQL Server production có kết nối TLS, tài khoản database quyền tối thiểu và dung lượng phù hợp.
- [ ] Chốt domain, HTTPS certificate và origin frontend; đặt `Cors__AllowedOrigins__0` thành origin thật.
- [ ] Chạy một instance MPS trong giai đoạn đầu. `NotificationQueue` dùng Channels trong bộ nhớ nên chưa thể scale ngang an toàn.
- [ ] Mở health check `GET /health/live` cho liveness và `GET /health/ready` cho readiness. Readiness phải trả HTTP 503 nếu không đọc được bảng `Schools`.
- [ ] Cấu hình cổng từ `ASPNETCORE_URLS`; container hiện lắng nghe `0.0.0.0:8080`.

## 2. Bí mật và tích hợp

- [ ] Đưa `ConnectionStrings__MpsDatabase`, `MPS_JWT_SIGNING_KEY` và `MPS_PHONE_ENCRYPTION_KEY` vào secret manager của nhà cung cấp.
- [ ] Tạo khóa JWT và khóa điện thoại độc lập, ngẫu nhiên, Base64 tối thiểu 32 byte; sao lưu khóa điện thoại an toàn. Không thay khóa trên database đã có dữ liệu mã hóa nếu chưa có kế hoạch re-encrypt.
- [ ] Chỉ đặt `MPS_PLATFORM_PROVISIONING_KEY` nếu đội vận hành dùng endpoint nền tảng. Không gửi khóa này cho trường.
- [ ] Cấu hình SMTP với sender đã xác minh. Gửi và xác nhận một OTP thử cùng một email nhắc thử trước khi mở đăng ký.
- [ ] Không chuyển bí mật vào Git, Docker image, build log hoặc tài liệu. `.env.example` không chứa giá trị thật; `.env` bị ignore.

## 3. Database và dữ liệu

- [ ] Chọn quy trình khởi tạo mới hoặc nâng cấp database có dữ liệu. Không chạy script khởi tạo database mới lên production hiện hữu.
- [ ] Backup database trước mỗi migration; rà lại thứ tự script trong `README.md` và API README theo đúng trạng thái database.
- [ ] Xác nhận số trường, nhân viên, sinh viên, xe và hợp đồng sau migration; kiểm tra khóa ghép và unique/filtered index.
- [ ] Bổ sung email cho hồ sơ sinh viên cũ trước khi kỳ vọng các hồ sơ đó nhận nhắc hạn.
- [ ] Diễn tập restore vào một database riêng và ghi lại thời gian phục hồi cùng dữ liệu có thể mất.

## 4. Kiểm thử trước mở truy cập

- [x] Unit test quy tắc ngày, khoảng hợp đồng, trạng thái, đổi/hủy xe, cửa sổ nhắc và SQL translation: 18 test hiện chạy thành công.
- [ ] Integration test bằng SQL Server cho unique index, transaction và hai yêu cầu cạnh tranh cùng biển số/khoảng hợp đồng.
- [ ] Kiểm tra role matrix và tenant isolation cho dashboard, hợp đồng, lịch sử, tài khoản, OTP và platform endpoints.
- [ ] Kiểm tra OTP sai/hết hạn/rate-limit; SMTP thiếu cấu hình; timeout/retry; log Failed và email được che.
- [ ] Thử khôi phục backup, đổi khóa theo quy trình và xác nhận hệ thống cảnh báo khi readiness thất bại.
- [ ] Chạy thử trên môi trường staging với timezone `Asia/Ho_Chi_Minh`; kiểm tra các mốc StartDate, EndDate, nửa đêm và ngày nhuận.

## 5. Giám sát và vận hành

- [ ] Thu log có cấu trúc cho lỗi đăng nhập, worker và SMTP; không ghi mật khẩu, OTP, bearer token, email đầy đủ hoặc số điện thoại.
- [ ] Tạo cảnh báo cho health check 503, lỗi gửi email, hàng đợi Pending kéo dài và database gần đầy.
- [ ] Chốt người chịu trách nhiệm SMTP, xử lý email Failed và cấp/thu hồi khóa nền tảng.
- [ ] Chốt retention cho audit/notification logs, quyền truy cập bản backup, RPO/RTO và lịch diễn tập định kỳ.
- [ ] Trước khi chạy nhiều instance, thay Channels bằng queue/outbox có claim lease và idempotency hoặc cơ chế tương đương.

## 6. Trình tự phát hành an toàn

1. Khởi tạo staging database từ bản sao hoặc dữ liệu thử đã ẩn danh.
2. Nạp secrets staging và chạy migration đã rà soát.
3. Build image từ `Dockerfile`, đẩy lên staging và chờ `/health/ready` trả HTTP 200.
4. Chạy checklist test staging, kiểm tra log và gửi email thử.
5. Backup production, áp dụng migration đã duyệt, deploy image và chờ readiness.
6. Kiểm tra đăng nhập, tạo/sửa hợp đồng, cách ly tenant và email bằng tài khoản thử; theo dõi lỗi sau phát hành.
7. Nếu migration đã đổi schema, rollback ứng dụng không tự hoàn tác database. Dùng restore đã diễn tập hoặc migration tiến về trước đã được duyệt.

**Điều kiện dừng hiện tại:** Không tạo app hosting hoặc gửi dữ liệu sinh viên ra ngoài cho đến khi có SQL Server production và owner xác nhận vị trí lưu dữ liệu, backup, secrets và quyền truy cập.
