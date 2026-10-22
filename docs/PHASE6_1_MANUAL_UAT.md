# NEXORA – Phase 6.1 Manual UAT Checklist

**Purpose:** owner-run browser verification of Admin account management and staff invitation.<br>
**Status:** NOT RUN. Do not mark a case PASS without performing it.<br>
**No credentials belong in this file or chat.** Enter passwords only in the browser locally.

## Verified QA runtime and accounts (read-only snapshot, 2026-10-22)

- QA URL started for this UAT preparation: `http://127.0.0.1:5060`. It uses `ConnectionStrings__DefaultConnection` from the already-approved `NEXORA_SQLSERVER_TEST_CONNECTION` environment variable. The running process is kept alive for the owner; its lifetime is tied to the current terminal session.
- Read-only preflight confirmed `PC01\SQLEXPRESS`, `NEXORA_IntegrationTests`, `ONLINE`, 27 physical tables, the three expected migrations, and exactly the four roles `Admin`, `Customer`, `Sales`, `Warehouse`.
- Port `5059` was already occupied by a separate `dotnet` process and its Phase 6.1 routes returned 404. It was not stopped or used for UAT. Use the verified port `5060` below.
- Development startup queried the four existing roles and explicitly skipped Catalog/Consultation/Inventory demo seeders because the configured database is not the Development `NEXORA` database. No migration was run and no real email sender is configured in this process.

| Role | Existing QA identity | Current state | UAT use |
|---|---|---|---|
| Admin | UserId `82`; login identifier withheld because it is not a reserved `@nexora.test` address | Active, password set; has both `Admin` and `Customer` (legacy multi-role anomaly) | Use only if the owner already knows the login. It can access Admin, but do not use it as a clean single-role account or attempt to repair/change its roles during UAT. |
| Customer | Single-role candidates UserIds `81`, `83`, `805`; login identifiers withheld because they are not reserved `@nexora.test` addresses | Active, password set | Choose only an account whose QA login is already known to the owner. Do not reset credentials or mutate existing customer data as part of preparation. |
| Sales | `qa.sales@nexora.test`, UserId `803` | Active, password set, Sales only | Use the password already known locally to the owner; no password is recorded here. |
| Warehouse | `qa.warehouse@nexora.test`, UserId `804` | Active, password set, Warehouse only | Use the password already known locally to the owner; no password is recorded here. |

The snapshot confirms account/role rows and whether a password hash exists; it does not reveal or verify anyone's password. No QA account was created, reset, locked, unlocked, or changed during preparation. UserId `82` remains the only Admin and the known multi-role anomaly; it can access Admin because the persisted `Admin` role is still assigned. Do not use it for role-isolation comparisons. The current cookie validator checks that the role claims match persisted roles, but it does not itself reject a matching multi-role set; whether UserId `82` should retain both roles requires owner confirmation or a separately approved remediation.

## UAT case URL and role map

Use the base URL `http://127.0.0.1:5060` with these paths. For cases that involve POST actions, open the corresponding page and use its antiforgery-protected form; do not paste tokens into a URL.

