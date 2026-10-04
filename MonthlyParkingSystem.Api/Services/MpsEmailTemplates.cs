using System.Net;

namespace MonthlyParkingSystem.Api.Services;

/// <summary>
/// Mẫu email HTML thương hiệu chính thức cho hệ thống MPS (Monthly Parking System).
/// Tích hợp nhận diện logo The Fluid Ribbon qua Content-ID (cid:mps-logo) tương thích 100% với Gmail và Outlook.
/// </summary>
public static class MpsEmailTemplates
{
    public static string BuildOtpEmail(string otp, string? schoolName = null, string? username = null)
    {
        var escapedOtp = WebUtility.HtmlEncode(otp);
        var schoolInfo = string.IsNullOrWhiteSpace(schoolName)
            ? "trên hệ thống MPS"
            : $"trường <strong>{WebUtility.HtmlEncode(schoolName)}</strong>";
        var greeting = string.IsNullOrWhiteSpace(username)
            ? "Chào bạn,"
            : $"Chào <strong>{WebUtility.HtmlEncode(username)}</strong>,";

        var content =
            $"""
            <h2 style="margin:0 0 10px 0;font-size:21px;font-weight:750;color:#0f172a;letter-spacing:-0.4px;">Xác minh đăng ký tài khoản trường</h2>
            <p style="margin:0 0 20px 0;font-size:14px;line-height:1.6;color:#475569;">
              {greeting} Bạn vừa thực hiện yêu cầu khởi tạo không gian quản trị {schoolInfo}. Vui lòng nhập mã OTP dưới đây để hoàn tất quá trình kích hoạt:
            </p>

            <!-- Khối OTP nổi bật -->
            <div style="background:#f0f9ff;border:2px dashed #0284c7;border-radius:16px;padding:26px 16px;text-align:center;margin:24px 0;">
              <div style="font-size:11px;font-weight:700;color:#0369a1;text-transform:uppercase;letter-spacing:1.2px;margin-bottom:10px;">Mã xác thực một lần (OTP)</div>
              <div style="font-size:38px;font-weight:800;color:#0f172a;letter-spacing:12px;font-family:'SF Mono',Consolas,monospace;padding-left:12px;">{escapedOtp}</div>
              <div style="font-size:12px;color:#64748b;margin-top:12px;font-weight:500;">
                ⏱️ Mã có hiệu lực trong vòng <strong>5 phút</strong>
              </div>
            </div>

            <!-- Cảnh báo an toàn -->
            <div style="background:#f8fafc;border-left:4px solid #0284c7;border-radius:4px;padding:12px 16px;margin:22px 0;font-size:13px;color:#334155;line-height:1.6;">
              🔒 <strong>Lưu ý bảo mật:</strong> Tuyệt đối không chia sẻ mã này cho bất kỳ ai khác. Nếu bạn không thực hiện đăng ký trường trên MPS, vui lòng bỏ qua email này.
            </div>
            """;

        return WrapInLayout(content, "Mã xác minh đăng ký trường MPS");
    }

    public static string BuildContractExpiryEmail(
        string licensePlate,
        DateOnly dueDate,
        string? studentName = null,
        string? roomNumber = null,
        string? studentCode = null)
    {
        var plate = WebUtility.HtmlEncode(licensePlate);
        var formattedDate = dueDate.ToString("dd/MM/yyyy");

        var studentDisplay = string.IsNullOrWhiteSpace(studentName)
            ? "Sinh viên KTX"
            : WebUtility.HtmlEncode(studentName);
        if (!string.IsNullOrWhiteSpace(studentCode))
        {
            studentDisplay += $" ({WebUtility.HtmlEncode(studentCode)})";
        }

        var roomDisplay = string.IsNullOrWhiteSpace(roomNumber)
            ? "Ký túc xá"
            : $"Phòng {WebUtility.HtmlEncode(roomNumber)}";

        var content =
            $"""
            <div style="display:inline-block;padding:6px 12px;background:#fef3c7;border:1px solid #fde68a;border-radius:999px;font-size:12px;font-weight:700;color:#b45309;margin-bottom:14px;">
              ⚠️ CẢNH BÁO: HỢP ĐỒNG SẮP HẾT HẠN
            </div>
            <h2 style="margin:0 0 10px 0;font-size:21px;font-weight:750;color:#0f172a;letter-spacing:-0.4px;">Thông báo nhắc gia hạn gửi xe</h2>
            <p style="margin:0 0 20px 0;font-size:14px;line-height:1.6;color:#475569;">
              Hệ thống MPS xin thông báo hợp đồng gửi xe tháng của bạn sắp kết thúc thời hạn hiệu lực. Chi tiết phương tiện như sau:
            </p>

            <!-- Thẻ vé gửi xe điện tử (Digital Parking Pass) -->
            <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f8fafc;border:1.5px solid #e2e8f0;border-radius:16px;padding:20px;margin:20px 0;">
              <tr>
                <td style="font-size:13px;color:#64748b;font-weight:600;padding-bottom:12px;">🚗 Biển số phương tiện:</td>
                <td align="right" style="font-size:20px;font-weight:800;color:#0f172a;letter-spacing:1px;font-family:monospace;padding-bottom:12px;">{plate}</td>
              </tr>
              <tr>
                <td style="font-size:13px;color:#64748b;font-weight:600;border-top:1px dashed #cbd5e1;padding:12px 0;">👤 Chủ xe &amp; Nơi ở:</td>
                <td align="right" style="font-size:14px;font-weight:600;color:#1e293b;border-top:1px dashed #cbd5e1;padding:12px 0;">{studentDisplay} · {roomDisplay}</td>
              </tr>
              <tr>
                <td style="font-size:13px;color:#64748b;font-weight:600;border-top:1px dashed #cbd5e1;padding-top:12px;">📅 Hạn cuối gia hạn:</td>
                <td align="right" style="font-size:18px;font-weight:800;color:#dc2626;border-top:1px dashed #cbd5e1;padding-top:12px;">{formattedDate}</td>
              </tr>
            </table>

            <!-- Hướng dẫn hành động -->
            <div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:12px;padding:14px 18px;margin:22px 0;font-size:13px;color:#1e40af;line-height:1.65;">
              📌 <strong>Hướng dẫn:</strong> Vui lòng liên hệ trực tiếp <strong>Văn phòng Quản sinh / Ban Quản lý KTX</strong> trước ngày hết hạn để làm thủ tục gia hạn vé tháng.<br/>
              <em>Sau thời điểm này, thẻ từ phương tiện sẽ tạm thời không thể quẹt tự động qua cổng barrier.</em>
            </div>
            """;

        return WrapInLayout(content, $"Nhắc gia hạn gửi xe biển số {plate}");
    }

