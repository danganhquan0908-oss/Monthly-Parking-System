# MPS API

ASP.NET Core Web API for the MPS parking workflow, connected to the SQL Server LocalDB database created by `Init_Database.sql`.

## Run locally

1. Initialize the database and apply the school-scoped migration from the repository root:

   ```powershell
   sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Init_Database.sql
   sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Migrate_Epic5_MultiTenant.sql
   sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Migrate_Epic5_1_SelfServiceRegistration.sql
   sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i Migrate_Epic5_2_EmailRegistration.sql
   ```

2. Set a private 32-byte AES key for student phone encryption. Keep the key outside source control and back it up securely; losing it makes saved phone numbers unreadable.

   ```powershell
   $keyBytes = [byte[]]::new(32)
   [Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
   $env:MPS_PHONE_ENCRYPTION_KEY = [Convert]::ToBase64String($keyBytes)
   ```

3. Start the API:

   ```powershell
   dotnet run --project MonthlyParkingSystem.Api --launch-profile http
   ```

The local API listens at `http://localhost:5127`. Phone numbers are stored as AES-256-GCM bytes (`nonce || tag || ciphertext`). A phone number is optional during school registration; if one is supplied, the phone encryption key above is required.

For production, configure a stable signing key, platform provisioning key, and phone encryption key in a secret store or process environment before starting the API. The JWT and phone keys must decode from Base64 to at least 32 bytes. The platform key creates/suspends schools and issues the first Manager invitation; it is never sent to a school user.

```powershell
$jwtBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($jwtBytes)
$env:MPS_JWT_SIGNING_KEY = [Convert]::ToBase64String($jwtBytes)
$platformBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($platformBytes)
$env:MPS_PLATFORM_PROVISIONING_KEY = [Convert]::ToBase64String($platformBytes)
```

## Epic 1 endpoints

| Method | Route | Behavior |
| --- | --- | --- |
| `POST` | `/api/v1/contracts` | Create a contract; create or synchronize the student and create/reuse their vehicle |
| `GET` | `/api/v1/contracts/{id}` | Read a contract |
| `PUT` | `/api/v1/contracts/{id}` | Update shared student details and contract dates; a plate change is audited |
| `PUT` | `/api/v1/contracts/{id}/change-vehicle` | Change an active contract's plate and record the audit entry |
| `GET` | `/api/v1/contracts/{id}/vehicle-history` | Read plate-change history in chronological order |
| `GET` | `/api/v1/contracts/{id}/history` | Read contract creation, edits, cancellation, and vehicle changes for the authenticated school |
| `PUT` | `/api/v1/contracts/{id}/cancel` | Cancel an active contract early |
| `GET` | `/api/v1/contracts/stats` | Get active, expiring-soon (0–3 days), and expired totals |
| `GET` | `/api/v1/contracts/expiring?page=1&pageSize=10&search=...&status=all` | Search contracts with server-side pagination and dashboard stats |
| `POST` | `/api/v1/auth/login` | Verify school code, staff username and password; return a 60-minute school-scoped bearer token |
| `POST` | `/api/v1/auth/accept-invitation` | Accept a single-use invitation and create a school account |
| `GET` | `/api/v1/auth/staff` | List staff for the authenticated school |
| `POST` | `/api/v1/auth/staff` | Create a school-scoped Manager/Guard/Staff account directly |
| `POST` | `/api/v1/auth/staff/invitations` | Issue a 3-day staff invitation (Manager invites Guard/Staff; Admin may invite Manager too) |
| `GET` | `/api/v1/auth/account-audit` | Read the latest 200 school-scoped account actions |
| `PUT` | `/api/v1/auth/staff/{id}/active` | Admin/Manager deactivates or reactivates staff in their school |
| `POST` | `/api/v1/platform/schools` | Platform-key protected school creation and first Manager invitation |
| `GET` | `/api/v1/platform/schools` | Platform-key protected school listing |
| `PUT` | `/api/v1/platform/schools/{id}/active` | Platform-key protected school suspension/reactivation |
| `POST` | `/api/v1/public/school-registrations/send-otp` | Begin public school registration; stores a short-lived OTP challenge and sends an email |
| `POST` | `/api/v1/public/school-registrations/verify-otp` | Verify the OTP and atomically create the school plus its first Admin account |

