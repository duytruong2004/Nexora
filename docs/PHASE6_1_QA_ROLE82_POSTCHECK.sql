/*
    NEXORA-P61-QA-ROLE82 — READ-ONLY POSTCHECK

    Intended to be run only after a separately approved remediation attempt,
    from a NEW connection opened after the attempt, targeting
    PC01\SQLEXPRESS / NEXORA_IntegrationTests. Close the execution window/session
    used for remediation, open a new SQL connection, verify its target, then run
    this read-only script. SQL cannot prove that the operator used a new client
    connection, so this is an operator precondition.
    This script contains no DML or DDL and has not been executed in this turn.
    It returns aggregate counts and a pass indicator only; it never selects
    password hashes, tokens, security stamps, email, or other personal data.
    PostcheckPassed=1 proves database state only; it does not prove Admin login.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SET LOCK_TIMEOUT 15000;

DECLARE @ExpectedServer nvarchar(128) = N'PC01\SQLEXPRESS';
DECLARE @ExpectedDatabase sysname = N'NEXORA_IntegrationTests';
DECLARE @UserId int = 82;
DECLARE @ExpectedUsers bigint = 6;
DECLARE @ExpectedRoles bigint = 4;
DECLARE @ExpectedMappings bigint = 6;

BEGIN TRY
    DECLARE @ActualServer nvarchar(128) = CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'));
    IF @ActualServer IS NULL
       OR DATALENGTH(@ActualServer) <> DATALENGTH(@ExpectedServer)
       OR @ActualServer COLLATE Latin1_General_100_BIN2 <> @ExpectedServer COLLATE Latin1_General_100_BIN2
        THROW 51300, 'Connected SQL Server does not match the approved QA instance.', 1;

    DECLARE @ActualDatabase sysname = DB_NAME();
    IF @ActualDatabase IS NULL
       OR DATALENGTH(@ActualDatabase) <> DATALENGTH(@ExpectedDatabase)
       OR @ActualDatabase COLLATE Latin1_General_100_BIN2 <> @ExpectedDatabase COLLATE Latin1_General_100_BIN2
        THROW 51301, 'Current database does not match the approved QA database.', 1;

    DECLARE @DatabaseStatus nvarchar(60) = CONVERT(nvarchar(60), DATABASEPROPERTYEX(@ActualDatabase, 'Status'));
    IF @DatabaseStatus IS NULL
       OR DATALENGTH(@DatabaseStatus) <> DATALENGTH(N'ONLINE')
       OR @DatabaseStatus COLLATE Latin1_General_100_BIN2 <> N'ONLINE' COLLATE Latin1_General_100_BIN2
        THROW 51302, 'Approved QA database is not ONLINE.', 1;

    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.user_roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NULL
        THROW 51303, 'Required Identity or EF migration-history table is missing.', 1;

    IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) <> 27
        THROW 51309, 'Physical table count differs from the approved 27-table QA schema.', 1;

    IF COL_LENGTH(N'dbo.users', N'id') IS NULL
       OR COL_LENGTH(N'dbo.users', N'is_active') IS NULL
       OR COL_LENGTH(N'dbo.roles', N'id') IS NULL
       OR COL_LENGTH(N'dbo.roles', N'name') IS NULL
       OR COL_LENGTH(N'dbo.roles', N'normalized_name') IS NULL
       OR COL_LENGTH(N'dbo.user_roles', N'user_id') IS NULL
       OR COL_LENGTH(N'dbo.user_roles', N'role_id') IS NULL
        THROW 51304, 'Identity schema columns do not match the reviewed EF mapping.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
                   WHERE MigrationId = N'20261015173046_InitialFoundation')
       OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
                      WHERE MigrationId = N'20261016133620_AddCheckoutRequestId')
       OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
                      WHERE MigrationId = N'20261019174632_AddOrderCancellationRequestFields')
       OR (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) <> 3
        THROW 51308, 'EF migration history differs from the approved three-migration QA baseline.', 1;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users')
               AND name = N'id' AND (TYPE_NAME(user_type_id) <> N'int' OR is_nullable <> 0))
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users')
                  AND name = N'is_active' AND (TYPE_NAME(user_type_id) <> N'bit' OR is_nullable <> 0))
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.roles')
                  AND name = N'id' AND (TYPE_NAME(user_type_id) <> N'int' OR is_nullable <> 0))
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.roles')
                  AND name = N'name' AND (TYPE_NAME(user_type_id) <> N'nvarchar' OR max_length <> 512 OR is_nullable <> 0))
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.roles')
                  AND name = N'normalized_name' AND (TYPE_NAME(user_type_id) <> N'nvarchar' OR max_length <> 512 OR is_nullable <> 0))
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.user_roles')
                  AND name = N'user_id' AND (TYPE_NAME(user_type_id) <> N'int' OR is_nullable <> 0))
       OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.user_roles')
                  AND name = N'role_id' AND (TYPE_NAME(user_type_id) <> N'int' OR is_nullable <> 0))
        THROW 51307, 'Identity schema column types or nullability do not match the reviewed EF mapping.', 1;

    DECLARE @AdminRoleId int;
    DECLARE @CustomerRoleId int;
    DECLARE @AdminRoleMatches int;
    DECLARE @CustomerRoleMatches int;
    SELECT @AdminRoleId = MAX(id), @AdminRoleMatches = COUNT(*)
    FROM dbo.roles WITH (HOLDLOCK)
    WHERE DATALENGTH(name) = DATALENGTH(N'Admin')
      AND name COLLATE Latin1_General_100_BIN2 = N'Admin'
      AND DATALENGTH(normalized_name) = DATALENGTH(N'ADMIN')
      AND normalized_name COLLATE Latin1_General_100_BIN2 = N'ADMIN';
    SELECT @CustomerRoleId = MAX(id), @CustomerRoleMatches = COUNT(*)
    FROM dbo.roles WITH (HOLDLOCK)
    WHERE DATALENGTH(name) = DATALENGTH(N'Customer')
      AND name COLLATE Latin1_General_100_BIN2 = N'Customer'
      AND DATALENGTH(normalized_name) = DATALENGTH(N'CUSTOMER')
      AND normalized_name COLLATE Latin1_General_100_BIN2 = N'CUSTOMER';

    IF @AdminRoleMatches <> 1 OR @CustomerRoleMatches <> 1
        THROW 51305, 'Canonical Admin or Customer role is missing or ambiguous.', 1;

    DECLARE @UserCount bigint;
    DECLARE @RoleCount bigint;
    DECLARE @CanonicalRoleCount int;
    DECLARE @MappingCount bigint;
    SELECT @UserCount = COUNT_BIG(*) FROM dbo.users WITH (HOLDLOCK);
    SELECT @RoleCount = COUNT_BIG(*) FROM dbo.roles WITH (HOLDLOCK);
    SELECT @MappingCount = COUNT_BIG(*) FROM dbo.user_roles WITH (HOLDLOCK);
    SELECT @CanonicalRoleCount = COUNT(*)
    FROM dbo.roles WITH (HOLDLOCK)
    WHERE (DATALENGTH(name) = DATALENGTH(N'Admin')
           AND name COLLATE Latin1_General_100_BIN2 = N'Admin'
           AND DATALENGTH(normalized_name) = DATALENGTH(N'ADMIN')
           AND normalized_name COLLATE Latin1_General_100_BIN2 = N'ADMIN')
       OR (DATALENGTH(name) = DATALENGTH(N'Customer')
           AND name COLLATE Latin1_General_100_BIN2 = N'Customer'
           AND DATALENGTH(normalized_name) = DATALENGTH(N'CUSTOMER')
           AND normalized_name COLLATE Latin1_General_100_BIN2 = N'CUSTOMER')
       OR (DATALENGTH(name) = DATALENGTH(N'Sales')
           AND name COLLATE Latin1_General_100_BIN2 = N'Sales'
           AND DATALENGTH(normalized_name) = DATALENGTH(N'SALES')
           AND normalized_name COLLATE Latin1_General_100_BIN2 = N'SALES')
       OR (DATALENGTH(name) = DATALENGTH(N'Warehouse')
           AND name COLLATE Latin1_General_100_BIN2 = N'Warehouse'
           AND DATALENGTH(normalized_name) = DATALENGTH(N'WAREHOUSE')
           AND normalized_name COLLATE Latin1_General_100_BIN2 = N'WAREHOUSE');

    DECLARE @IsActive bit;
    SELECT @IsActive = is_active FROM dbo.users WITH (HOLDLOCK) WHERE id = @UserId;

    DECLARE @TargetMappingCount int;
    DECLARE @TargetAdminMappings int;
    DECLARE @TargetCustomerMappings int;
    DECLARE @TargetOtherRoleMappings int;
    SELECT
        @TargetMappingCount = COUNT(*),
        @TargetAdminMappings = SUM(CASE WHEN role_id = @AdminRoleId THEN 1 ELSE 0 END),
        @TargetCustomerMappings = SUM(CASE WHEN role_id = @CustomerRoleId THEN 1 ELSE 0 END),
        @TargetOtherRoleMappings = SUM(CASE WHEN role_id NOT IN (@AdminRoleId, @CustomerRoleId) THEN 1 ELSE 0 END)
    FROM dbo.user_roles WITH (HOLDLOCK)
    WHERE user_id = @UserId;

    DECLARE @ActiveAdminCount bigint;
    DECLARE @ActiveAdminOnlyCount bigint;
    DECLARE @MultiRoleUserCount bigint;
    SELECT @ActiveAdminCount = COUNT(DISTINCT u.id)
    FROM dbo.users AS u WITH (HOLDLOCK)
    JOIN dbo.user_roles AS ur WITH (HOLDLOCK) ON ur.user_id = u.id
    WHERE u.is_active = 1 AND ur.role_id = @AdminRoleId;
    SELECT @ActiveAdminOnlyCount = COUNT_BIG(*)
    FROM dbo.users AS u WITH (HOLDLOCK)
    WHERE u.is_active = 1
      AND EXISTS (SELECT 1 FROM dbo.user_roles AS ur WITH (HOLDLOCK)
                  WHERE ur.user_id = u.id AND ur.role_id = @AdminRoleId)
      AND NOT EXISTS (SELECT 1 FROM dbo.user_roles AS ur WITH (HOLDLOCK)
                      WHERE ur.user_id = u.id AND ur.role_id <> @AdminRoleId);
    SELECT @MultiRoleUserCount = COUNT_BIG(*)
    FROM
    (
        SELECT user_id FROM dbo.user_roles WITH (HOLDLOCK)
        GROUP BY user_id HAVING COUNT(*) > 1
    ) AS multi_role_users;

    DECLARE @Passed bit = CASE WHEN
        @UserCount = @ExpectedUsers
        AND @RoleCount = @ExpectedRoles
        AND @CanonicalRoleCount = @ExpectedRoles
        AND @MappingCount = @ExpectedMappings
        AND @IsActive = 1
        AND @TargetMappingCount = 1 AND @TargetAdminMappings = 1
        AND @TargetCustomerMappings = 0 AND @TargetOtherRoleMappings = 0
        AND @ActiveAdminCount = 1 AND @ActiveAdminOnlyCount = 1
        AND @MultiRoleUserCount = 0
        THEN 1 ELSE 0 END;

    COMMIT TRANSACTION;

    SELECT @Passed AS DatabasePostcheckPassed,
           @UserId AS UserId,
           @IsActive AS IsActive,
           @TargetMappingCount AS TargetRoleMappingCount,
           @TargetAdminMappings AS TargetAdminMappingCount,
           @TargetCustomerMappings AS TargetCustomerMappingCount,
           @UserCount AS UserCount,
           @RoleCount AS RoleCount,
           @CanonicalRoleCount AS CanonicalRoleCount,
           @MappingCount AS UserRoleMappingCount,
           @ActiveAdminCount AS ActiveAdminCount,
           @ActiveAdminOnlyCount AS ActiveAdminOnlyCount,
           @MultiRoleUserCount AS MultiRoleUserCount;

    IF @Passed <> 1
        THROW 51306, 'Read-only postcheck did not match the approved target state.', 1;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
