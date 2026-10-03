/* Create the internal staff credential and role table for Epic 4. */
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

PRINT N'Epic 4 staff authentication schema is ready.';
GO
