/*
    MPS (Monthly Parking System) - SQL Server LocalDB bootstrap
    Fresh or existing database:
      sqlcmd -S "(localdb)\MSSQLLocalDB" -E -i database\Init_Database.sql
      sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i database\Migrate_Epic5_MultiTenant.sql
      sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -i database\Migrate_Epic5_1_SelfServiceRegistration.sql

    Students.PhoneEncrypted must contain application-encrypted AES-256 bytes;
    never store a plaintext phone number in this database.
    LocalDB does not support SQL Server TDE. Enable TDE on a supported SQL
    Server deployment separately, using a certificate backed up securely.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

USE [master];
GO

IF DB_ID(N'MPS') IS NULL
BEGIN
    CREATE DATABASE [MPS];
END;
GO

USE [MPS];
GO

IF OBJECT_ID(N'dbo.StaffUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffUsers
    (
        StaffUserId       int IDENTITY(1,1) NOT NULL,
        Username          nvarchar(64) NOT NULL,
        NormalizedUsername nvarchar(64) NOT NULL,
        PasswordHash      nvarchar(512) NOT NULL,
        Role              varchar(16) NOT NULL,
        IsActive          bit NOT NULL CONSTRAINT DF_StaffUsers_IsActive DEFAULT (1),
        AccessFailedCount tinyint NOT NULL CONSTRAINT DF_StaffUsers_AccessFailedCount DEFAULT (0),
        LockoutEndUtc     datetime2(0) NULL,
        CreatedAtUtc      datetime2(0) NOT NULL CONSTRAINT DF_StaffUsers_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        LastLoginAtUtc    datetime2(0) NULL,
        CONSTRAINT PK_StaffUsers PRIMARY KEY CLUSTERED (StaffUserId),
        CONSTRAINT UQ_StaffUsers_NormalizedUsername UNIQUE (NormalizedUsername),
        CONSTRAINT CK_StaffUsers_Role CHECK (Role IN ('Admin', 'Manager', 'Guard', 'Staff')),
        CONSTRAINT CK_StaffUsers_AccessFailedCount CHECK (AccessFailedCount <= 5)
    );
END;
GO

IF OBJECT_ID(N'dbo.Students', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Students
    (
        StudentId       int IDENTITY(1,1) NOT NULL,
        StudentCode     nvarchar(32) NOT NULL,
        FullName        nvarchar(150) NOT NULL,
        RoomNumber      nvarchar(30) NOT NULL,
        EmailAddress    nvarchar(320) NULL,
        PhoneEncrypted  varbinary(512) NULL,
        CreatedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_Students_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_Students_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_Students PRIMARY KEY CLUSTERED (StudentId),
        CONSTRAINT UQ_Students_StudentCode UNIQUE (StudentCode),
        CONSTRAINT CK_Students_FullName_NotBlank CHECK (LEN(LTRIM(RTRIM(FullName))) > 0),
        CONSTRAINT CK_Students_RoomNumber_NotBlank CHECK (LEN(LTRIM(RTRIM(RoomNumber))) > 0),
        CONSTRAINT CK_Students_EmailAddress_NotBlank CHECK (EmailAddress IS NULL OR LEN(LTRIM(RTRIM(EmailAddress))) > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.Vehicles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Vehicles
    (
        VehicleId       int IDENTITY(1,1) NOT NULL,
        StudentId       int NOT NULL,
        LicensePlate    nvarchar(20) NOT NULL,
        CreatedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_Vehicles_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_Vehicles_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_Vehicles PRIMARY KEY CLUSTERED (VehicleId),
        CONSTRAINT UQ_Vehicles_LicensePlate UNIQUE (LicensePlate),
        CONSTRAINT UQ_Vehicles_Vehicle_Student UNIQUE (VehicleId, StudentId),
        CONSTRAINT FK_Vehicles_Students FOREIGN KEY (StudentId) REFERENCES dbo.Students(StudentId),
        CONSTRAINT CK_Vehicles_LicensePlate_NotBlank CHECK (LEN(LTRIM(RTRIM(LicensePlate))) > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.ParkingContracts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ParkingContracts
    (
        ContractId      bigint IDENTITY(1,1) NOT NULL,
        StudentId       int NOT NULL,
        VehicleId       int NOT NULL,
        StartDate       date NOT NULL,
        EndDate         date NOT NULL,
        Status          varchar(12) NOT NULL CONSTRAINT DF_ParkingContracts_Status DEFAULT ('Active'),
        CancelledAtUtc  datetime2(0) NULL,
        CancellationNote nvarchar(500) NULL,
        CreatedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_ParkingContracts_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc    datetime2(0) NOT NULL CONSTRAINT DF_ParkingContracts_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ParkingContracts PRIMARY KEY CLUSTERED (ContractId),
        CONSTRAINT UQ_ParkingContracts_Contract_Student UNIQUE (ContractId, StudentId),
        CONSTRAINT UQ_ParkingContracts_Contract_Vehicle_Student UNIQUE (ContractId, VehicleId, StudentId),
        CONSTRAINT FK_ParkingContracts_Students FOREIGN KEY (StudentId) REFERENCES dbo.Students(StudentId),
        CONSTRAINT FK_ParkingContracts_Vehicles_Student FOREIGN KEY (VehicleId, StudentId)
            REFERENCES dbo.Vehicles(VehicleId, StudentId),
        CONSTRAINT CK_ParkingContracts_Status CHECK (Status IN ('Active', 'Expired', 'Cancelled', 'Pending')),
        CONSTRAINT CK_ParkingContracts_DateRange CHECK (EndDate >= StartDate),
        CONSTRAINT CK_ParkingContracts_CancelledAt CHECK
            ((Status = 'Cancelled' AND CancelledAtUtc IS NOT NULL) OR (Status <> 'Cancelled' AND CancelledAtUtc IS NULL))
    );
END;
GO

/* The API/worker must set Status='Expired' when a contract expires. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ParkingContracts') AND name = N'UX_ParkingContracts_OneActivePerStudent')
BEGIN
    CREATE UNIQUE INDEX UX_ParkingContracts_OneActivePerStudent
        ON dbo.ParkingContracts(StudentId)
        WHERE Status = 'Active';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ParkingContracts') AND name = N'IX_ParkingContracts_Status_EndDate')
BEGIN
    CREATE INDEX IX_ParkingContracts_Status_EndDate
        ON dbo.ParkingContracts(Status, EndDate)
        INCLUDE (StudentId, VehicleId, StartDate);
END;
GO

IF OBJECT_ID(N'dbo.NotificationLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationLogs
    (
        NotificationLogId bigint IDENTITY(1,1) NOT NULL,
        ContractId         bigint NOT NULL,
        StudentId          int NOT NULL,
        NotificationType   varchar(24) NOT NULL,
        Channel            varchar(12) NOT NULL,
        Status             varchar(12) NOT NULL,
        DueDate             date NOT NULL,
        AttemptCount       tinyint NOT NULL CONSTRAINT DF_NotificationLogs_AttemptCount DEFAULT (0),
        ProviderMessageId  nvarchar(120) NULL,
        DestinationMasked nvarchar(320) NULL,
        ErrorMessage       nvarchar(1000) NULL,
        CreatedAtUtc       datetime2(0) NOT NULL CONSTRAINT DF_NotificationLogs_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        SentAtUtc          datetime2(0) NULL,
        CONSTRAINT PK_NotificationLogs PRIMARY KEY CLUSTERED (NotificationLogId),
        CONSTRAINT FK_NotificationLogs_Contract_Student FOREIGN KEY (ContractId, StudentId)
            REFERENCES dbo.ParkingContracts(ContractId, StudentId),
        CONSTRAINT CK_NotificationLogs_Type CHECK (NotificationType IN ('ExpiryReminder', 'ExpiredNotice')),
        CONSTRAINT CK_NotificationLogs_Channel CHECK (Channel IN ('SMS', 'Zalo', 'Email')),
        CONSTRAINT CK_NotificationLogs_Status CHECK (Status IN ('Pending', 'Sent', 'Failed')),
        CONSTRAINT CK_NotificationLogs_Attempts CHECK (AttemptCount <= 4),
        CONSTRAINT CK_NotificationLogs_SentAt CHECK ((Status = 'Sent' AND SentAtUtc IS NOT NULL) OR (Status <> 'Sent' AND SentAtUtc IS NULL))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'UX_NotificationLogs_ExpiryReminder_PerDueDate')
BEGIN
    CREATE UNIQUE INDEX UX_NotificationLogs_ExpiryReminder_PerDueDate
        ON dbo.NotificationLogs(ContractId, DueDate)
        WHERE NotificationType = 'ExpiryReminder';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'IX_NotificationLogs_Contract_CreatedAt')
BEGIN
    CREATE INDEX IX_NotificationLogs_Contract_CreatedAt
        ON dbo.NotificationLogs(ContractId, CreatedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'dbo.VehicleChangeLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VehicleChangeLogs
    (
        VehicleChangeLogId bigint IDENTITY(1,1) NOT NULL,
        ContractId          bigint NOT NULL,
        VehicleId           int NOT NULL,
        StudentId           int NOT NULL,
        OldLicensePlate     nvarchar(20) NOT NULL,
        NewLicensePlate     nvarchar(20) NOT NULL,
        ChangedBy           nvarchar(100) NULL,
        ChangedAtUtc        datetime2(0) NOT NULL CONSTRAINT DF_VehicleChangeLogs_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        ChangeNote          nvarchar(500) NULL,
        CONSTRAINT PK_VehicleChangeLogs PRIMARY KEY CLUSTERED (VehicleChangeLogId),
        CONSTRAINT FK_VehicleChangeLogs_Contract_Vehicle_Student FOREIGN KEY (ContractId, VehicleId, StudentId)
            REFERENCES dbo.ParkingContracts(ContractId, VehicleId, StudentId),
        CONSTRAINT CK_VehicleChangeLogs_PlateChanged CHECK (OldLicensePlate <> NewLicensePlate)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.VehicleChangeLogs') AND name = N'IX_VehicleChangeLogs_Contract_ChangedAt')
BEGIN
    CREATE INDEX IX_VehicleChangeLogs_Contract_ChangedAt
        ON dbo.VehicleChangeLogs(ContractId, ChangedAtUtc DESC);
END;
GO


IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        AuditLogId bigint IDENTITY(1,1) NOT NULL,
        SchoolId int NOT NULL,
        EntityName varchar(50) NOT NULL,
        EntityId bigint NOT NULL,
        Action varchar(20) NOT NULL,
        OldValues nvarchar(max) NULL,
        NewValues nvarchar(max) NULL,
        ChangedBy nvarchar(100) NULL,
        ChangedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_AuditLogs_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AuditLogs PRIMARY KEY CLUSTERED (AuditLogId)
    );
END;
GO

PRINT N'MPS database schema is ready.';
GO
