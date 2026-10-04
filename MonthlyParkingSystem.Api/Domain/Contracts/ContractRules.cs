using System.Linq.Expressions;
using MonthlyParkingSystem.Api.Models;

namespace MonthlyParkingSystem.Api.Domain.Contracts;

/// <summary>
/// Pure contract lifecycle rules shared by API workflows and automated tests.
/// Database predicates are exposed as expressions so Entity Framework can translate them.
/// </summary>
public static class ContractRules
{
    public static bool IsValidPeriod(DateOnly startDate, DateOnly endDate) =>
        startDate != default && endDate != default && endDate >= startDate;

    public static string InitialStatus(DateOnly startDate, DateOnly endDate, DateOnly today)
    {
        if (endDate < today) return ContractStatuses.Expired;
        return startDate > today ? ContractStatuses.Pending : ContractStatuses.Active;
    }

    public static bool CanChangeVehicle(string status, DateOnly endDate, DateOnly today) =>
        (status == ContractStatuses.Active || status == ContractStatuses.Pending) && endDate >= today;

    public static bool CanCancel(string status, DateOnly endDate, DateOnly today) =>
        (status == ContractStatuses.Active || status == ContractStatuses.Pending) && endDate >= today;

    public static Expression<Func<ParkingContract, bool>> BlockingStatus() =>
        contract => contract.Status == ContractStatuses.Active || contract.Status == ContractStatuses.Pending;

    public static Expression<Func<ParkingContract, bool>> OverlapsPeriod(DateOnly startDate, DateOnly endDate) =>
        contract => startDate <= contract.EndDate && endDate >= contract.StartDate;

    public static Expression<Func<ParkingContract, bool>> PendingForActivation(DateOnly today) =>
        contract => contract.Status == ContractStatuses.Pending && contract.StartDate <= today;

    public static Expression<Func<ParkingContract, bool>> ActiveForExpiry(DateOnly today) =>
        contract => contract.Status == ContractStatuses.Active && contract.EndDate < today;

    public static Expression<Func<ParkingContract, bool>> ReminderDue(DateOnly today)
    {
        var throughDate = today.AddDays(3);
        return contract => contract.Status == ContractStatuses.Active &&
                           contract.StartDate <= today &&
                           contract.EndDate >= today &&
                           contract.EndDate <= throughDate &&
                           contract.Student.EmailAddress != null &&
                           contract.Student.EmailAddress != string.Empty;
    }
}
