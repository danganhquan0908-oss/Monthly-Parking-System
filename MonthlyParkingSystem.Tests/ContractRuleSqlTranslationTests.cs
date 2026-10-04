using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Data;
using MonthlyParkingSystem.Api.Domain.Contracts;

namespace MonthlyParkingSystem.Tests;

public sealed class ContractRuleSqlTranslationTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [Fact]
    public void BlockingAndOverlapRulesTranslateToSqlServerSql()
    {
        using var db = CreateContext();

        var sql = db.ParkingContracts.AsNoTracking()
            .Where(contract => contract.SchoolId == 10 && contract.StudentId == 20)
            .Where(ContractRules.BlockingStatus())
            .Where(ContractRules.OverlapsPeriod(Today, Today.AddDays(30)))
            .ToQueryString();

        Assert.Contains("ParkingContracts", sql);
        Assert.Contains("Status", sql);
        Assert.Contains("StartDate", sql);
        Assert.Contains("EndDate", sql);
    }

    [Fact]
    public void ReminderEligibilityRuleTranslatesStudentEmailNavigation()
    {
        using var db = CreateContext();

        var sql = db.ParkingContracts.AsNoTracking()
            .Where(ContractRules.ReminderDue(Today))
            .ToQueryString();

        Assert.Contains("Students", sql);
        Assert.Contains("EmailAddress", sql);
        Assert.Contains("StartDate", sql);
        Assert.Contains("EndDate", sql);
    }

    private static MpsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MpsDbContext>()
            .UseSqlServer("Server=localhost;Database=MPS_QueryTranslation;User ID=test;Password=unused;TrustServerCertificate=True")
            .Options;
        return new MpsDbContext(options);
    }
}