    public static string BuildGenericEmail(string heading, string message)
    {
        var escapedHeading = WebUtility.HtmlEncode(heading);
        var escapedMessage = WebUtility.HtmlEncode(message).Replace("\n", "<br/>");
        var content =
            $"""
            <h2 style="margin:0 0 12px 0;font-size:20px;font-weight:750;color:#0f172a;letter-spacing:-0.4px;">{escapedHeading}</h2>
            <div style="font-size:14px;line-height:1.65;color:#475569;margin-bottom:16px;">
              {escapedMessage}
            </div>
            """;

        return WrapInLayout(content, heading);
    }

    private static string WrapInLayout(string innerContent, string previewText)
    {
        var escapedPreview = WebUtility.HtmlEncode(previewText);
        return
            $$"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>{{escapedPreview}}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f1f5f9;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;-webkit-font-smoothing:antialiased;">
              <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f1f5f9;padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:540px;margin:0 auto;">

                      <!-- Header với Logo The Fluid Ribbon đính kèm CID -->
                      <tr>
                        <td align="center" style="padding-bottom:22px;">
                          <table cellpadding="0" cellspacing="0" border="0">
                            <tr>
                              <td style="vertical-align:middle;">
                                <img src="cid:mps-logo" alt="MPS" width="50" height="50" style="display:block;border-radius:14px;border:0;outline:none;" />
                              </td>
                              <td style="padding-left:14px;vertical-align:middle;text-align:left;">
                                <div style="font-size:21px;font-weight:800;color:#0f172a;letter-spacing:-0.5px;line-height:1.1;">
                                  MPS <span style="font-weight:450;color:#64748b;font-size:14px;">· Residence</span>
                                </div>
                                <div style="font-size:11px;color:#0284c7;font-weight:700;letter-spacing:0.8px;text-transform:uppercase;margin-top:3px;">
                                  Monthly Parking System
                                </div>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <!-- Khung nội dung chính -->
                      <tr>
                        <td style="background-color:#ffffff;border-radius:20px;border:1px solid #e2e8f0;box-shadow:0 10px 30px rgba(15,23,42,0.06);overflow:hidden;padding:34px 28px;">
                          <!-- Thanh phân cách Gradient The Fluid Ribbon -->
                          <div style="height:4px;margin:-34px -28px 28px -28px;background:linear-gradient(90deg,#0284c7 0%,#14b8a6 40%,#10b981 70%,#fb7185 100%);"></div>

                          {{innerContent}}
                        </td>
                      </tr>

                      <!-- Footer chân trang -->
                      <tr>
                        <td style="padding-top:24px;text-align:center;font-size:12px;color:#94a3b8;line-height:1.6;">
                          <div>Email tự động từ <strong>MPS — Monthly Parking System</strong>.</div>
                          <div>Hệ thống quản lý gửi xe tháng thông minh KTX &amp; Trường Đại học.</div>
                          <div style="margin-top:6px;font-size:11px;color:#cbd5e1;">© 2026 MPS Residence · Mọi quyền được bảo lưu.</div>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
