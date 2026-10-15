# NEXORA

NEXORA is an ASP.NET Core MVC .NET 10 modular monolith using Entity Framework Core and SQL Server.

## Projects

- `src/NEXORA.Web` – MVC application, Identity account flows and Razor UI.
- `src/NEXORA.Application` – application contracts, including the email sender abstraction.
- `src/NEXORA.Domain` – business entities and domain rules.
- `src/NEXORA.Infrastructure` – SQL Server persistence, Identity mapping and migrations.
- `tests/NEXORA.Tests` – domain and EF model tests.

## Local prerequisites

- .NET 10 SDK.
- SQL Server Express or another supported SQL Server instance.
- For the default Development settings, a local named instance `SQLEXPRESS` and Windows Integrated Authentication.

The Development connection is `Server=.\SQLEXPRESS;Database=NEXORA;Integrated Security=True`. On the verified PC01 host, `.` resolves to `PC01`; the database was created only after approval and now contains migration `20261015173046_InitialFoundation`.

## Restore, build and test

```powershell
dotnet restore NEXORA.sln
dotnet build NEXORA.sln
dotnet test NEXORA.sln
```

## Migration status

The approved migration is in `src/NEXORA.Infrastructure/Persistence/Migrations`; it has already been applied to `PC01\SQLEXPRESS`, database `NEXORA`. Do not run the SQL review script directly. To prepare a different, newly approved empty development database, use the migration command below after changing and verifying the connection string:

```powershell
dotnet ef database update `
  --project src/NEXORA.Infrastructure/NEXORA.Infrastructure.csproj `
  --startup-project src/NEXORA.Web/NEXORA.Web.csproj `
  --context AppDbContext
```

The migration creates 22 official business tables, four ASP.NET Identity support tables, and EF Core's `__EFMigrationsHistory` table.

## Run the website

To run the website against the configured database:

```powershell
dotnet run --project src/NEXORA.Web/NEXORA.Web.csproj
```

The app initializes the four required roles when it starts against an applied schema. Public registration only creates Customer accounts. No administrator account or default password is created. Production must configure `ConnectionStrings__DefaultConnection` and a real `IEmailSender` provider before enabling password reset.

In Development, password reset emails are held in memory and can be inspected at `/dev/mailbox`. The mailbox disappears when the app stops and is not available outside Development.
