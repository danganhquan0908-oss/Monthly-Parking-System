using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Contracts;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Models;

namespace MonthlyParkingSystem.Api.Controllers;

[ApiController]
[Authorize(Roles = StaffRoles.DashboardReaders)]
[Route("api/v1/contracts")]
public sealed class DashboardController(MpsDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    [ProducesResponseType<DashboardStatsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardStatsResponse>> GetStats(CancellationToken cancellationToken)
        => Ok(await ReadStats(cancellationToken));

    private async Task<DashboardStatsResponse> ReadStats(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var through = today.AddDays(3);
        var stats = await db.ParkingContracts.AsNoTracking()
            .Where(contract => contract.SchoolId == CurrentSchoolId())
            .GroupBy(_ => 1)
            .Select(group => new DashboardStatsResponse(
                group.Count(contract => contract.Status == ContractStatuses.Active && contract.EndDate >= today),
                group.Count(contract => contract.Status == ContractStatuses.Active && contract.EndDate >= today && contract.EndDate <= through),
                group.Count(contract => contract.Status == ContractStatuses.Expired ||
                                        (contract.Status == ContractStatuses.Active && contract.EndDate < today))))
            .SingleOrDefaultAsync(cancellationToken);

        return stats ?? new DashboardStatsResponse(0, 0, 0);
    }

    [HttpGet("expiring")]
    [ProducesResponseType<PagedDashboardResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedDashboardResponse>> GetContracts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest("page must be greater than zero.");
        if (pageSize < 1 || pageSize > 100) return BadRequest("pageSize must be between 1 and 100.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var through = today.AddDays(3);
        var query = db.ParkingContracts.AsNoTracking()
            .Where(contract => contract.SchoolId == CurrentSchoolId())
            .Select(contract => new
            {
                contract.ContractId,
                contract.Student.StudentCode,
                contract.Student.FullName,
                contract.Student.RoomNumber,
                contract.Vehicle.LicensePlate,
                contract.StartDate,
                contract.EndDate,
                contract.Status,
                DisplayStatus = contract.Status == ContractStatuses.Cancelled ? ContractStatuses.Cancelled :
                    contract.Status == ContractStatuses.Expired || contract.EndDate < today ? ContractStatuses.Expired :
                    contract.Status == ContractStatuses.Pending || contract.StartDate > today ? ContractStatuses.Pending :
                    contract.EndDate <= through ? "ExpiringSoon" : ContractStatuses.Active
            });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(contract => contract.StudentCode.Contains(term) ||
                contract.FullName.Contains(term) || contract.RoomNumber.Contains(term) ||
                contract.LicensePlate.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var requestedStatus = status.Trim().ToLowerInvariant();
            query = requestedStatus switch
            {
                "active" => query.Where(contract => contract.DisplayStatus == ContractStatuses.Active || contract.DisplayStatus == "ExpiringSoon"),
                "pending" => query.Where(contract => contract.DisplayStatus == ContractStatuses.Pending),
                "expiring" or "expiringsoon" => query.Where(contract => contract.DisplayStatus == "ExpiringSoon"),
                "expired" => query.Where(contract => contract.DisplayStatus == ContractStatuses.Expired),
                "cancelled" or "canceled" => query.Where(contract => contract.DisplayStatus == ContractStatuses.Cancelled),
                "all" => query,
                _ => null
            };
            if (query is null) return BadRequest("status must be all, active, pending, expiring, expired, or cancelled.");
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(contract => contract.DisplayStatus == "ExpiringSoon" ? 0 :
                contract.DisplayStatus == ContractStatuses.Active ? 1 :
                contract.DisplayStatus == ContractStatuses.Pending ? 2 :
                contract.DisplayStatus == ContractStatuses.Expired ? 3 : 4)
            .ThenBy(contract => contract.EndDate)
            .ThenBy(contract => contract.ContractId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(contract => new DashboardContractResponse(
                contract.ContractId,
                contract.StudentCode,
                contract.FullName,
                contract.RoomNumber,
                contract.LicensePlate,
                contract.StartDate,
                contract.EndDate,
                contract.DisplayStatus))
            .ToListAsync(cancellationToken);

        var stats = await ReadStats(cancellationToken);
        return Ok(new PagedDashboardResponse(items, page, pageSize, totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize), stats));
    }

    private int CurrentSchoolId() => int.Parse(User.FindFirst("school_id")!.Value);
}
