# NEXORA

NEXORA is a .NET 10 ASP.NET Core MVC modular monolith using Entity Framework Core and SQL Server.

## Run on a team member's computer

Each member uses a separate local database named `NEXORA_Dev_<Member>`. The repository does not contain a database, and team members do not need access to the project owner's PC01 SQL Server.

1. Install the .NET 10 SDK and SQL Server Express or LocalDB.
2. Clone this repository and open PowerShell in its folder.
3. Follow the Vietnamese [installation guide](docs/HUONG_DAN_CAI_DAT.md), then consult [Team Development Setup](docs/TEAM_DEVELOPMENT_SETUP.md) for full policy details.
4. Configure the local target, apply migrations, and explicitly seed demo data using `tools/setup-development.ps1`.
5. Start the site with `.\tools\setup-development.ps1 -Run` and open the local URL it prints.

The Development connection policy requires the explicit `PersonalDevelopment` profile and a local SQL Server target with a `NEXORA_Dev_<Member>` database name. It rejects the main `NEXORA` and QA `NEXORA_IntegrationTests` databases. The owner's existing exact QA target remains a separate compatibility lane and is never seeded automatically.

## Projects

- `src/NEXORA.Web` — MVC application, Identity, local commands, and Razor UI.
- `src/NEXORA.Application` — application contracts and use cases.
- `src/NEXORA.Domain` — entities and business rules.
- `src/NEXORA.Infrastructure` — SQL Server persistence and EF Core migrations.
- `tests/NEXORA.Tests` — unit, policy, and HTTP tests. SQL integration tests require an explicitly approved local test database.

## Restore, build, and SQL-free tests

```powershell
dotnet restore .\NEXORA.sln
dotnet build .\NEXORA.sln
```

For the reviewed SQL-free test allowlist, see `.github/workflows/nexora-tests.yml`. Do not run SQL integration tests against the project owner's QA or main database.

## Safety

- Secrets belong in ASP.NET Core User Secrets or local environment variables, never in Git.
- No database is migrated at website startup. Applying migrations is an explicit setup step.
- Demo roles, accounts, catalog, consultation rules, and initial demo inventory are seeded only by explicit commands against a member-owned local Development database.
- Never copy QA/Production accounts, password hashes, or business data into a member's database.
- If Windows Smart App Control or Code Integrity blocks a locally built assembly, do not disable or bypass it; use a code-signing or administrator-approved allowlisting process.
