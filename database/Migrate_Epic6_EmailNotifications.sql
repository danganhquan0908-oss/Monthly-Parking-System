/* Epic 6: move student expiry reminders from SMS/Zalo gateway delivery to email. */
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

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.Students', N'EmailAddress') IS NULL
        ALTER TABLE dbo.Students ADD EmailAddress nvarchar(320) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.Students')
                     AND name = N'CK_Students_EmailAddress_NotBlank')
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Students WITH CHECK ADD CONSTRAINT CK_Students_EmailAddress_NotBlank
            CHECK (EmailAddress IS NULL OR LEN(LTRIM(RTRIM(EmailAddress))) > 0);';

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.NotificationLogs')
                 AND name = N'CK_NotificationLogs_Channel')
        ALTER TABLE dbo.NotificationLogs DROP CONSTRAINT CK_NotificationLogs_Channel;

    ALTER TABLE dbo.NotificationLogs WITH CHECK ADD CONSTRAINT CK_NotificationLogs_Channel
        CHECK (Channel IN ('SMS', 'Zalo', 'Email'));

    ALTER TABLE dbo.NotificationLogs ALTER COLUMN DestinationMasked nvarchar(320) NULL;

    /* Keep historical delivery channels intact; reroute only undelivered work. */
    UPDATE dbo.NotificationLogs
       SET Channel = 'Email'
     WHERE Status = 'Pending' AND Channel IN ('SMS', 'Zalo');

    COMMIT TRANSACTION;
    PRINT N'Epic 6 email notification schema is ready. Existing student email addresses remain NULL until staff add them.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
