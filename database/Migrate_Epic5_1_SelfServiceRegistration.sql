/*
    Epic 5.1: public self-service school registration with SMS OTP verification.
    Apply after Migrate_Epic5_MultiTenant.sql. Existing data is left unchanged.
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

IF COL_LENGTH(N'dbo.StaffUsers', N'PhoneNumberEncrypted') IS NULL
    ALTER TABLE dbo.StaffUsers ADD PhoneNumberEncrypted varbinary(512) NULL;
GO

IF OBJECT_ID(N'dbo.SchoolRegistrationChallenges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchoolRegistrationChallenges
    (
        RegistrationId uniqueidentifier NOT NULL CONSTRAINT PK_SchoolRegistrationChallenges PRIMARY KEY,
        SchoolCode nvarchar(32) NOT NULL,
        NormalizedSchoolCode nvarchar(32) NOT NULL,
        SchoolName nvarchar(150) NOT NULL,
        PhoneLookupHash varchar(64) NOT NULL,
        PhoneNumberEncrypted varbinary(512) NOT NULL,
        Username nvarchar(64) NOT NULL,
        NormalizedUsername nvarchar(64) NOT NULL,
        AdminPasswordHash nvarchar(512) NOT NULL,
        OtpHash varchar(64) NOT NULL,
        AttemptCount tinyint NOT NULL CONSTRAINT DF_SchoolRegistrationChallenges_AttemptCount DEFAULT (0),
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_SchoolRegistrationChallenges_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ExpiresAtUtc datetime2(0) NOT NULL,
        ConsumedAtUtc datetime2(0) NULL,
        CONSTRAINT CK_SchoolRegistrationChallenges_Attempts CHECK (AttemptCount <= 5),
        CONSTRAINT CK_SchoolRegistrationChallenges_SchoolCode_NotBlank CHECK (LEN(LTRIM(RTRIM(SchoolCode))) > 0),
        CONSTRAINT CK_SchoolRegistrationChallenges_SchoolName_NotBlank CHECK (LEN(LTRIM(RTRIM(SchoolName))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SchoolRegistrationChallenges') AND name = N'IX_SchoolRegistrationChallenges_Phone_CreatedAt')
    CREATE INDEX IX_SchoolRegistrationChallenges_Phone_CreatedAt
        ON dbo.SchoolRegistrationChallenges(PhoneLookupHash, CreatedAtUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SchoolRegistrationChallenges') AND name = N'IX_SchoolRegistrationChallenges_Expiry')
    CREATE INDEX IX_SchoolRegistrationChallenges_Expiry ON dbo.SchoolRegistrationChallenges(ExpiresAtUtc);
GO