| Case | URL / operation | Account or fixture | Expected result (see detailed steps below) |
|---|---|---|---|
| A01 | `/Admin/Users/Index` | Admin 82 | Account list opens; no password, token, hash, or security stamp is exposed. |
| A02 | `/Admin/Users/Index` | Anonymous; Customer 81/83/805; Sales 803; Warehouse 804; Admin 82 | Anonymous is redirected to sign-in; non-Admin is denied; Admin is allowed. |
| A03 | `/Admin/Users/Details/{id}` | Admin 82; inspect only a QA account | Safe profile, roles, state, and timestamps appear; credential material does not. |
| A04 | `/Admin/Users/Index?search={QA-name-or-fragment}` | Admin 82; reserved disposable QA fixture | Search returns the matching QA record without unrelated account data. |
| A05 | `/Admin/Users/Index?role={Customer\|Sales\|Warehouse\|Admin}&isActive={true\|false}&page=1` | Admin 82 | Role/status filters and bounded pagination behave correctly. |
| A06 | `/Admin/Users/CreateStaff` | Admin 82 | Only Sales and Warehouse can be selected; no password field is present. |
| A07 | `/Admin/Users/CreateStaff` → `/Admin/Users/Details/{new-id}`; `/dev/mailbox` | Admin 82; new reserved `@nexora.test` address | Pending Sales invitation appears in the Development mailbox; no external mail is sent. |
| A08 | `/Admin/Users/CreateStaff` → `/Admin/Users/Details/{new-id}`; `/dev/mailbox` | Admin 82; another reserved `@nexora.test` address | Pending Warehouse invitation appears; no external mail is sent. |
| A09 | `/Admin/Users/CreateStaff` | Admin 82; disposable duplicate/invalid input | Validation rejects the duplicate or malformed request and leaves no partial account. |
| A10 | Click the one-time link in `/dev/mailbox`; landing path `/account/ActivateStaff` | Admin 82 to read mailbox, then invited recipient in the same local browser | Fragment is removed from the address bar; the valid invitation permits password setup and redirects to sign-in. Never copy/save the link. |
| A11 | `/account/ActivateStaff` using a wrong, expired, or consumed disposable invitation | Disposable invited account | Generic invalid-link state; account remains unchanged and no token detail is shown. |
| A12 | `/Admin/Users/Details/{pending-id}` → POST `/Admin/Users/ResendInvitation/{id}`; inspect `/dev/mailbox` | Admin 82; disposable pending invitation | Old invitation no longer validates; the newest link is the only usable one; no duplicate account is created. |
| A13 | `/Admin/Users/Details/{activated-id}` → POST `/Admin/Users/Lock/{id}`, then make a protected request in the existing session | Admin 82 and disposable activated Sales/Warehouse session | Existing cookie is rejected on the next protected request. |
| A14 | `/Admin/Users/Details/{id}` → POST `/Admin/Users/Unlock/{id}` | Admin 82; same activated fixture from A13 | Explicit unlock permits a fresh login; the old session is not silently restored. |
| A15 | `/Admin/Users/Details/{pending-id}` → POST `/Admin/Users/Unlock/{id}` | Admin 82; a still-pending invitation fixture | Unlock is rejected; the account remains pending/inactive without a password. |
| A16 | `/Admin/Users/Details/{sales-id}` → POST `/Admin/Users/ChangeRole/{id}` | Admin 82; disposable Sales fixture | Sales→Warehouse is atomic; old Sales cookie is rejected; a fresh login has Warehouse only. |
| A17 | `/Admin/Users/Details/{warehouse-id}` → POST `/Admin/Users/ChangeRole/{id}` | Admin 82; different disposable Warehouse fixture | Warehouse→Sales is atomic; old Warehouse cookie is rejected; a fresh login has Sales only. |
| A18 | `/Admin/Users/CreateStaff` with a tampered role form value | Admin 82; disposable request only | Server validation rejects Customer/Admin assignment; no elevated account or role mapping is created. |
| A19 | `/Admin/Users/Details/82` and attempt the self-lock/self-role action only if the UI offers it | Admin 82, currently the sole Admin and multi-role | The server denies the action; verify state is unchanged. If an action reports success, stop UAT and report a blocker. Do not target this account for anomaly repair. |
| A20 | `/dev/mailbox` | Admin 82; then Customer/Sales/Warehouse and anonymous attempts | Only a local Development Admin can read the simulated mailbox; others are denied. No external email is sent. |

## Security POC evidence (automated, not browser UAT)

The following individual cases were read from `tests/NEXORA.Tests/TestResults/Phase61Final.trx`; each listed outcome is **Passed**. Paths are repository-relative.

| Security claim | Passed test(s) | Evidence file |
|---|---|---|
| Pending invitation requires inactive + unconfirmed + no password + exactly one approved staff role; a suspended account has a password and is not treated as pending, regardless of email-confirmed state. | `PendingStaffInvitation_IsInactiveUnconfirmedAndHasNoPassword` (Sales, Warehouse); `SuspendedStaff_HasPasswordAndCannotBeTreatedAsPendingInvitation` (`emailConfirmed: false`, `true`); `ActivatedStaff_IsActiveEvenWhenLegacyEmailConfirmationIsFalse` (Sales, Warehouse). | `tests/NEXORA.Tests/StaffAccountStatePolicyTests.cs` |
| Admin cannot unlock a pending invitation before a password is established; an activated account locked after activation cannot be reactivated by its old invitation. | `StaffInvitation_ExpiredTokenAndSuspendedAccount_CannotActivateOrBeUnlockedAsPending` (pending unlock blocked; activation then lock; old token invalid and activation rejected; explicit unlock of the activated user succeeds). | `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` |
| Expired, replayed, resent, and concurrent invitation tokens are rejected or accepted exactly once. | `ExpiredStaffInvitationToken_IsRejected`; `StaffInvitation_CreateActivateAndReplay_AreAtomicPurposeBoundAndOneUse`; `StaffInvitation_ResendInvalidatesOldToken_AndDoesNotCreateDuplicate`; `StaffInvitation_ConcurrentRedemption_AllowsOnlyOneActivation`. | `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` |
| Lock and role change invalidate the existing authentication cookie at its next protected request. | `AccountLock_RejectsExistingCookieOnTheVeryNextProtectedRequest`; `RoleChange_RejectsOldRoleCookieOnTheVeryNextProtectedRequest`. | `tests/NEXORA.Tests/AccountSessionRevocationSqlServerIntegrationTests.cs` |
| Multi-role and unknown-role anomalies are classified and not auto-corrected; unlock of a multi-role account and Customer promotion through staff role change are blocked. This does not prove that an active, matching multi-role cookie is denied: cookie validation checks equality with persisted roles, so UserId `82` still has Admin access through its existing Admin mapping. | `AccountWithMultipleRoles_IsInconsistentAndMustNotBeAutomaticallyRewritten`; `UnknownPersistedRole_IsInconsistent`; `UnlockDoesNotReactivateAnAccountWithMultipleRoles`; `CustomerCannotBePromotedThroughStaffRoleChange`. | `tests/NEXORA.Tests/StaffAccountStatePolicyTests.cs`; `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs`; role-claim comparison in `src/NEXORA.Web/Identity/AuthoritativeAccountCookieValidation.cs` |
| Admin-only access, role tampering, and activation antiforgery are checked at HTTP level. | `UsersArea_IsAdminOnly_AndCreateFormNeverOffersAdminRole`; `AdminTamperedCreateRole_IsRejectedByServerValidationWithAntiforgery`; `StaffActivationKeepsInvitationSecretsOutOfRequestUrlAndValidatesWithAntiforgery`. | `tests/NEXORA.Tests/AccountAdministrationHttpIntegrationTests.cs` |

