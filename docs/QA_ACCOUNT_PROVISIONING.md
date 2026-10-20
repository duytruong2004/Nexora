# NEXORA – QA account provisioning

## Scope and safety

This local console command creates a **new** QA identity only for the existing `Sales` or `Warehouse` role. It does not grant a role to an existing user, create an Admin, reset a password, or add a fifth role. The command is not an HTTP endpoint and is not run automatically at website startup.

Before asking for credentials, it opens a read-only SQL connection and requires all of the following to match: Windows Integrated Authentication, `PC01\SQLEXPRESS`, `NEXORA_IntegrationTests`, database status `ONLINE`, the three approved migrations, 27 physical tables (22 business + four Identity support tables + migration history), Phase 5 order/inventory columns, and exactly the four NEXORA roles. It refuses to proceed if any check differs. The email must be a new `qa.*@nexora.test` identity; an existing email/username is never modified. Passwords are entered with console echo disabled and are validated by the application's existing ASP.NET Core Identity policy.

The current policy is 10 or more characters, uppercase, lowercase, a digit, a non-alphanumeric character, and at least four unique characters. Do not paste a password into chat, a command argument, shell history, an appsettings file, or a report. Keep the QA password in your own password manager if you need it for the browser session.

Identity creation and role assignment execute inside one transaction managed by EF Core's SQL Server execution strategy. The account and requested role are verified together after an ambiguous commit before EF retries. The Identity stores and provisioning command resolve the same scoped `AppDbContext`; retries clear stale tracked entities, and an existing account is never upgraded.

## Create a QA Sales or Warehouse identity

Run in a local PowerShell terminal from the repository root. Build to an output directory separate from the running website so the command cannot accidentally launch an older `--no-build` binary:

```powershell
$buildArtifacts = 'D:\NEXORA-QA-Provisioning-Build'
dotnet restore NEXORA.sln --artifacts-path $buildArtifacts
dotnet build NEXORA.sln --no-restore --artifacts-path $buildArtifacts

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_CONTENTROOT = (Resolve-Path .\src\NEXORA.Web).Path
$env:ConnectionStrings__DefaultConnection = 'Server=PC01\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10'
dotnet "$buildArtifacts\bin\NEXORA.Web\debug\NEXORA.Web.dll" --provision-qa-staff
```

For this repair, that output directory already contains the rebuilt command. Do not use `dotnet run --no-build` against the repository's default `bin` directory for this retry.

At the prompts:

1. Select only `Sales` or `Warehouse`.
2. Enter a new QA address such as `qa.sales@nexora.test` or `qa.warehouse@nexora.test` and a QA display name.
3. Enter and confirm the password locally. The input is not echoed.
4. Type the exact confirmation requested by the command (`CREATE QA Sales` or `CREATE QA Warehouse`).

Run the command again to create the other role. The process exits after one account. A successful result reports the new numeric user ID and role only. The account is active, its email is not marked confirmed, and it is not sent through an external email provider.

## Verify role assignment without showing private data

The command reports its result. If a separate SQL verification is needed, use a read-only query on the dedicated QA database and select only the user ID/role; do not select email, password hash, token, or personal fields:

```sql
SELECT u.id, r.name AS role_name
FROM dbo.users AS u
JOIN dbo.user_roles AS ur ON ur.user_id = u.id
JOIN dbo.roles AS r ON r.id = ur.role_id
WHERE u.normalized_email = UPPER(N'qa.sales@nexora.test');
```

Replace the example address locally for the role being verified. Never run this against `NEXORA`.

## Use in the browser

Use the QA site at `http://127.0.0.1:5059/account/login`. Sign in with the QA email and the password entered during local provisioning. Do not enable “Duy trì đăng nhập” on a shared machine. The web application uses the same database override as above; it does not use the connection string in `appsettings.Development.json`.

## Disable after UAT

Do not remove or disable these accounts before the owner confirms UAT is complete. There is no employee-account administration UI in Phase 5. After UAT, request a separately scoped local Identity operation to set `AppUser.IsActive = false` and remove the QA-only `Sales`/`Warehouse` assignment, with the same database guard and a user-ID confirmation. Do not delete rows or change roles through ad-hoc SQL. This deactivation operation is intentionally **not** part of the current provisioning command and has not been run.
