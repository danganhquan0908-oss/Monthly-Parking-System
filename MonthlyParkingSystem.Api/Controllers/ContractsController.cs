using System.Text.Json;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Contracts;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;
using MonthlyParkingSystem.Api.Services;

namespace MonthlyParkingSystem.Api.Controllers;

[ApiController]
[Authorize(Roles = StaffRoles.DashboardReaders)]
[Route("api/v1/contracts")]
public sealed class ContractsController(MpsDbContext db, IPhoneEncryptionService phoneEncryption) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = StaffRoles.ContractManagers)]
    [ProducesResponseType<ContractResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ContractResponse>> Register(RegisterContractRequest request, CancellationToken cancellationToken)
    {
        if (request.StartDate == default || request.EndDate == default) return BadRequest("StartDate and EndDate are required.");
        if (request.EndDate < request.StartDate) return BadRequest("EndDate must be on or after StartDate.");
        var studentCode = request.StudentCode.Trim();
        var plate = NormalizePlate(request.LicensePlate);
        if (plate.Length == 0) return BadRequest("LicensePlate is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var schoolId = CurrentSchoolId();
            var student = await db.Students.SingleOrDefaultAsync(x => x.SchoolId == schoolId && x.StudentCode == studentCode, cancellationToken);
            if (student is null)
            {
                student = new Student { SchoolId = schoolId, StudentCode = studentCode };
                db.Students.Add(student);
            }
            ApplyStudentDetails(student, request.FullName, request.RoomNumber, request.PhoneNumber, creating: true);

            await db.SaveChangesAsync(cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.Now);
            var previousActive = await db.ParkingContracts
                .Where(x => x.SchoolId == schoolId && x.StudentId == student.StudentId && x.Status == ContractStatuses.Active)
                .ToListAsync(cancellationToken);
            foreach (var previous in previousActive.Where(x => x.EndDate < today))
            {
                previous.Status = ContractStatuses.Expired;
                previous.UpdatedAtUtc = DateTime.UtcNow;
            }
            
            var hasOverlap = await db.ParkingContracts.AnyAsync(x => 
                x.SchoolId == schoolId && 
                x.StudentId == student.StudentId && 
                (x.Status == ContractStatuses.Active || x.Status == ContractStatuses.Pending) &&
                request.StartDate <= x.EndDate && request.EndDate >= x.StartDate, 
                cancellationToken);

            if (hasOverlap)
                return Conflict("Thời gian hợp đồng bị chồng chéo với một hợp đồng (Active hoặc Pending) khác của sinh viên.");

            var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.SchoolId == schoolId && x.LicensePlate == plate, cancellationToken);
            if (vehicle is not null && vehicle.StudentId != student.StudentId)
                return Conflict("Biển số xe đã được đăng ký cho sinh viên khác.");
            if (vehicle is null)
            {
                vehicle = new Vehicle { SchoolId = schoolId, StudentId = student.StudentId, LicensePlate = plate };
                db.Vehicles.Add(vehicle);
            }
            var contract = new ParkingContract
            {
                SchoolId = schoolId,
                StudentId = student.StudentId,
                Vehicle = vehicle,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Status = request.EndDate < today ? ContractStatuses.Expired : request.StartDate > today ? ContractStatuses.Pending : ContractStatuses.Active
            };
            db.ParkingContracts.Add(contract);

            await db.SaveChangesAsync(cancellationToken);

            // Save once to obtain the identity ContractId, then write its audit record in the same transaction.
            db.AuditLogs.Add(new AuditLog
            {
                SchoolId = schoolId,
                EntityName = "ParkingContract",
                EntityId = contract.ContractId,
                Action = "Create",
                NewValues = JsonSerializer.Serialize(BuildContractSnapshot(contract, student, vehicle)),
                ChangedBy = User.Identity?.Name,
                ChangedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = contract.ContractId }, ToResponse(contract, student, vehicle));
        }
        catch (PhoneEncryptionKeyMissingException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Phone encryption is not configured", detail: ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("MPS_PHONE_ENCRYPTION_KEY", StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Phone encryption configuration is invalid", detail: ex.Message);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("Mã sinh viên, biển số hoặc hợp đồng đang hoạt động bị trùng.");
        }
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = StaffRoles.ContractManagers)]
    [ProducesResponseType<ContractResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContractResponse>> Update(long id, UpdateContractRequest request, CancellationToken cancellationToken)
    {
        if (request.StartDate == default || request.EndDate == default) return BadRequest("StartDate and EndDate are required.");
        if (request.EndDate < request.StartDate) return BadRequest("EndDate must be on or after StartDate.");
        var plate = NormalizePlate(request.LicensePlate);
        if (plate.Length == 0) return BadRequest("LicensePlate is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var contract = await db.ParkingContracts.Include(x => x.Student).Include(x => x.Vehicle)
                .SingleOrDefaultAsync(x => x.SchoolId == CurrentSchoolId() && x.ContractId == id, cancellationToken);
            if (contract is null) return NotFound();
            if (contract.Status == ContractStatuses.Cancelled) return Conflict("Hợp đồng đã bị hủy.");

            var oldValues = BuildContractSnapshot(contract, contract.Student, contract.Vehicle);

            var today = DateOnly.FromDateTime(DateTime.Now);
            
            var hasOverlap = await db.ParkingContracts.AnyAsync(x => 
                x.SchoolId == CurrentSchoolId() && 
                x.StudentId == contract.StudentId && 
                x.ContractId != id &&
                (x.Status == ContractStatuses.Active || x.Status == ContractStatuses.Pending) &&
                request.StartDate <= x.EndDate && request.EndDate >= x.StartDate, 
                cancellationToken);

            if (hasOverlap)
                return Conflict("Thời gian cập nhật bị chồng chéo với một hợp đồng khác.");

            var changingPlate = !string.Equals(contract.Vehicle.LicensePlate, plate, StringComparison.OrdinalIgnoreCase);
            if (changingPlate && (contract.Status != ContractStatuses.Active || contract.EndDate < today))
                return Conflict("Chỉ được đổi biển số khi hợp đồng còn hiệu lực hoặc chưa bắt đầu.");

            ApplyStudentDetails(contract.Student, request.FullName, request.RoomNumber, request.PhoneNumber, creating: false);
            contract.StartDate = request.StartDate;
            contract.EndDate = request.EndDate;
            contract.Status = request.EndDate < today ? ContractStatuses.Expired : request.StartDate > today ? ContractStatuses.Pending : ContractStatuses.Active;
            contract.UpdatedAtUtc = DateTime.UtcNow;

            if (changingPlate)
            {
                var plateOwner = await db.Vehicles.SingleOrDefaultAsync(x => x.SchoolId == CurrentSchoolId() && x.LicensePlate == plate, cancellationToken);
                if (plateOwner is not null && plateOwner.VehicleId != contract.VehicleId)
                    return Conflict("Biển số xe đã được đăng ký.");
                AddVehicleChange(contract, plate);
            }

            db.AuditLogs.Add(new AuditLog
            {
                SchoolId = contract.SchoolId,
                EntityName = "ParkingContract",
                EntityId = contract.ContractId,
                Action = "Update",
                OldValues = JsonSerializer.Serialize(oldValues),
                NewValues = JsonSerializer.Serialize(BuildContractSnapshot(contract, contract.Student, contract.Vehicle)),
                ChangedBy = User.Identity?.Name,
                ChangedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(ToResponse(contract, contract.Student, contract.Vehicle));
        }
        catch (PhoneEncryptionKeyMissingException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Phone encryption is not configured", detail: ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("MPS_PHONE_ENCRYPTION_KEY", StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Phone encryption configuration is invalid", detail: ex.Message);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("Biển số xe hoặc trạng thái hợp đồng bị trùng.");
        }
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<ContractResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContractResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var contract = await db.ParkingContracts.AsNoTracking()
            .Include(x => x.Student).Include(x => x.Vehicle)
            .SingleOrDefaultAsync(x => x.SchoolId == CurrentSchoolId() && x.ContractId == id, cancellationToken);
        return contract is null ? NotFound() : Ok(ToResponse(contract, contract.Student, contract.Vehicle));
    }

    [HttpPut("{id:long}/change-vehicle")]
    [Authorize(Roles = StaffRoles.ContractManagers)]
    [ProducesResponseType<ContractResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContractResponse>> ChangeVehicle(long id, ChangeVehicleRequest request, CancellationToken cancellationToken)
    {
        var plate = NormalizePlate(request.LicensePlate);
        if (plate.Length == 0) return BadRequest("LicensePlate is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var contract = await db.ParkingContracts.Include(x => x.Student).Include(x => x.Vehicle)
                .SingleOrDefaultAsync(x => x.SchoolId == CurrentSchoolId() && x.ContractId == id, cancellationToken);
            if (contract is null) return NotFound();
            if ((contract.Status != ContractStatuses.Active && contract.Status != ContractStatuses.Pending) || contract.EndDate < DateOnly.FromDateTime(DateTime.Now))
                return Conflict("Chỉ được đổi biển số khi hợp đồng còn hiệu lực hoặc chưa bắt đầu.");
            if (string.Equals(contract.Vehicle.LicensePlate, plate, StringComparison.OrdinalIgnoreCase))
                return BadRequest("Biển số mới phải khác biển số hiện tại.");

            var plateOwner = await db.Vehicles.SingleOrDefaultAsync(x => x.SchoolId == CurrentSchoolId() && x.LicensePlate == plate, cancellationToken);
            if (plateOwner is not null && plateOwner.VehicleId != contract.VehicleId)
                return Conflict("Biển số xe đã được đăng ký.");

            AddVehicleChange(contract, plate, request.ChangeNote);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(ToResponse(contract, contract.Student, contract.Vehicle));
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("Biển số xe đã được đăng ký.");
        }
    }

    [HttpGet("{id:long}/vehicle-history")]
    [ProducesResponseType<IEnumerable<VehicleChangeResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<VehicleChangeResponse>>> GetVehicleHistory(long id, CancellationToken cancellationToken)
    {
        var schoolId = CurrentSchoolId();
        var exists = await db.ParkingContracts.AnyAsync(x => x.SchoolId == schoolId && x.ContractId == id, cancellationToken);
        if (!exists) return NotFound();
        var history = await db.VehicleChangeLogs.AsNoTracking().Where(x => x.SchoolId == schoolId && x.ContractId == id)
            .OrderBy(x => x.ChangedAtUtc)
            .Select(x => new VehicleChangeResponse(x.VehicleChangeLogId, x.OldLicensePlate, x.NewLicensePlate,
                x.ChangedBy, x.ChangeNote, x.ChangedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(history);
    }

    [HttpPut("{id:long}/cancel")]
    [Authorize(Roles = StaffRoles.ContractManagers)]
    [ProducesResponseType<ContractResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContractResponse>> Cancel(long id, CancelContractRequest request, CancellationToken cancellationToken)
    {
        var contract = await db.ParkingContracts.Include(x => x.Student).Include(x => x.Vehicle)
            .SingleOrDefaultAsync(x => x.SchoolId == CurrentSchoolId() && x.ContractId == id, cancellationToken);
        if (contract is null) return NotFound();
        if ((contract.Status != ContractStatuses.Active && contract.Status != ContractStatuses.Pending) || contract.EndDate < DateOnly.FromDateTime(DateTime.Now))
            return Conflict("Chỉ có thể hủy hợp đồng đang hoặc sẽ có hiệu lực.");

        var oldValues = BuildContractSnapshot(contract, contract.Student, contract.Vehicle);

        contract.Status = ContractStatuses.Cancelled;
        contract.CancelledAtUtc = DateTime.UtcNow;
        contract.CancellationNote = string.IsNullOrWhiteSpace(request.CancellationNote) ? null : request.CancellationNote.Trim();
        contract.UpdatedAtUtc = DateTime.UtcNow;

        var newValues = BuildContractSnapshot(contract, contract.Student, contract.Vehicle);
        newValues["CancellationNote"] = contract.CancellationNote;
        db.AuditLogs.Add(new AuditLog
        {
            SchoolId = contract.SchoolId,
            EntityName = "ParkingContract",
            EntityId = contract.ContractId,
            Action = "Cancel",
            OldValues = JsonSerializer.Serialize(oldValues),
            NewValues = JsonSerializer.Serialize(newValues),
            ChangedBy = User.Identity?.Name,
            ChangedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(contract, contract.Student, contract.Vehicle));
    }

    private void ApplyStudentDetails(Student student, string fullName, string roomNumber, string? phoneNumber, bool creating)
    {
        student.FullName = fullName.Trim();
        student.RoomNumber = roomNumber.Trim();
        if (creating || !string.IsNullOrWhiteSpace(phoneNumber)) student.PhoneEncrypted = phoneEncryption.Encrypt(phoneNumber);
        student.UpdatedAtUtc = DateTime.UtcNow;
    }

    private void AddVehicleChange(ParkingContract contract, string newPlate, string? changeNote = null)
    {
        var oldPlate = contract.Vehicle.LicensePlate;
        contract.Vehicle.LicensePlate = newPlate;
        contract.Vehicle.UpdatedAtUtc = DateTime.UtcNow;
        contract.UpdatedAtUtc = DateTime.UtcNow;
        db.VehicleChangeLogs.Add(new VehicleChangeLog
        {
            SchoolId = contract.SchoolId,
            ContractId = contract.ContractId,
            VehicleId = contract.VehicleId,
            StudentId = contract.StudentId,
            OldLicensePlate = oldPlate,
            NewLicensePlate = newPlate,
            ChangedBy = User.Identity?.Name,
            ChangeNote = string.IsNullOrWhiteSpace(changeNote) ? null : changeNote.Trim()
        });
    }

    private ContractResponse ToResponse(ParkingContract contract, Student student, Vehicle vehicle)
    {
        var phoneNumber = phoneEncryption.Decrypt(student.PhoneEncrypted);
        if (!User.IsInRole(StaffRoles.Admin) && !User.IsInRole(StaffRoles.Manager))
            phoneNumber = MaskPhone(phoneNumber);
        return new ContractResponse(contract.ContractId, student.StudentCode, student.FullName, student.RoomNumber,
            vehicle.LicensePlate, contract.StartDate, contract.EndDate, contract.Status,
            contract.CreatedAtUtc, contract.UpdatedAtUtc, phoneNumber);
    }

    private static string? MaskPhone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return null;
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return new string('*', Math.Max(1, digits.Length));
        return new string('*', digits.Length - 4) + digits[^4..];
    }

    private static string NormalizePlate(string plate) => plate.Trim().ToUpperInvariant();

    private int CurrentSchoolId() => int.Parse(User.FindFirst("school_id")!.Value);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    public record ContractHistoryResponse(DateTime ChangedAt, string Action, string ChangedBy, string Details);

    [HttpGet("{id:long}/history")]
    public async Task<ActionResult<IEnumerable<ContractHistoryResponse>>> GetHistory(long id, CancellationToken cancellationToken)
    {
        var schoolId = CurrentSchoolId();
        var contractExists = await db.ParkingContracts.AsNoTracking()
            .AnyAsync(x => x.SchoolId == schoolId && x.ContractId == id, cancellationToken);
        if (!contractExists) return NotFound();

        var auditRows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.SchoolId == schoolId && x.EntityName == "ParkingContract" && x.EntityId == id)
            .Select(x => new { x.AuditLogId, x.ChangedAtUtc, x.Action, x.ChangedBy, x.OldValues, x.NewValues })
            .ToListAsync(cancellationToken);
        var audits = auditRows.Select(x => new ContractHistoryResponse(
            x.ChangedAtUtc,
            x.Action,
            x.ChangedBy ?? "Hệ thống",
            FormatAuditDetails(x.Action, x.OldValues, x.NewValues)));

        var vehicleLogs = await db.VehicleChangeLogs.AsNoTracking()
            .Where(x => x.SchoolId == schoolId && x.ContractId == id)
            .Select(x => new { x.VehicleChangeLogId, x.ChangedAtUtc, x.ChangedBy, x.OldLicensePlate, x.NewLicensePlate, x.ChangeNote })
            .ToListAsync(cancellationToken);

        var history = audits.Concat(vehicleLogs.Select(x => new ContractHistoryResponse(
                x.ChangedAtUtc,
                "ChangeVehicle",
                x.ChangedBy ?? "Hệ thống",
                $"Biển số: {x.OldLicensePlate} → {x.NewLicensePlate}" +
                (string.IsNullOrWhiteSpace(x.ChangeNote) ? string.Empty : $". Lý do: {x.ChangeNote}"))))
            .OrderByDescending(x => x.ChangedAt)
            .ToList();
        return Ok(history);
    }

    private static Dictionary<string, object?> BuildContractSnapshot(ParkingContract contract, Student student, Vehicle vehicle) =>
        new()
        {
            ["StudentCode"] = student.StudentCode,
            ["FullName"] = student.FullName,
            ["RoomNumber"] = student.RoomNumber,
            ["LicensePlate"] = vehicle.LicensePlate,
            ["StartDate"] = contract.StartDate,
            ["EndDate"] = contract.EndDate,
            ["Status"] = contract.Status
        };

    private static string FormatAuditDetails(string action, string? oldValuesJson, string? newValuesJson)
    {
        var oldValues = ParseSnapshot(oldValuesJson);
        var newValues = ParseSnapshot(newValuesJson);
        var keys = oldValues.Keys.Union(newValues.Keys, StringComparer.OrdinalIgnoreCase);
        var changes = new List<string>();

        foreach (var key in keys)
        {
            var hasOld = oldValues.TryGetValue(key, out var oldValue);
            var hasNew = newValues.TryGetValue(key, out var newValue);
            if (hasOld && hasNew && string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;

            var label = key switch
            {
                "StudentCode" => "Mã sinh viên",
                "FullName" => "Họ tên",
                "RoomNumber" => "Phòng",
                "LicensePlate" => "Biển số",
                "StartDate" => "Ngày bắt đầu",
                "EndDate" => "Ngày hết hạn",
                "Status" => "Trạng thái",
                "CancellationNote" => "Lý do hủy",
                _ => key
            };
            changes.Add(hasOld && hasNew
                ? $"{label}: {oldValue} → {newValue}"
                : hasNew ? $"{label}: {newValue}" : $"{label}: {oldValue} → đã xóa");
        }

        return changes.Count == 0
            ? action switch
            {
                "Create" => "Tạo hợp đồng",
                "Cancel" => "Hủy hợp đồng",
                _ => "Không có thay đổi chi tiết"
            }
            : string.Join("; ", changes);
    }

    private static Dictionary<string, string> ParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return document.RootElement.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null => "—",
                    _ => property.Value.ToString()
                },
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