Static code review additionally confirmed that activation secrets are placed in the URL fragment (not the HTTP request target), `history.replaceState` removes the fragment before fetch, the validation POST sends antiforgery in `RequestVerificationToken`, both POST actions validate antiforgery, the activation page sets `Cache-Control: no-store` and `Referrer-Policy: no-referrer`, and the shared layout uses local JavaScript/CSS assets (no third-party script on that page). Browser execution of the fragment JavaScript remains part of owner UAT, not a passed test here.

## Safe setup

1. Use `http://127.0.0.1:5060`, which was launched with the approved QA connection override. If you restart it, verify the target first and use the QA-only command below; never use the Development `NEXORA` connection for these write scenarios.
2. Confirm the QA database is online and the expected three migrations/four roles are present before opening the application.
3. Sign in with a QA Admin account whose credentials are already known to the owner. Do not alter the pre-existing multi-role anomaly or existing QA Sales/Warehouse accounts.
4. For invitation checks, use only a unique reserved QA address under `nexora.test` and the Development in-memory mailbox. Do not send to a real recipient. Real email delivery remains blocked until a provider and recipient are separately approved.
5. For role/session checks, use only disposable QA users created during this checklist. Delete only those exact fixtures after the owner finishes and authorizes cleanup.

### QA-only launch command (PowerShell)

This uses the already-configured environment variable and does not print its value. If it is absent, stop; do not substitute the local Development connection string.

```powershell
if ([string]::IsNullOrWhiteSpace($env:NEXORA_SQLSERVER_TEST_CONNECTION)) { throw 'QA connection is not configured.' }
$env:ConnectionStrings__DefaultConnection = $env:NEXORA_SQLSERVER_TEST_CONNECTION
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:StaffInvitation__PublicBaseUrl = 'http://127.0.0.1:5060'
dotnet .\tests\NEXORA.Tests\bin\Phase61\Debug\net10.0\NEXORA.Web.dll `
  --contentRoot D:\Projects\NEXORA\src\NEXORA.Web `
  --urls http://127.0.0.1:5060
```

The QA connection is approved by the same guard used in the SQL integration-test support: it requires Windows Authentication, `PC01\SQLEXPRESS`, `NEXORA_IntegrationTests`, ONLINE state, the expected migration baseline, 27 tables, and exactly the four approved roles. Startup's Development seeders explicitly skip this database. Do not run migrations or any command that targets the main `NEXORA` database.

### Evidence fields

For each case record: **Result** (`NOT RUN` / `PASS` / `FAIL` / `BLOCKED`), date/time, browser/version, URL, observed behavior, and screenshot path if the owner chooses to capture one. Record failures and attach genuine screenshots in the project's approved location. Never include passwords, invitation links/tokens, password hashes, security stamps, or real customer data.

