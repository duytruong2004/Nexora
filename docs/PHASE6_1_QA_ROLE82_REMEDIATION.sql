/*
    NEXORA-P61-QA-ROLE82 — REVIEW-ONLY REMEDIATION SCRIPT

    NOT APPROVED FOR EXECUTION. Do not set @OwnerExecutionApproved to 1 until
    the owner separately approves this exact QA-only change and a fresh
    read-only preflight confirms every baseline below.

    Intended target only: PC01\SQLEXPRESS / NEXORA_IntegrationTests
    Intended data change: delete exactly UserId 82's Customer mapping;
    retain the active account and its Admin mapping.

    No database is created/switched to by this script. It fails closed unless
    the connection is already on the exact approved server and database.
    This script has not been executed or parsed by SQL Server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SET LOCK_TIMEOUT 15000;

DECLARE @OwnerExecutionApproved bit = 0; -- Change only after separate owner approval.
DECLARE @SchemaChangeFreezeConfirmed bit = 0; -- Operator attestation required for a separately approved run.
DECLARE @ExpectedServer nvarchar(128) = N'PC01\SQLEXPRESS';
DECLARE @ExpectedDatabase sysname = N'NEXORA_IntegrationTests';
DECLARE @UserId int = 82;
DECLARE @ExpectedUsers bigint = 6;
DECLARE @ExpectedRoles bigint = 4;
DECLARE @ExpectedMappingsBefore bigint = 7;
DECLARE @ExpectedMappingsAfter bigint = 6;
DECLARE @ExpectedAdminRoleId int = 4; -- Drift check only; role is resolved by canonical name below.
DECLARE @ExpectedCustomerRoleId int = 1; -- Drift check only; role is resolved by canonical name below.

BEGIN TRY
    IF @OwnerExecutionApproved <> 1
        THROW 51200, 'Owner execution approval is not enabled in this reviewed script.', 1;
    IF @SchemaChangeFreezeConfirmed <> 1
        THROW 51227, 'A database schema-change freeze has not been confirmed for this execution window.', 1;

    DECLARE @ActualServer nvarchar(128) = CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'));
    IF @ActualServer IS NULL
       OR DATALENGTH(@ActualServer) <> DATALENGTH(@ExpectedServer)
       OR @ActualServer COLLATE Latin1_General_100_BIN2 <> @ExpectedServer COLLATE Latin1_General_100_BIN2
        THROW 51201, 'Connected SQL Server does not match the approved QA instance.', 1;

    DECLARE @ActualDatabase sysname = DB_NAME();
    IF @ActualDatabase IS NULL
       OR DATALENGTH(@ActualDatabase) <> DATALENGTH(@ExpectedDatabase)
       OR @ActualDatabase COLLATE Latin1_General_100_BIN2 <> @ExpectedDatabase COLLATE Latin1_General_100_BIN2
        THROW 51202, 'Current database does not match the approved QA database.', 1;

    DECLARE @DatabaseStatus nvarchar(60) = CONVERT(nvarchar(60), DATABASEPROPERTYEX(@ActualDatabase, 'Status'));
    IF @DatabaseStatus IS NULL
       OR DATALENGTH(@DatabaseStatus) <> DATALENGTH(N'ONLINE')
       OR @DatabaseStatus COLLATE Latin1_General_100_BIN2 <> N'ONLINE' COLLATE Latin1_General_100_BIN2
        THROW 51203, 'Approved QA database is not ONLINE.', 1;

    BEGIN TRANSACTION;

    DECLARE @LockResult int;

    /*
        Lock names do not form a hierarchy. NEXORA:ACCOUNT is retained because
        this reviewed change requires that coordination key; it does not by
        itself coordinate with IdentityAccountAdministrationService. That
        service uses NEXORA:ACCOUNT:POLICY, so acquire that exact resource too.
        No NEXORA root resource is needed or acquired. The service takes only
        the POLICY resource; cooperating scripts taking both must use this order.
        Both locks use public, Exclusive, Transaction, and a 15-second timeout.
    */
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'NEXORA:ACCOUNT',
        @DbPrincipal = N'public',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 15000;
    IF @LockResult < 0
        THROW 51209, 'Could not acquire NEXORA:ACCOUNT application lock.', 1;

    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'NEXORA:ACCOUNT:POLICY',
        @DbPrincipal = N'public',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 15000;
    IF @LockResult < 0
        THROW 51210, 'Could not acquire the Identity account-policy application lock.', 1;

    /* Reject missing tables first. If concurrent DDL removes one after this
       check, the lock query below errors and CATCH rolls the transaction back. */
    IF OBJECT_ID(N'dbo.users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.user_roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NULL
        THROW 51204, 'Required Identity or EF migration-history table is missing.', 1;

    /* Hold X/HOLDLOCK on the affected Identity and history tables before
       checking their metadata. This fences concurrent DML and DDL on these
       objects through the final assertions and COMMIT. */
    DECLARE @UserCount bigint;
    DECLARE @RoleCount bigint;
    DECLARE @MappingCount bigint;
    DECLARE @MigrationCount bigint;
    DECLARE @AuxiliaryCount bigint;
    SELECT @UserCount = COUNT_BIG(*) FROM dbo.users WITH (TABLOCKX, HOLDLOCK);
    SELECT @RoleCount = COUNT_BIG(*) FROM dbo.roles WITH (TABLOCKX, HOLDLOCK);
    SELECT @MappingCount = COUNT_BIG(*) FROM dbo.user_roles WITH (TABLOCKX, HOLDLOCK);
    SELECT @MigrationCount = COUNT_BIG(*) FROM dbo.__EFMigrationsHistory WITH (TABLOCKX, HOLDLOCK);
    SELECT @AuxiliaryCount = COUNT_BIG(*) FROM dbo.AspNetUserClaims WITH (TABLOCKX, HOLDLOCK);
    SELECT @AuxiliaryCount = COUNT_BIG(*) FROM dbo.AspNetUserLogins WITH (TABLOCKX, HOLDLOCK);
    SELECT @AuxiliaryCount = COUNT_BIG(*) FROM dbo.AspNetUserTokens WITH (TABLOCKX, HOLDLOCK);
    SELECT @AuxiliaryCount = COUNT_BIG(*) FROM dbo.AspNetRoleClaims WITH (TABLOCKX, HOLDLOCK);

    /* All schema and migration-history checks now run inside the transaction
       while locks on the relevant tables are held. */
    IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) <> 27
        THROW 51224, 'Physical table count differs from the approved 27-table QA schema.', 1;

    DECLARE @ExpectedTables TABLE
    (
        SchemaName sysname COLLATE Latin1_General_100_BIN2 NOT NULL,
        TableName sysname COLLATE Latin1_General_100_BIN2 NOT NULL,
        PRIMARY KEY (SchemaName, TableName)
    );
    INSERT INTO @ExpectedTables (SchemaName, TableName)
    VALUES
        (N'dbo', N'AspNetRoleClaims'), (N'dbo', N'AspNetUserClaims'),
        (N'dbo', N'AspNetUserLogins'), (N'dbo', N'AspNetUserTokens'),
        (N'dbo', N'__EFMigrationsHistory'), (N'dbo', N'brands'),
        (N'dbo', N'cart_items'), (N'dbo', N'carts'), (N'dbo', N'categories'),
        (N'dbo', N'category_specifications'), (N'dbo', N'favorites'),
        (N'dbo', N'inventories'), (N'dbo', N'inventory_transactions'),
        (N'dbo', N'order_details'), (N'dbo', N'orders'),
        (N'dbo', N'product_images'), (N'dbo', N'product_specifications'),
        (N'dbo', N'products'), (N'dbo', N'promotion_products'),
        (N'dbo', N'promotions'), (N'dbo', N'recommendation_rules'),
        (N'dbo', N'reviews'), (N'dbo', N'roles'), (N'dbo', N'specifications'),
        (N'dbo', N'usage_needs'), (N'dbo', N'user_roles'), (N'dbo', N'users');

    IF EXISTS
       (SELECT SchemaName, TableName FROM @ExpectedTables
        EXCEPT
        SELECT s.name COLLATE Latin1_General_100_BIN2,
               t.name COLLATE Latin1_General_100_BIN2
        FROM sys.tables AS t
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0)
       OR EXISTS
       (SELECT s.name COLLATE Latin1_General_100_BIN2,
               t.name COLLATE Latin1_General_100_BIN2
        FROM sys.tables AS t
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0
        EXCEPT
        SELECT SchemaName, TableName FROM @ExpectedTables)
        THROW 51229, 'Physical table names or schemas differ from the approved 27-table baseline.', 1;

    IF COL_LENGTH(N'dbo.users', N'id') IS NULL
       OR COL_LENGTH(N'dbo.users', N'is_active') IS NULL
       OR COL_LENGTH(N'dbo.roles', N'id') IS NULL
       OR COL_LENGTH(N'dbo.roles', N'name') IS NULL
       OR COL_LENGTH(N'dbo.roles', N'normalized_name') IS NULL
       OR COL_LENGTH(N'dbo.user_roles', N'user_id') IS NULL
       OR COL_LENGTH(N'dbo.user_roles', N'role_id') IS NULL
        THROW 51205, 'Identity schema columns do not match the reviewed EF mapping.', 1;

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
        THROW 51222, 'Identity schema column types or nullability do not match the reviewed EF mapping.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS kc
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.users')
          AND kc.type = N'PK'
          AND kc.name = N'PK_users'
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = kc.parent_object_id
                 AND ic.index_id = kc.unique_index_id
                 AND ic.key_ordinal > 0) = 1
          AND EXISTS (SELECT 1 FROM sys.index_columns AS ic
                      JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                        AND ic.key_ordinal = 1 AND c.name = N'id')
    )
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS kc
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.roles')
          AND kc.type = N'PK'
          AND kc.name = N'PK_roles'
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = kc.parent_object_id
                 AND ic.index_id = kc.unique_index_id
                 AND ic.key_ordinal > 0) = 1
          AND EXISTS (SELECT 1 FROM sys.index_columns AS ic
                      JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                        AND ic.key_ordinal = 1 AND c.name = N'id')
    )
       OR NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS kc
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.user_roles')
          AND kc.type = N'PK'
          AND kc.name = N'PK_user_roles'
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = kc.parent_object_id
                 AND ic.index_id = kc.unique_index_id
                 AND ic.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns AS ic
                      JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                        AND ic.key_ordinal = 1 AND c.name = N'user_id')
          AND EXISTS (SELECT 1 FROM sys.index_columns AS ic
                      JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                        AND ic.key_ordinal = 2 AND c.name = N'role_id')
    )
        THROW 51206, 'Identity primary keys do not match the reviewed EF schema.', 1;

    IF NOT EXISTS
       (SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.roles')
          AND i.name = N'RoleNameIndex'
          AND i.is_unique = 1
          AND i.is_disabled = 0
          AND i.is_hypothetical = 0
          AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                 AND ic.key_ordinal > 0) = 1
          AND EXISTS
              (SELECT 1
               FROM sys.index_columns AS ic
               JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                 AND ic.key_ordinal = 1 AND c.name = N'normalized_name'))
        THROW 51223, 'Canonical role-name unique index is missing.', 1;

    /* The source migration has two outgoing CASCADE FKs from user_roles to
       users/roles, no incoming FK, and no trigger. Deleting a child mapping
       does not cascade to its parent, but unknown live metadata fails closed. */
    IF (SELECT COUNT(*) FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.user_roles')) <> 2
       OR EXISTS (SELECT 1 FROM sys.foreign_keys
                  WHERE referenced_object_id = OBJECT_ID(N'dbo.user_roles'))
       OR NOT EXISTS
          (SELECT 1 FROM sys.foreign_keys AS fk
           WHERE fk.name = N'FK_user_roles_users_user_id'
             AND fk.parent_object_id = OBJECT_ID(N'dbo.user_roles')
             AND fk.referenced_object_id = OBJECT_ID(N'dbo.users')
             AND fk.delete_referential_action = 1
             AND fk.is_disabled = 0
             AND fk.is_not_trusted = 0
             AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
                  WHERE fkc.constraint_object_id = fk.object_id) = 1
             AND EXISTS (SELECT 1 FROM sys.foreign_key_columns AS fkc
                         JOIN sys.columns AS parent_column
                           ON parent_column.object_id = fkc.parent_object_id
                          AND parent_column.column_id = fkc.parent_column_id
                         JOIN sys.columns AS referenced_column
                           ON referenced_column.object_id = fkc.referenced_object_id
                          AND referenced_column.column_id = fkc.referenced_column_id
                         WHERE fkc.constraint_object_id = fk.object_id
                           AND parent_column.name = N'user_id'
                           AND referenced_column.name = N'id'))
       OR NOT EXISTS
          (SELECT 1 FROM sys.foreign_keys AS fk
           WHERE fk.name = N'FK_user_roles_roles_role_id'
             AND fk.parent_object_id = OBJECT_ID(N'dbo.user_roles')
             AND fk.referenced_object_id = OBJECT_ID(N'dbo.roles')
             AND fk.delete_referential_action = 1
             AND fk.is_disabled = 0
             AND fk.is_not_trusted = 0
             AND (SELECT COUNT(*) FROM sys.foreign_key_columns AS fkc
                  WHERE fkc.constraint_object_id = fk.object_id) = 1
             AND EXISTS (SELECT 1 FROM sys.foreign_key_columns AS fkc
                         JOIN sys.columns AS parent_column
                           ON parent_column.object_id = fkc.parent_object_id
                          AND parent_column.column_id = fkc.parent_column_id
                         JOIN sys.columns AS referenced_column
                           ON referenced_column.object_id = fkc.referenced_object_id
                          AND referenced_column.column_id = fkc.referenced_column_id
                         WHERE fkc.constraint_object_id = fk.object_id
                           AND parent_column.name = N'role_id'
                           AND referenced_column.name = N'id'))
        THROW 51225, 'user_roles foreign-key graph or cascade action differs from reviewed source.', 1;

    IF EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID(N'dbo.user_roles'))
        THROW 51226, 'A trigger exists on user_roles; review it before any remediation.', 1;

    IF @MigrationCount <> 3
       OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261015173046_InitialFoundation')
       OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261016133620_AddCheckoutRequestId')
       OR NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261019174632_AddOrderCancellationRequestFields')
        THROW 51208, 'EF migration history does not match the approved three-migration QA baseline.', 1;

    IF @UserCount <> @ExpectedUsers OR @RoleCount <> @ExpectedRoles
       OR @MappingCount <> @ExpectedMappingsBefore
        THROW 51211, 'QA users, roles, or mapping count drifted from the approved preflight.', 1;

    IF NOT EXISTS
       (SELECT 1 FROM dbo.roles WITH (HOLDLOCK)
        WHERE DATALENGTH(name) = DATALENGTH(N'Admin')
          AND name COLLATE Latin1_General_100_BIN2 = N'Admin'
          AND DATALENGTH(normalized_name) = DATALENGTH(N'ADMIN')
          AND normalized_name COLLATE Latin1_General_100_BIN2 = N'ADMIN')
        THROW 51212, 'Canonical Admin role is missing or not normalized as expected.', 1;
    IF NOT EXISTS
       (SELECT 1 FROM dbo.roles WITH (HOLDLOCK)
        WHERE DATALENGTH(name) = DATALENGTH(N'Customer')
          AND name COLLATE Latin1_General_100_BIN2 = N'Customer'
          AND DATALENGTH(normalized_name) = DATALENGTH(N'CUSTOMER')
          AND normalized_name COLLATE Latin1_General_100_BIN2 = N'CUSTOMER')
        THROW 51213, 'Canonical Customer role is missing or not normalized as expected.', 1;
    IF NOT EXISTS
       (SELECT 1 FROM dbo.roles WITH (HOLDLOCK)
        WHERE DATALENGTH(name) = DATALENGTH(N'Sales')
          AND name COLLATE Latin1_General_100_BIN2 = N'Sales'
          AND DATALENGTH(normalized_name) = DATALENGTH(N'SALES')
          AND normalized_name COLLATE Latin1_General_100_BIN2 = N'SALES')
        THROW 51214, 'Canonical Sales role is missing or not normalized as expected.', 1;
    IF NOT EXISTS
       (SELECT 1 FROM dbo.roles WITH (HOLDLOCK)
        WHERE DATALENGTH(name) = DATALENGTH(N'Warehouse')
          AND name COLLATE Latin1_General_100_BIN2 = N'Warehouse'
          AND DATALENGTH(normalized_name) = DATALENGTH(N'WAREHOUSE')
          AND normalized_name COLLATE Latin1_General_100_BIN2 = N'WAREHOUSE')
        THROW 51215, 'Canonical Warehouse role is missing or not normalized as expected.', 1;

    DECLARE @AdminRoleId int;
    DECLARE @CustomerRoleId int;
    SELECT @AdminRoleId = id FROM dbo.roles
    WHERE DATALENGTH(name) = DATALENGTH(N'Admin')
      AND name COLLATE Latin1_General_100_BIN2 = N'Admin'
      AND DATALENGTH(normalized_name) = DATALENGTH(N'ADMIN')
      AND normalized_name COLLATE Latin1_General_100_BIN2 = N'ADMIN';
    SELECT @CustomerRoleId = id FROM dbo.roles
    WHERE DATALENGTH(name) = DATALENGTH(N'Customer')
      AND name COLLATE Latin1_General_100_BIN2 = N'Customer'
      AND DATALENGTH(normalized_name) = DATALENGTH(N'CUSTOMER')
      AND normalized_name COLLATE Latin1_General_100_BIN2 = N'CUSTOMER';

    IF @AdminRoleId <> @ExpectedAdminRoleId OR @CustomerRoleId <> @ExpectedCustomerRoleId
        THROW 51216, 'Canonical role IDs drifted from the read-only QA preflight.', 1;

    DECLARE @IsActive bit;
    SELECT @IsActive = is_active FROM dbo.users WHERE id = @UserId;
    IF @IsActive IS NULL OR @IsActive <> 1
        THROW 51217, 'Target account is missing or inactive.', 1;

    DECLARE @TargetMappingCount int;
    DECLARE @TargetAdminMappings int;
    DECLARE @TargetCustomerMappings int;
    DECLARE @TargetOtherRoleMappings int;
    SELECT
        @TargetMappingCount = COUNT(*),
        @TargetAdminMappings = SUM(CASE WHEN role_id = @AdminRoleId THEN 1 ELSE 0 END),
        @TargetCustomerMappings = SUM(CASE WHEN role_id = @CustomerRoleId THEN 1 ELSE 0 END),
        @TargetOtherRoleMappings = SUM(CASE WHEN role_id NOT IN (@AdminRoleId, @CustomerRoleId) THEN 1 ELSE 0 END)
    FROM dbo.user_roles
    WHERE user_id = @UserId;
    IF @TargetMappingCount <> 2 OR @TargetAdminMappings <> 1
       OR @TargetCustomerMappings <> 1 OR @TargetOtherRoleMappings <> 0
        THROW 51218, 'Target account no longer has exactly one Admin and one Customer mapping.', 1;

    DECLARE @ActiveAdminCount bigint;
    DECLARE @ActiveAdminOnlyCount bigint;
    DECLARE @OtherMultiRoleUserCount bigint;
    SELECT @ActiveAdminCount = COUNT(DISTINCT u.id)
    FROM dbo.users AS u
    JOIN dbo.user_roles AS ur ON ur.user_id = u.id
    WHERE u.is_active = 1 AND ur.role_id = @AdminRoleId;
    SELECT @ActiveAdminOnlyCount = COUNT_BIG(*)
    FROM dbo.users AS u
    WHERE u.is_active = 1
      AND EXISTS (SELECT 1 FROM dbo.user_roles AS ur WHERE ur.user_id = u.id AND ur.role_id = @AdminRoleId)
      AND NOT EXISTS (SELECT 1 FROM dbo.user_roles AS ur WHERE ur.user_id = u.id AND ur.role_id <> @AdminRoleId);
    SELECT @OtherMultiRoleUserCount = COUNT_BIG(*)
    FROM
    (
        SELECT user_id FROM dbo.user_roles GROUP BY user_id HAVING COUNT(*) > 1
    ) AS multi_role_users
    WHERE user_id <> @UserId;

    IF @ActiveAdminCount <> 1 OR @ActiveAdminOnlyCount <> 0 OR @OtherMultiRoleUserCount <> 0
        THROW 51219, 'Admin or multi-role account baseline differs from the approved preflight.', 1;

    /* Recheck the global schema/history baseline immediately before the only
       DML. Identity/history tables are transaction-locked above. SQL Server
       does not provide this script a cooperative lock against arbitrary DDL;
       the separately confirmed schema-change freeze is therefore mandatory. */
    IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) <> 27
       OR (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) <> 3
        THROW 51228, 'Schema or migration history changed during the guarded transaction.', 1;

    DECLARE @AffectedRows int;
    DELETE FROM dbo.user_roles
    WHERE user_id = @UserId
      AND role_id = @CustomerRoleId;
    SET @AffectedRows = @@ROWCOUNT;
    IF @AffectedRows <> 1
        THROW 51220, 'Expected exactly one Customer mapping to be deleted.', 1;

    /* Assert every postcondition before COMMIT. Any mismatch enters CATCH and
       rolls back the single-row deletion. */
    SELECT @UserCount = COUNT_BIG(*) FROM dbo.users;
    SELECT @RoleCount = COUNT_BIG(*) FROM dbo.roles;
    SELECT @MappingCount = COUNT_BIG(*) FROM dbo.user_roles;
    SELECT @IsActive = is_active FROM dbo.users WHERE id = @UserId;
    SELECT
        @TargetMappingCount = COUNT(*),
        @TargetAdminMappings = SUM(CASE WHEN role_id = @AdminRoleId THEN 1 ELSE 0 END),
        @TargetCustomerMappings = SUM(CASE WHEN role_id = @CustomerRoleId THEN 1 ELSE 0 END),
        @TargetOtherRoleMappings = SUM(CASE WHEN role_id NOT IN (@AdminRoleId, @CustomerRoleId) THEN 1 ELSE 0 END)
    FROM dbo.user_roles
    WHERE user_id = @UserId;
    SELECT @ActiveAdminCount = COUNT(DISTINCT u.id)
    FROM dbo.users AS u
    JOIN dbo.user_roles AS ur ON ur.user_id = u.id
    WHERE u.is_active = 1 AND ur.role_id = @AdminRoleId;
    SELECT @ActiveAdminOnlyCount = COUNT_BIG(*)
    FROM dbo.users AS u
    WHERE u.is_active = 1
      AND EXISTS (SELECT 1 FROM dbo.user_roles AS ur WHERE ur.user_id = u.id AND ur.role_id = @AdminRoleId)
      AND NOT EXISTS (SELECT 1 FROM dbo.user_roles AS ur WHERE ur.user_id = u.id AND ur.role_id <> @AdminRoleId);
    SELECT @OtherMultiRoleUserCount = COUNT_BIG(*)
    FROM
    (
        SELECT user_id FROM dbo.user_roles GROUP BY user_id HAVING COUNT(*) > 1
    ) AS multi_role_users
    WHERE user_id <> @UserId;

    IF @UserCount <> @ExpectedUsers OR @RoleCount <> @ExpectedRoles
       OR @MappingCount <> @ExpectedMappingsAfter
       OR @IsActive <> 1 OR @TargetMappingCount <> 1
       OR @TargetAdminMappings <> 1 OR @TargetCustomerMappings <> 0
       OR @TargetOtherRoleMappings <> 0 OR @ActiveAdminCount <> 1
       OR @ActiveAdminOnlyCount <> 1 OR @OtherMultiRoleUserCount <> 0
        THROW 51221, 'Postconditions failed; the transaction will be rolled back.', 1;

    COMMIT TRANSACTION;

    SELECT N'COMMITTED' AS ChangeStatus,
           @UserId AS UserId,
           @UserCount AS UserCount,
           @MappingCount AS UserRoleMappingCount,
           @ActiveAdminCount AS ActiveAdminCount,
           @ActiveAdminOnlyCount AS ActiveAdminOnlyCount,
           @AffectedRows AS AffectedRows;

    /* If the client loses the connection while COMMIT is in flight, its result
       is ambiguous. Do not rerun this DELETE or restore Customer automatically.
       Reconnect to the exact QA target and run the separate POSTCHECK script. */
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