All contract and dashboard routes require a bearer token containing a verified `school_id`. Admin and Manager can create, edit, change plates, and cancel contracts. Guard and Staff can read dashboard, contract details, and contract history; phone numbers are masked for those roles. Admin and Manager can see the full number. Contract history records who made each change, when it occurred, and the affected field values; phone numbers are intentionally excluded. Invalid passwords are locked for 15 minutes after five consecutive failures. Deactivated users or suspended schools invalidate existing tokens on the next request. Invitation tokens are random, stored only as SHA-256 hashes, expire, and can be used once.

Unique plate conflicts and the one-active-contract-per-student-per-school rule return HTTP `409`. CORS allows all origins only in Development; set `Cors:AllowedOrigins` to explicit frontend origins in other environments. LocalDB is for development; centrally hosted deployments must use production SQL Server and appropriate encryption/backups.

## Dashboard and school self-registration

The Vue 3 dashboard is served at `http://localhost:5127/`. New schools can register at `/register`, choose a school code and first Admin account, then verify the manager's email by OTP. A phone number is optional and is saved only as unverified contact information. Successful verification creates the tenant directly; the school code is shown on the completion page. The verified email is saved on the first Admin account. The platform provisioning key stays with platform operations and is not used by public registration. Staff invitations are still returned as links for out-of-band delivery.

Registration OTP delivery uses SMTP with STARTTLS. Set the following environment variables to the SMTP credentials from your email provider; store credentials in a secret manager in production and use a verified sender address/domain. Without SMTP configuration, the API returns HTTP 503 and does not create a usable registration request.

```powershell
$env:Email__SmtpHost = 'email-smtp.ap-southeast-1.amazonaws.com'
$env:Email__SmtpPort = '587'
$env:Email__SmtpUsername = '<smtp-username>'
$env:Email__SmtpPassword = '<smtp-password>'
$env:Email__FromAddress = 'no-reply@your-domain.example'
$env:Email__FromName = 'MPS'
```

OTP codes expire after five minutes, allow at most five attempts, and are rate-limited to three sends per email address per hour plus IP-based request limits. Email addresses are stored on the first Admin account; supplied phone numbers remain optional and unverified.

## Expiry notifications

`DailyScanWorker` runs at startup and then every 24 hours. It expires past-due contracts and queues one reminder for each active contract with an end date from today through three days from today. A reminder is deduplicated by contract and due date. `NotificationProcessor` sends through a configured HTTP gateway and uses Polly for three retries, five seconds apart. Each delivery result is recorded in `NotificationLogs`; only a masked phone number is stored there.

Expiry reminders remain separate from registration email. They use the configured HTTP notification gateway; set the gateway environment variables before starting the API. Until an endpoint is configured, scans still expire contracts but pause reminder queuing.

```powershell
$env:MPS_NOTIFICATION_ENDPOINT = 'https://your-sms-or-zalo-gateway.example/messages'
$env:MPS_NOTIFICATION_TOKEN = '<gateway-token>'
$env:MPS_NOTIFICATION_CHANNEL = 'SMS' # or Zalo
```

The gateway adapter sends JSON with `channel`, `to`, `message`, and `clientReference` fields. It may return a `messageId` or `id` field, which is saved in the notification log. Configure the gateway URL and credentials supplied by the chosen provider; the project does not include provider credentials.

For an existing database, run `Migrate_Epic3_Notification_DueDate.sql`, `Migrate_Epic4_StaffUsers.sql`, `Migrate_Epic5_MultiTenant.sql`, `Migrate_Epic5_1_SelfServiceRegistration.sql`, and `Migrate_Epic5_2_EmailRegistration.sql` before starting the updated API. The Epic 5 migration moves existing rows and staff accounts into school code `MPS-DEFAULT` without deleting data; Epic 5.1 adds registration challenge storage and optional phone storage; Epic 5.2 adds email OTP fields and preserves existing registration records.
