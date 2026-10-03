/*
    Epic 5: Centralized, school-scoped MPS.
    Safe to run after Init_Database.sql on an existing single-school database.
    Existing records and accounts are assigned to MPS-DEFAULT.
*/
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
USE [MPS];
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Schools', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Schools
    (
        SchoolId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Schools PRIMARY KEY,
        Code nvarchar(32) NOT NULL,
        NormalizedCode nvarchar(32) NOT NULL,
        Name nvarchar(150) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Schools_IsActive DEFAULT (1),
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_Schools_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Schools_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT UQ_Schools_NormalizedCode UNIQUE (NormalizedCode)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT')
    INSERT dbo.Schools (Code, NormalizedCode, Name) VALUES (N'MPS-DEFAULT', N'MPS-DEFAULT', N'Ký túc xá mặc định');

IF COL_LENGTH(N'dbo.StaffUsers', N'SchoolId') IS NULL ALTER TABLE dbo.StaffUsers ADD SchoolId int NULL;
IF COL_LENGTH(N'dbo.Students', N'SchoolId') IS NULL ALTER TABLE dbo.Students ADD SchoolId int NULL;
IF COL_LENGTH(N'dbo.Vehicles', N'SchoolId') IS NULL ALTER TABLE dbo.Vehicles ADD SchoolId int NULL;
IF COL_LENGTH(N'dbo.ParkingContracts', N'SchoolId') IS NULL ALTER TABLE dbo.ParkingContracts ADD SchoolId int NULL;
IF COL_LENGTH(N'dbo.NotificationLogs', N'SchoolId') IS NULL ALTER TABLE dbo.NotificationLogs ADD SchoolId int NULL;
IF COL_LENGTH(N'dbo.VehicleChangeLogs', N'SchoolId') IS NULL ALTER TABLE dbo.VehicleChangeLogs ADD SchoolId int NULL;
GO

UPDATE dbo.StaffUsers SET SchoolId = (SELECT SchoolId FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT') WHERE SchoolId IS NULL;
UPDATE dbo.Students SET SchoolId = (SELECT SchoolId FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT') WHERE SchoolId IS NULL;
UPDATE dbo.Vehicles SET SchoolId = (SELECT SchoolId FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT') WHERE SchoolId IS NULL;
UPDATE dbo.ParkingContracts SET SchoolId = (SELECT SchoolId FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT') WHERE SchoolId IS NULL;
UPDATE dbo.NotificationLogs SET SchoolId = (SELECT SchoolId FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT') WHERE SchoolId IS NULL;
UPDATE dbo.VehicleChangeLogs SET SchoolId = (SELECT SchoolId FROM dbo.Schools WHERE NormalizedCode = N'MPS-DEFAULT') WHERE SchoolId IS NULL;

IF OBJECT_ID(N'dbo.StaffInvitations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffInvitations
    (
        StaffInvitationId uniqueidentifier NOT NULL CONSTRAINT PK_StaffInvitations PRIMARY KEY,
        SchoolId int NOT NULL,
        Role varchar(16) NOT NULL,
        TokenHash varchar(64) NOT NULL,
        CreatedByStaffUserId int NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_StaffInvitations_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ExpiresAtUtc datetime2(0) NOT NULL,
        AcceptedAtUtc datetime2(0) NULL,
        RevokedAtUtc datetime2(0) NULL,
        CONSTRAINT CK_StaffInvitations_Role CHECK (Role IN ('Admin', 'Manager', 'Guard', 'Staff')),
        CONSTRAINT FK_StaffInvitations_Schools FOREIGN KEY (SchoolId) REFERENCES dbo.Schools(SchoolId)
    );
END;

/* Replace global identity constraints and old foreign keys with tenant-aware ones. */
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StaffInvitations_Schools')
    ALTER TABLE dbo.StaffInvitations DROP CONSTRAINT FK_StaffInvitations_Schools;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StaffInvitations_School_Creator')
    ALTER TABLE dbo.StaffInvitations DROP CONSTRAINT FK_StaffInvitations_School_Creator;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Vehicles_Students')
    ALTER TABLE dbo.Vehicles DROP CONSTRAINT FK_Vehicles_Students;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ParkingContracts_Students')
    ALTER TABLE dbo.ParkingContracts DROP CONSTRAINT FK_ParkingContracts_Students;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ParkingContracts_Vehicles_Student')
    ALTER TABLE dbo.ParkingContracts DROP CONSTRAINT FK_ParkingContracts_Vehicles_Student;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NotificationLogs_Contract_Student')
    ALTER TABLE dbo.NotificationLogs DROP CONSTRAINT FK_NotificationLogs_Contract_Student;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VehicleChangeLogs_Contract_Vehicle_Student')
    ALTER TABLE dbo.VehicleChangeLogs DROP CONSTRAINT FK_VehicleChangeLogs_Contract_Vehicle_Student;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_StaffUsers_NormalizedUsername') ALTER TABLE dbo.StaffUsers DROP CONSTRAINT UQ_StaffUsers_NormalizedUsername;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_Students_StudentCode') ALTER TABLE dbo.Students DROP CONSTRAINT UQ_Students_StudentCode;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_Vehicles_LicensePlate') ALTER TABLE dbo.Vehicles DROP CONSTRAINT UQ_Vehicles_LicensePlate;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_Vehicles_Vehicle_Student') ALTER TABLE dbo.Vehicles DROP CONSTRAINT UQ_Vehicles_Vehicle_Student;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_ParkingContracts_Contract_Student') ALTER TABLE dbo.ParkingContracts DROP CONSTRAINT UQ_ParkingContracts_Contract_Student;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_ParkingContracts_Contract_Vehicle_Student') ALTER TABLE dbo.ParkingContracts DROP CONSTRAINT UQ_ParkingContracts_Contract_Vehicle_Student;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ParkingContracts') AND name = N'UX_ParkingContracts_OneActivePerStudent')
    DROP INDEX UX_ParkingContracts_OneActivePerStudent ON dbo.ParkingContracts;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'UX_NotificationLogs_ExpiryReminder_PerDueDate')
    DROP INDEX UX_NotificationLogs_ExpiryReminder_PerDueDate ON dbo.NotificationLogs;

ALTER TABLE dbo.StaffUsers ALTER COLUMN SchoolId int NOT NULL;
ALTER TABLE dbo.Students ALTER COLUMN SchoolId int NOT NULL;
ALTER TABLE dbo.Vehicles ALTER COLUMN SchoolId int NOT NULL;
ALTER TABLE dbo.ParkingContracts ALTER COLUMN SchoolId int NOT NULL;
ALTER TABLE dbo.NotificationLogs ALTER COLUMN SchoolId int NOT NULL;
ALTER TABLE dbo.VehicleChangeLogs ALTER COLUMN SchoolId int NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_StaffUsers_School_User')
    ALTER TABLE dbo.StaffUsers ADD CONSTRAINT UQ_StaffUsers_School_User UNIQUE (SchoolId, StaffUserId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffUsers') AND name = N'UQ_StaffUsers_School_NormalizedUsername')
    CREATE UNIQUE INDEX UQ_StaffUsers_School_NormalizedUsername ON dbo.StaffUsers(SchoolId, NormalizedUsername);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Students') AND name = N'UQ_Students_School_StudentCode')
    CREATE UNIQUE INDEX UQ_Students_School_StudentCode ON dbo.Students(SchoolId, StudentCode);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_Students_School_Student')
    ALTER TABLE dbo.Students ADD CONSTRAINT UQ_Students_School_Student UNIQUE (SchoolId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Vehicles') AND name = N'UQ_Vehicles_School_LicensePlate')
    CREATE UNIQUE INDEX UQ_Vehicles_School_LicensePlate ON dbo.Vehicles(SchoolId, LicensePlate);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_Vehicles_School_Vehicle_Student')
    ALTER TABLE dbo.Vehicles ADD CONSTRAINT UQ_Vehicles_School_Vehicle_Student UNIQUE (SchoolId, VehicleId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_ParkingContracts_School_Contract_Student')
    ALTER TABLE dbo.ParkingContracts ADD CONSTRAINT UQ_ParkingContracts_School_Contract_Student UNIQUE (SchoolId, ContractId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_ParkingContracts_School_Contract_Vehicle_Student')
    ALTER TABLE dbo.ParkingContracts ADD CONSTRAINT UQ_ParkingContracts_School_Contract_Vehicle_Student UNIQUE (SchoolId, ContractId, VehicleId, StudentId);

IF OBJECT_ID(N'dbo.AccountAuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountAuditLogs
    (
        AccountAuditLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountAuditLogs PRIMARY KEY,
        SchoolId int NOT NULL,
        ActorStaffUserId int NULL,
        TargetStaffUserId int NULL,
        Action varchar(40) NOT NULL,
        TargetUsername nvarchar(64) NULL,
        Details nvarchar(500) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_AccountAuditLogs_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AccountAuditLogs_Action_NotBlank CHECK (LEN(LTRIM(RTRIM(Action))) > 0),
        CONSTRAINT FK_AccountAuditLogs_Schools FOREIGN KEY (SchoolId) REFERENCES dbo.Schools(SchoolId),
        CONSTRAINT FK_AccountAuditLogs_School_Actor FOREIGN KEY (SchoolId, ActorStaffUserId) REFERENCES dbo.StaffUsers(SchoolId, StaffUserId),
        CONSTRAINT FK_AccountAuditLogs_School_Target FOREIGN KEY (SchoolId, TargetStaffUserId) REFERENCES dbo.StaffUsers(SchoolId, StaffUserId)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StaffUsers_Schools')
    ALTER TABLE dbo.StaffUsers WITH CHECK ADD CONSTRAINT FK_StaffUsers_Schools FOREIGN KEY (SchoolId) REFERENCES dbo.Schools(SchoolId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StaffInvitations_Schools')
    ALTER TABLE dbo.StaffInvitations WITH CHECK ADD CONSTRAINT FK_StaffInvitations_Schools FOREIGN KEY (SchoolId) REFERENCES dbo.Schools(SchoolId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StaffInvitations_School_Creator')
    ALTER TABLE dbo.StaffInvitations WITH CHECK ADD CONSTRAINT FK_StaffInvitations_School_Creator
        FOREIGN KEY (SchoolId, CreatedByStaffUserId) REFERENCES dbo.StaffUsers(SchoolId, StaffUserId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Vehicles_School_Student')
    ALTER TABLE dbo.Vehicles WITH CHECK ADD CONSTRAINT FK_Vehicles_School_Student
        FOREIGN KEY (SchoolId, StudentId) REFERENCES dbo.Students(SchoolId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ParkingContracts_School_Student')
    ALTER TABLE dbo.ParkingContracts WITH CHECK ADD CONSTRAINT FK_ParkingContracts_School_Student
        FOREIGN KEY (SchoolId, StudentId) REFERENCES dbo.Students(SchoolId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ParkingContracts_School_Vehicle_Student')
    ALTER TABLE dbo.ParkingContracts WITH CHECK ADD CONSTRAINT FK_ParkingContracts_School_Vehicle_Student
        FOREIGN KEY (SchoolId, VehicleId, StudentId) REFERENCES dbo.Vehicles(SchoolId, VehicleId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_NotificationLogs_School_Contract_Student')
    ALTER TABLE dbo.NotificationLogs WITH CHECK ADD CONSTRAINT FK_NotificationLogs_School_Contract_Student
        FOREIGN KEY (SchoolId, ContractId, StudentId) REFERENCES dbo.ParkingContracts(SchoolId, ContractId, StudentId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_VehicleChangeLogs_School_Contract_Vehicle_Student')
    ALTER TABLE dbo.VehicleChangeLogs WITH CHECK ADD CONSTRAINT FK_VehicleChangeLogs_School_Contract_Vehicle_Student
        FOREIGN KEY (SchoolId, ContractId, VehicleId, StudentId)
        REFERENCES dbo.ParkingContracts(SchoolId, ContractId, VehicleId, StudentId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ParkingContracts') AND name = N'UX_ParkingContracts_OneActivePerSchoolStudent')
    CREATE UNIQUE INDEX UX_ParkingContracts_OneActivePerSchoolStudent
        ON dbo.ParkingContracts(SchoolId, StudentId) WHERE Status = 'Active';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ParkingContracts') AND name = N'IX_ParkingContracts_School_Status_EndDate')
    CREATE INDEX IX_ParkingContracts_School_Status_EndDate
        ON dbo.ParkingContracts(SchoolId, Status, EndDate) INCLUDE (StudentId, VehicleId, StartDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'UX_NotificationLogs_School_ExpiryReminder_PerDueDate')
    CREATE UNIQUE INDEX UX_NotificationLogs_School_ExpiryReminder_PerDueDate
        ON dbo.NotificationLogs(SchoolId, ContractId, DueDate) WHERE NotificationType = 'ExpiryReminder';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'IX_NotificationLogs_School_Contract_CreatedAt')
    CREATE INDEX IX_NotificationLogs_School_Contract_CreatedAt
        ON dbo.NotificationLogs(SchoolId, ContractId, CreatedAtUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.VehicleChangeLogs') AND name = N'IX_VehicleChangeLogs_School_Contract_ChangedAt')
    CREATE INDEX IX_VehicleChangeLogs_School_Contract_ChangedAt
        ON dbo.VehicleChangeLogs(SchoolId, ContractId, ChangedAtUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffInvitations') AND name = N'UQ_StaffInvitations_TokenHash')
    CREATE UNIQUE INDEX UQ_StaffInvitations_TokenHash ON dbo.StaffInvitations(TokenHash);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffInvitations') AND name = N'IX_StaffInvitations_School_Expiry')
    CREATE INDEX IX_StaffInvitations_School_Expiry ON dbo.StaffInvitations(SchoolId, ExpiresAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccountAuditLogs') AND name = N'IX_AccountAuditLogs_School_CreatedAt')
    CREATE INDEX IX_AccountAuditLogs_School_CreatedAt ON dbo.AccountAuditLogs(SchoolId, CreatedAtUtc DESC);

COMMIT TRANSACTION;
PRINT N'Epic 5 multi-school schema migration completed; existing rows belong to MPS-DEFAULT.';
GO
