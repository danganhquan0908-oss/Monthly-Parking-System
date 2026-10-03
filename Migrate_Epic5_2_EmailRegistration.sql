/*
    Epic 5.2: replace SMS-based school-registration OTP with email OTP.
    Apply after Migrate_Epic5_1_SelfServiceRegistration.sql. Existing data is preserved.
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

IF COL_LENGTH(N'dbo.StaffUsers', N'EmailAddress') IS NULL
    ALTER TABLE dbo.StaffUsers ADD EmailAddress nvarchar(320) NULL;
IF COL_LENGTH(N'dbo.StaffUsers', N'EmailVerified') IS NULL
    ALTER TABLE dbo.StaffUsers ADD EmailVerified bit NOT NULL
        CONSTRAINT DF_StaffUsers_EmailVerified DEFAULT (0) WITH VALUES;
GO

IF COL_LENGTH(N'dbo.SchoolRegistrationChallenges', N'EmailAddress') IS NULL
    ALTER TABLE dbo.SchoolRegistrationChallenges ADD EmailAddress nvarchar(320) NULL;
IF COL_LENGTH(N'dbo.SchoolRegistrationChallenges', N'EmailLookupHash') IS NULL
    ALTER TABLE dbo.SchoolRegistrationChallenges ADD EmailLookupHash varchar(64) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.SchoolRegistrationChallenges')
             AND name = N'IX_SchoolRegistrationChallenges_Phone_CreatedAt')
    DROP INDEX IX_SchoolRegistrationChallenges_Phone_CreatedAt ON dbo.SchoolRegistrationChallenges;
ALTER TABLE dbo.SchoolRegistrationChallenges ALTER COLUMN PhoneLookupHash varchar(64) NULL;
ALTER TABLE dbo.SchoolRegistrationChallenges ALTER COLUMN PhoneNumberEncrypted varbinary(512) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.SchoolRegistrationChallenges')
                 AND name = N'CK_SchoolRegistrationChallenges_Email_NotBlank')
    ALTER TABLE dbo.SchoolRegistrationChallenges ADD CONSTRAINT CK_SchoolRegistrationChallenges_Email_NotBlank
        CHECK (EmailAddress IS NULL OR LEN(LTRIM(RTRIM(EmailAddress))) > 0);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.SchoolRegistrationChallenges')
                 AND name = N'IX_SchoolRegistrationChallenges_Phone_CreatedAt')
    CREATE INDEX IX_SchoolRegistrationChallenges_Phone_CreatedAt
        ON dbo.SchoolRegistrationChallenges(PhoneLookupHash, CreatedAtUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.SchoolRegistrationChallenges')
                 AND name = N'IX_SchoolRegistrationChallenges_Email_CreatedAt')
    CREATE INDEX IX_SchoolRegistrationChallenges_Email_CreatedAt
        ON dbo.SchoolRegistrationChallenges(EmailLookupHash, CreatedAtUtc DESC);
GO
