using Microsoft.EntityFrameworkCore;
using MonthlyParkingSystem.Api.Models;

namespace MonthlyParkingSystem.Api.Data;

public sealed class MpsDbContext(DbContextOptions<MpsDbContext> options) : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<AccountAuditLog> AccountAuditLogs => Set<AccountAuditLog>();
    public DbSet<SchoolRegistrationChallenge> SchoolRegistrationChallenges => Set<SchoolRegistrationChallenge>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ParkingContract> ParkingContracts => Set<ParkingContract>();
    public DbSet<VehicleChangeLog> VehicleChangeLogs => Set<VehicleChangeLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<School>(entity =>
        {
            entity.ToTable("Schools", table => table.HasCheckConstraint("CK_Schools_Name_NotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0"));
            entity.HasKey(x => x.SchoolId).HasName("PK_Schools");
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.NormalizedCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.NormalizedCode).IsUnique().HasDatabaseName("UQ_Schools_NormalizedCode");
        });

        modelBuilder.Entity<StaffUser>(entity =>
        {
            entity.ToTable("StaffUsers", table =>
            {
                table.HasCheckConstraint("CK_StaffUsers_Role", "[Role] IN ('Admin', 'Manager', 'Guard', 'Staff')");
                table.HasCheckConstraint("CK_StaffUsers_AccessFailedCount", "[AccessFailedCount] <= 5");
            });
            entity.HasKey(x => x.StaffUserId).HasName("PK_StaffUsers");
            entity.Property(x => x.Username).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.EmailVerified).HasDefaultValue(false);
            entity.Property(x => x.PhoneNumberEncrypted).HasColumnType("varbinary(512)");
            entity.Property(x => x.Role).HasMaxLength(16).IsUnicode(false).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.AccessFailedCount).HasDefaultValue((byte)0);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.SchoolId, x.NormalizedUsername }).IsUnique()
                .HasDatabaseName("UQ_StaffUsers_School_NormalizedUsername");
            entity.HasAlternateKey(x => new { x.SchoolId, x.StaffUserId }).HasName("UQ_StaffUsers_School_User");
            entity.HasOne(x => x.School).WithMany(x => x.StaffUsers).HasForeignKey(x => x.SchoolId)
                .HasConstraintName("FK_StaffUsers_Schools").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SchoolRegistrationChallenge>(entity =>
        {
            entity.ToTable("SchoolRegistrationChallenges", table =>
            {
                table.HasCheckConstraint("CK_SchoolRegistrationChallenges_Attempts", "[AttemptCount] <= 5");
                table.HasCheckConstraint("CK_SchoolRegistrationChallenges_SchoolCode_NotBlank", "LEN(LTRIM(RTRIM([SchoolCode]))) > 0");
                table.HasCheckConstraint("CK_SchoolRegistrationChallenges_SchoolName_NotBlank", "LEN(LTRIM(RTRIM([SchoolName]))) > 0");
                table.HasCheckConstraint("CK_SchoolRegistrationChallenges_Email_NotBlank", "[EmailAddress] IS NULL OR LEN(LTRIM(RTRIM([EmailAddress]))) > 0");
            });
            entity.HasKey(x => x.RegistrationId).HasName("PK_SchoolRegistrationChallenges");
            entity.Property(x => x.SchoolCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.NormalizedSchoolCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SchoolName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.EmailLookupHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.PhoneLookupHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.PhoneNumberEncrypted).HasColumnType("varbinary(512)");
            entity.Property(x => x.Username).HasMaxLength(64).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AdminPasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.OtpHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.AttemptCount).HasDefaultValue((byte)0);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.PhoneLookupHash, x.CreatedAtUtc })
                .HasDatabaseName("IX_SchoolRegistrationChallenges_Phone_CreatedAt");
            entity.HasIndex(x => new { x.EmailLookupHash, x.CreatedAtUtc })
                .HasDatabaseName("IX_SchoolRegistrationChallenges_Email_CreatedAt");
            entity.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_SchoolRegistrationChallenges_Expiry");
        });

        modelBuilder.Entity<StaffInvitation>(entity =>
        {
            entity.ToTable("StaffInvitations", table =>
                table.HasCheckConstraint("CK_StaffInvitations_Role", "[Role] IN ('Admin', 'Manager', 'Guard', 'Staff')"));
            entity.HasKey(x => x.StaffInvitationId).HasName("PK_StaffInvitations");
            entity.Property(x => x.Role).HasMaxLength(16).IsUnicode(false).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UQ_StaffInvitations_TokenHash");
            entity.HasIndex(x => new { x.SchoolId, x.ExpiresAtUtc }).HasDatabaseName("IX_StaffInvitations_School_Expiry");
            entity.HasOne(x => x.School).WithMany().HasForeignKey(x => x.SchoolId)
                .HasConstraintName("FK_StaffInvitations_Schools").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StaffUser>().WithMany().HasForeignKey(x => new { x.SchoolId, x.CreatedByStaffUserId })
                .HasPrincipalKey(x => new { x.SchoolId, x.StaffUserId }).HasConstraintName("FK_StaffInvitations_School_Creator")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountAuditLog>(entity =>
        {
            entity.ToTable("AccountAuditLogs", table =>
                table.HasCheckConstraint("CK_AccountAuditLogs_Action_NotBlank", "LEN(LTRIM(RTRIM([Action]))) > 0"));
            entity.HasKey(x => x.AccountAuditLogId).HasName("PK_AccountAuditLogs");
            entity.Property(x => x.Action).HasMaxLength(40).IsUnicode(false).IsRequired();
            entity.Property(x => x.TargetUsername).HasMaxLength(64);
            entity.Property(x => x.Details).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.SchoolId, x.CreatedAtUtc }).HasDatabaseName("IX_AccountAuditLogs_School_CreatedAt");
            entity.HasOne<School>().WithMany().HasForeignKey(x => x.SchoolId)
                .HasConstraintName("FK_AccountAuditLogs_Schools").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StaffUser>().WithMany().HasForeignKey(x => new { x.SchoolId, x.ActorStaffUserId })
                .HasPrincipalKey(x => new { x.SchoolId, x.StaffUserId }).HasConstraintName("FK_AccountAuditLogs_School_Actor")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StaffUser>().WithMany().HasForeignKey(x => new { x.SchoolId, x.TargetStaffUserId })
                .HasPrincipalKey(x => new { x.SchoolId, x.StaffUserId }).HasConstraintName("FK_AccountAuditLogs_School_Target")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.ToTable("Students", table =>
            {
                table.HasCheckConstraint("CK_Students_FullName_NotBlank", "LEN(LTRIM(RTRIM([FullName]))) > 0");
                table.HasCheckConstraint("CK_Students_RoomNumber_NotBlank", "LEN(LTRIM(RTRIM([RoomNumber]))) > 0");
                table.HasCheckConstraint("CK_Students_EmailAddress_NotBlank", "[EmailAddress] IS NULL OR LEN(LTRIM(RTRIM([EmailAddress]))) > 0");
            });
            entity.HasKey(x => x.StudentId).HasName("PK_Students");
            entity.Property(x => x.StudentCode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.RoomNumber).HasMaxLength(30).IsRequired();
            entity.Property(x => x.EmailAddress).HasMaxLength(320);
            entity.Property(x => x.PhoneEncrypted).HasColumnType("varbinary(512)");
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.SchoolId, x.StudentCode }).IsUnique().HasDatabaseName("UQ_Students_School_StudentCode");
            entity.HasAlternateKey(x => new { x.SchoolId, x.StudentId }).HasName("UQ_Students_School_Student");
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("Vehicles", table =>
                table.HasCheckConstraint("CK_Vehicles_LicensePlate_NotBlank", "LEN(LTRIM(RTRIM([LicensePlate]))) > 0"));
            entity.HasKey(x => x.VehicleId).HasName("PK_Vehicles");
            entity.Property(x => x.LicensePlate).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.SchoolId, x.LicensePlate }).IsUnique().HasDatabaseName("UQ_Vehicles_School_LicensePlate");
            entity.HasAlternateKey(x => new { x.SchoolId, x.VehicleId, x.StudentId }).HasName("UQ_Vehicles_School_Vehicle_Student");
            entity.HasOne(x => x.Student).WithMany(x => x.Vehicles).HasForeignKey(x => new { x.SchoolId, x.StudentId })
                .HasPrincipalKey(x => new { x.SchoolId, x.StudentId }).HasConstraintName("FK_Vehicles_School_Student")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParkingContract>(entity =>
        {
            entity.ToTable("ParkingContracts", table =>
            {
                table.HasCheckConstraint("CK_ParkingContracts_Status", "[Status] IN ('Active', 'Expired', 'Cancelled')");
                table.HasCheckConstraint("CK_ParkingContracts_DateRange", "[EndDate] >= [StartDate]");
                table.HasCheckConstraint("CK_ParkingContracts_CancelledAt", "(([Status] = 'Cancelled' AND [CancelledAtUtc] IS NOT NULL) OR ([Status] <> 'Cancelled' AND [CancelledAtUtc] IS NULL))");
            });
            entity.HasKey(x => x.ContractId).HasName("PK_ParkingContracts");
            entity.Property(x => x.Status).HasMaxLength(12).IsUnicode(false).HasDefaultValue(ContractStatuses.Active);
            entity.Property(x => x.StartDate).HasColumnType("date");
            entity.Property(x => x.EndDate).HasColumnType("date");
            entity.Property(x => x.CancellationNote).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasAlternateKey(x => new { x.SchoolId, x.ContractId, x.StudentId }).HasName("UQ_ParkingContracts_School_Contract_Student");
            entity.HasAlternateKey(x => new { x.SchoolId, x.ContractId, x.VehicleId, x.StudentId }).HasName("UQ_ParkingContracts_School_Contract_Vehicle_Student");
            entity.HasIndex(x => new { x.SchoolId, x.StudentId }).IsUnique().HasFilter("[Status] = 'Active'")
                .HasDatabaseName("UX_ParkingContracts_OneActivePerSchoolStudent");
            entity.HasIndex(x => new { x.SchoolId, x.Status, x.EndDate }).HasDatabaseName("IX_ParkingContracts_School_Status_EndDate")
                .IncludeProperties(x => new { x.StudentId, x.VehicleId, x.StartDate });
            entity.HasOne(x => x.Student).WithMany(x => x.Contracts).HasForeignKey(x => new { x.SchoolId, x.StudentId })
                .HasPrincipalKey(x => new { x.SchoolId, x.StudentId }).HasConstraintName("FK_ParkingContracts_School_Student")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Vehicle).WithMany(x => x.Contracts)
                .HasForeignKey(x => new { x.SchoolId, x.VehicleId, x.StudentId })
                .HasPrincipalKey(x => new { x.SchoolId, x.VehicleId, x.StudentId })
                .HasConstraintName("FK_ParkingContracts_School_Vehicle_Student").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.ToTable("NotificationLogs", table =>
            {
                table.HasCheckConstraint("CK_NotificationLogs_Type", "[NotificationType] IN ('ExpiryReminder', 'ExpiredNotice')");
                table.HasCheckConstraint("CK_NotificationLogs_Channel", "[Channel] IN ('SMS', 'Zalo', 'Email')");
                table.HasCheckConstraint("CK_NotificationLogs_Status", "[Status] IN ('Pending', 'Sent', 'Failed')");
                table.HasCheckConstraint("CK_NotificationLogs_Attempts", "[AttemptCount] <= 4");
                table.HasCheckConstraint("CK_NotificationLogs_SentAt", "(([Status] = 'Sent' AND [SentAtUtc] IS NOT NULL) OR ([Status] <> 'Sent' AND [SentAtUtc] IS NULL))");
            });
            entity.HasKey(x => x.NotificationLogId).HasName("PK_NotificationLogs");
            entity.Property(x => x.NotificationType).HasMaxLength(24).IsUnicode(false).IsRequired();
            entity.Property(x => x.Channel).HasMaxLength(12).IsUnicode(false).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(12).IsUnicode(false).IsRequired();
            entity.Property(x => x.DueDate).HasColumnType("date");
            entity.Property(x => x.AttemptCount).HasDefaultValue((byte)0);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(120);
            entity.Property(x => x.DestinationMasked).HasMaxLength(320);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasOne(x => x.Contract).WithMany().HasForeignKey(x => new { x.SchoolId, x.ContractId, x.StudentId })
                .HasPrincipalKey(x => new { x.SchoolId, x.ContractId, x.StudentId })
                .HasConstraintName("FK_NotificationLogs_School_Contract_Student").OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.SchoolId, x.ContractId, x.DueDate }).IsUnique()
                .HasFilter("[NotificationType] = 'ExpiryReminder'")
                .HasDatabaseName("UX_NotificationLogs_School_ExpiryReminder_PerDueDate");
            entity.HasIndex(x => new { x.SchoolId, x.ContractId, x.CreatedAtUtc }).HasDatabaseName("IX_NotificationLogs_School_Contract_CreatedAt");
        });

        modelBuilder.Entity<VehicleChangeLog>(entity =>
        {
            entity.ToTable("VehicleChangeLogs", table =>
                table.HasCheckConstraint("CK_VehicleChangeLogs_PlateChanged", "[OldLicensePlate] <> [NewLicensePlate]"));
            entity.HasKey(x => x.VehicleChangeLogId).HasName("PK_VehicleChangeLogs");
            entity.Property(x => x.OldLicensePlate).HasMaxLength(20).IsRequired();
            entity.Property(x => x.NewLicensePlate).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ChangedBy).HasMaxLength(100);
            entity.Property(x => x.ChangeNote).HasMaxLength(500);
            entity.Property(x => x.ChangedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.SchoolId, x.ContractId, x.ChangedAtUtc }).HasDatabaseName("IX_VehicleChangeLogs_School_Contract_ChangedAt");
            entity.HasOne(x => x.Contract).WithMany(x => x.VehicleChangeLogs)
                .HasForeignKey(x => new { x.SchoolId, x.ContractId, x.VehicleId, x.StudentId })
                .HasPrincipalKey(x => new { x.SchoolId, x.ContractId, x.VehicleId, x.StudentId })
                .HasConstraintName("FK_VehicleChangeLogs_School_Contract_Vehicle_Student").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
