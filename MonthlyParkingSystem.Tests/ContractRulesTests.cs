using MonthlyParkingSystem.Api.Domain.Contracts;
using MonthlyParkingSystem.Api.Models;

namespace MonthlyParkingSystem.Tests;

public sealed class ContractRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [Fact]
    public void IsValidPeriod_RequiresDatesAndEndNotBeforeStart()
    {
        Assert.True(ContractRules.IsValidPeriod(Today, Today));
        Assert.True(ContractRules.IsValidPeriod(Today, Today.AddDays(1)));
        Assert.False(ContractRules.IsValidPeriod(default, Today));
        Assert.False(ContractRules.IsValidPeriod(Today, default));
        Assert.False(ContractRules.IsValidPeriod(Today.AddDays(1), Today));
    }

    [Fact]
    public void InitialStatus_UsesInclusiveStartAndEndDates()
    {
        Assert.Equal(ContractStatuses.Pending,
            ContractRules.InitialStatus(Today.AddDays(1), Today.AddDays(10), Today));
        Assert.Equal(ContractStatuses.Active,
            ContractRules.InitialStatus(Today, Today, Today));
        Assert.Equal(ContractStatuses.Active,
            ContractRules.InitialStatus(Today.AddDays(-1), Today, Today));
        Assert.Equal(ContractStatuses.Expired,
            ContractRules.InitialStatus(Today.AddDays(-2), Today.AddDays(-1), Today));
    }

    [Fact]
    public void BlockingStatus_IncludesOnlyActiveAndPendingContracts()
    {
        var blocks = ContractRules.BlockingStatus().Compile();

        Assert.True(blocks(new ParkingContract { Status = ContractStatuses.Active }));
        Assert.True(blocks(new ParkingContract { Status = ContractStatuses.Pending }));
        Assert.False(blocks(new ParkingContract { Status = ContractStatuses.Expired }));
        Assert.False(blocks(new ParkingContract { Status = ContractStatuses.Cancelled }));
    }

    [Fact]
    public void OverlapsPeriod_TreatsBothBoundaryDaysAsIncluded()
    {
        var overlaps = ContractRules.OverlapsPeriod(Today.AddDays(10), Today.AddDays(20)).Compile();
        var endsOnFirstDay = new ParkingContract
        {
            StartDate = Today,
            EndDate = Today.AddDays(10)
        };
        var startsTheDayAfter = new ParkingContract
        {
            StartDate = Today.AddDays(21),
            EndDate = Today.AddDays(30)
        };

        Assert.True(overlaps(endsOnFirstDay));
        Assert.False(overlaps(startsTheDayAfter));
    }

    [Theory]
    [InlineData(ContractStatuses.Active, 0, true)]
    [InlineData(ContractStatuses.Pending, 1, true)]
    [InlineData(ContractStatuses.Active, -1, false)]
    [InlineData(ContractStatuses.Expired, 2, false)]
    [InlineData(ContractStatuses.Cancelled, 2, false)]
    public void CanChangeVehicle_RequiresNonCancelledContractNotPastEndDate(
        string status,
        int daysFromToday,
        bool expected)
    {
        Assert.Equal(expected, ContractRules.CanChangeVehicle(status, Today.AddDays(daysFromToday), Today));
    }

    [Theory]
    [InlineData(ContractStatuses.Active, 0, true)]
    [InlineData(ContractStatuses.Pending, 1, true)]
    [InlineData(ContractStatuses.Active, -1, false)]
    [InlineData(ContractStatuses.Expired, 2, false)]
    [InlineData(ContractStatuses.Cancelled, 2, false)]
    public void CanCancel_RequiresActiveOrPendingContractNotPastEndDate(
        string status,
        int daysFromToday,
        bool expected)
    {
        Assert.Equal(expected, ContractRules.CanCancel(status, Today.AddDays(daysFromToday), Today));
    }

    [Fact]
    public void LifecycleTransitions_OnlyMoveAtTheirDateBoundaries()
    {
        var activate = ContractRules.PendingForActivation(Today).Compile();
        var expire = ContractRules.ActiveForExpiry(Today).Compile();

        Assert.True(activate(new ParkingContract { Status = ContractStatuses.Pending, StartDate = Today }));
        Assert.False(activate(new ParkingContract { Status = ContractStatuses.Pending, StartDate = Today.AddDays(1) }));
        Assert.False(activate(new ParkingContract { Status = ContractStatuses.Active, StartDate = Today }));

        Assert.True(expire(new ParkingContract { Status = ContractStatuses.Active, EndDate = Today.AddDays(-1) }));
        Assert.False(expire(new ParkingContract { Status = ContractStatuses.Active, EndDate = Today }));
        Assert.False(expire(new ParkingContract { Status = ContractStatuses.Pending, EndDate = Today.AddDays(-1) }));
    }

    [Fact]
    public void IsReminderDue_IncludesTodayAndThirdDayOnlyForActiveWithEmail()
    {
        var due = ContractRules.ReminderDue(Today).Compile();

        Assert.True(due(new ParkingContract
        {
            Status = ContractStatuses.Active,
            StartDate = Today,
            EndDate = Today,
            Student = new Student { EmailAddress = "student@example.edu" }
        }));
        Assert.True(due(new ParkingContract
        {
            Status = ContractStatuses.Active,
            StartDate = Today.AddDays(-1),
            EndDate = Today.AddDays(3),
            Student = new Student { EmailAddress = "student@example.edu" }
        }));
        Assert.False(due(new ParkingContract
        {
            Status = ContractStatuses.Active,
            StartDate = Today,
            EndDate = Today.AddDays(4),
            Student = new Student { EmailAddress = "student@example.edu" }
        }));
        Assert.False(due(new ParkingContract
        {
            Status = ContractStatuses.Pending,
            StartDate = Today,
            EndDate = Today.AddDays(1),
            Student = new Student { EmailAddress = "student@example.edu" }
        }));
        Assert.False(due(new ParkingContract
        {
            Status = ContractStatuses.Active,
            StartDate = Today,
            EndDate = Today.AddDays(1),
            Student = new Student { EmailAddress = null }
        }));
    }
}