| ID | Steps | Expected result | Result / evidence |
|---|---|---|---|
| A01 | Open the QA site, sign in as Admin, navigate to `/Admin/Users`. | Admin can open the account list; page renders without exposing credential fields. | NOT RUN — result: ___; notes/screenshot: ___ |
| A02 | Open `/Admin/Users` as anonymous, then repeat as Customer, Sales and Warehouse accounts if available. | Anonymous is sent to sign-in; non-Admin roles are denied. | NOT RUN — result: ___; notes/screenshot: ___ |
| A03 | In the account list, open an existing QA account's details. | Only profile/role/state/timestamps and password-presence are shown; no PasswordHash, token or SecurityStamp. | NOT RUN — result: ___; notes/screenshot: ___ |
| A04 | Search the account list by a unique QA name and then a QA email fragment. | Results match the search; unrelated account records are not shown. | NOT RUN — result: ___; notes/screenshot: ___ |
| A05 | Filter by Customer, Sales, Warehouse, Admin and active/inactive; try next/previous page if available. | Filters and bounded pagination preserve the selected query; invalid/out-of-range page does not break the page. | NOT RUN — result: ___; notes/screenshot: ___ |
| A06 | Inspect the Create Staff form. | Only Sales and Warehouse are selectable; Admin/Customer cannot be chosen and no password field exists. | NOT RUN — result: ___; notes/screenshot: ___ |
| A07 | Create a unique QA Sales user using a reserved `nexora.test` address. Check the Development mailbox. | User is pending/inactive with no password and exactly Sales role; fake email contains an invitation link. No external email is sent. | NOT RUN — result: ___; fixture ID: ___; notes: ___ |
| A08 | Repeat A07 with a separate QA Warehouse address. | User is pending/inactive with no password and exactly Warehouse role. | NOT RUN — result: ___; fixture ID: ___; notes: ___ |
| A09 | Try a duplicate address and invalid/missing fields. | Validation prevents duplicate account creation; no partial account is left. | NOT RUN — result: ___; notes/screenshot: ___ |
| A10 | Open the fake invitation link. Observe the URL, then enter an acceptable password and confirmation. | Invitation is validated; token stays in URL fragment and is removed from the address bar; activation succeeds and redirects to sign-in. | NOT RUN — result: ___; do not save/copy token: ___ |
| A11 | Try a wrong, expired or already-consumed invitation on a disposable fixture. | Generic invalid-link state; account remains inactive/unmodified; no token details appear in logs/UI. | NOT RUN — result: ___; notes/screenshot: ___ |
| A12 | On another pending fixture, resend the invitation, then try the old link and the newest Development-mailbox link. | Old link fails; newest link remains usable; no duplicate account is created. | NOT RUN — result: ___; notes: ___ |
| A13 | Sign in with an activated disposable staff fixture, keep that session open, then have Admin lock it. Use the existing session for the next protected request. | Next protected request is denied/sign-in required; it does not continue with a stale active cookie. | NOT RUN — result: ___; notes: ___ |
| A14 | Admin unlocks the same activated fixture; sign in again. | Account becomes usable after explicit unlock; existing session is not silently restored. | NOT RUN — result: ___; notes: ___ |
| A15 | Attempt to unlock a still-pending invitation before password setup. | Operation is rejected; account stays inactive/pending. | NOT RUN — result: ___; notes: ___ |
| A16 | Sign in as a disposable Sales fixture, then Admin changes it to Warehouse; try its existing Sales session against Sales and Warehouse routes. | Role replacement is atomic; old session is rejected on the next protected request; fresh login has only Warehouse access. | NOT RUN — result: ___; notes: ___ |
| A17 | Repeat A16 for Warehouse→Sales using a different disposable fixture. | Fresh session has only Sales access; old Warehouse session is rejected. | NOT RUN — result: ___; notes: ___ |
| A18 | Inspect UI and submit a tampered role value for Customer→Sales or Admin assignment using browser tools. | Server rejects both; no Customer promotion or Admin grant occurs. | NOT RUN — result: ___; notes/screenshot: ___ |
| A19 | Attempt self-lock/self-role-change using the current Admin session. Do not target the existing inconsistent account for repair. If testing last-Admin guard requires a disposable second Admin, stop until separately authorized. | Self-lock/self-demotion is denied; last active Admin cannot be removed. Existing multi-role data remains unchanged. | NOT RUN — result: ___; notes/screenshot: ___ |
| A20 | Open `/dev/mailbox` from loopback as Admin, then try as non-Admin and from a non-loopback environment if available. Inspect the activation URL format without copying its secret. | Only a local Development Admin can access mailbox; message is simulated; the request URL has no email/token query parameters. | NOT RUN — result: ___; notes/screenshot: ___ |

## Completion record

- Cases run: ___ / 20
- PASS: ___  FAIL: ___  BLOCKED: ___  NOT RUN: ___
- Browser/device/viewport(s): ___
- Genuine evidence location (if any): ___
- Owner confirmation/date: ___
- Follow-up issues: ___

Do not report Phase 6.1 as finally accepted until required UAT and the email-delivery decision have been reviewed by the owner. Phase 6.2 is outside this checklist.
