/* Add delivery idempotency date and retry capacity to an existing MPS database. */
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

IF COL_LENGTH(N'dbo.NotificationLogs', N'DueDate') IS NULL
BEGIN
    ALTER TABLE dbo.NotificationLogs ADD DueDate date NULL;
END;
GO

UPDATE dbo.NotificationLogs SET DueDate = CONVERT(date, CreatedAtUtc) WHERE DueDate IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'DueDate' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.NotificationLogs ALTER COLUMN DueDate date NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.CK_NotificationLogs_Attempts', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.NotificationLogs DROP CONSTRAINT CK_NotificationLogs_Attempts;
END;
GO

ALTER TABLE dbo.NotificationLogs WITH CHECK ADD CONSTRAINT CK_NotificationLogs_Attempts CHECK (AttemptCount <= 4);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.NotificationLogs') AND name = N'UX_NotificationLogs_ExpiryReminder_PerDueDate')
BEGIN
    CREATE UNIQUE INDEX UX_NotificationLogs_ExpiryReminder_PerDueDate
        ON dbo.NotificationLogs(ContractId, DueDate)
        WHERE NotificationType = 'ExpiryReminder';
END;
GO

PRINT N'Epic 3 notification schema is ready.';
GO
