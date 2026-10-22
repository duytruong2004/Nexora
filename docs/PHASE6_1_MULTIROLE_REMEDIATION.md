# NEXORA – Phase 6.1 Multi-Role Remediation Review

**Review date:** 2026-10-22
**Change ID:** `NEXORA-P61-QA-ROLE82`
**Status:** SECURITY CODE PREPARED; QA SQL PACKAGE READY FOR REVIEW; DATA CHANGE WAITING FOR EXPLICIT OWNER APPROVAL
**Manual UAT:** NOT RUN
**Real email UAT:** BLOCKED/PENDING

## 1. Scope and safety boundary

This review prepares the approved single-role authentication policy and a guarded proposal for the existing QA anomaly. It does not change any user, role mapping, password, email, business record, database schema, or migration. No website was restarted or replaced. No database other than the explicitly verified QA database was accessed.

The main `NEXORA` database was not queried or otherwise accessed. SQL preflight commands connected directly with Windows Authentication to `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`; each business-data batch stopped unless the reported server, database name, and database state matched the approved QA target.

## 2. Read-only QA preflight

| Check | Expected | Actual | Result | Evidence |
|---|---|---|---|---|
| SQL target | `PC01\\SQLEXPRESS / NEXORA_IntegrationTests` | Exact match | PASS | `SERVERPROPERTY('ServerName')`, `DB_NAME()` |
| Database state | ONLINE | ONLINE | PASS | `sys.databases.state_desc` |
| Migration history | Existing QA baseline only; no new migration | `20261015173046_InitialFoundation`, `20261016133620_AddCheckoutRequestId`, `20261019174632_AddOrderCancellationRequestFields` | PASS | `dbo.__EFMigrationsHistory` |
| Physical tables | 27 expected | 27 | PASS | `sys.tables` |
| Business/technical split | 22 business + 4 Identity + history | 22 + 4 + `__EFMigrationsHistory` | PASS | Table count and existing Phase 6.1 QA baseline |
| Roles | Exactly Customer, Sales, Warehouse, Admin | Exactly 4: Customer (ID 1), Sales (ID 2), Warehouse (ID 3), Admin (ID 4) | PASS | `dbo.roles` |
| QA users / mappings | Previous snapshot: 6 users / 7 mappings | 6 users / 7 mappings | PASS | `dbo.users`, `dbo.user_roles` |
| UserId 82 state | Existing active account; do not expose credentials | Exists; active; `EmailConfirmed=false`; password hash exists; security and concurrency stamps exist. Secret values were not selected. | PASS | `dbo.users`, presence-only projection |
| UserId 82 roles | Expected legacy anomaly: Admin + Customer | Admin (role ID 4), Customer (role ID 1), one mapping each; no third role or duplicate mapping | PASS | `dbo.user_roles` joined to `dbo.roles` |
| Active Admin-only accounts | Must be known before enabling the strict guard | **0** | PASS with deployment blocker | Role cardinality aggregate |
| Active accounts carrying Admin | Informational | 1; it is UserId 82 and is multi-role | PASS with deployment blocker | Role cardinality aggregate |
| UserId 82 related records | Inspect only; preserve history | Orders 0; carts 0; favorites 0; reviews 0; inventory-transaction actor rows 0 | PASS | Read-only counts using model-mapped tables |

No email, password hash, security stamp, concurrency stamp value, password, cookie, or token was selected or written to a report. No audit-history table exists in the approved schema; durable audit-history impact is therefore not claimed.

### Deployment lockout risk

UserId 82 is the only active account with the Admin role, and currently has both Admin and Customer. A strict one-role login guard deployed against this unchanged data will deny the only Admin login. The change must not be deployed to the QA website until the separately approved mapping cleanup has been applied and verified, or another owner-approved recovery path is established. No extra Admin is proposed or created here.

## 3. Source findings and remediation

### Existing behavior

- `AccountController.Login` checked active status, then used `PasswordSignInAsync`. That method could issue an authentication cookie before the controller checked role cardinality.
- `AuthoritativeAccountCookieValidation` compared cookie roles with persisted roles after applying `Distinct`. A cookie containing `{Admin, Customer}` therefore remained valid when the database also contained `{Admin, Customer}`.
- The Phase 6.1 account mutation policy already classified multi-role accounts as inconsistent and did not silently rewrite them. Ordinary role-change/unlock operations are not the repair path for UserId 82.

### Prepared behavior

- Login now verifies the password with `CheckPasswordSignInAsync`, then reads current roles and checks `IsActive` plus exactly one canonical role from the four-role allowlist. Only then does it call `SignInAsync` to issue a cookie. The public error remains generic; internal role details are not returned or logged.
- Cookie validation now reads active state, security stamp, and persisted roles through `IAccountAuthenticationStateReader`. It rejects a missing/inactive account, stale or missing security stamp, zero/multiple/unknown role claims, zero/multiple/unknown persisted roles, or a role mismatch. Duplicate claims are not deduplicated. Rejection calls `RejectPrincipal` and signs out the Identity application scheme.
- The production state reader uses `AppDbContext` with `AsNoTracking`; a fake reader is used by isolated HTTP tests. The existing per-request authoritative check remains fail-closed if the store cannot be read.
- UserManager, RoleManager, Identity options, role-transition policy, invitation state, and the four roles remain the only authentication/authorization system. No bypass, endpoint, role mutation, schema change, or migration was added.
- Role assignment, lock state, and invitation rules were not changed. An inactive pending invitation still cannot sign in; an inactive suspended account remains blocked.

### Files changed for the remediation

- `src/NEXORA.Application/Identity/AccountAdministrationContracts.cs` – shared single-role policy and authentication-state reader contract.
- `src/NEXORA.Infrastructure/Identity/AccountAuthenticationStateReader.cs` – authoritative read-only Identity state implementation.
- `src/NEXORA.Infrastructure/DependencyInjection.cs` – registers the reader.
- `src/NEXORA.Web/Controllers/AccountController.cs` – validate password and role policy before issuing the cookie.
- `src/NEXORA.Web/Identity/AuthoritativeAccountCookieValidation.cs` – reject multi-role, unknown, stale, inactive, and mismatched principals.
- `tests/NEXORA.Tests/AccountAuthenticationPolicyTests.cs` – single-role policy cases.
- `tests/NEXORA.Tests/AccountAuthenticationCookieHttpTests.cs` – isolated HTTP cookie-validation cases.
- `tests/NEXORA.Tests/AccountLoginPolicyHttpTests.cs` – isolated login HTTP cases using a fake Identity store.
- `tests/NEXORA.Tests/SqlServerFactAttribute.cs` and `CheckoutSqlServerFactAttribute.cs` – SQL test discovery now delegates to the exact QA connection validator.
- `tests/NEXORA.Tests/AdminProvisioningCommandTests.cs` – includes a negative case for a server name that merely contains the approved instance string.
- `tests/NEXORA.Tests/AccountAdministrationHttpIntegrationTests.cs` – test host can locate the web content root when build output is isolated.
- `docs/PHASE6_1_MULTIROLE_REMEDIATION.md` – this review and unexecuted change plan.

## 4. Security POC evidence

The existing `Phase61Final.trx` artifact records **240 passed, 0 failed, 0 skipped** for the earlier Phase 6.1 build. Those results prove the named invitation/session/role-mutation cases below; they do not prove the new single-role login guard. New isolated guard tests are reported separately in Section 5.

| Security question | Test and file | Recorded result | What it proves / limit |
|---|---|---|---|
| Pending invitation vs suspended account | `PendingStaffInvitation_IsInactiveUnconfirmedAndHasNoPassword`; `SuspendedStaff_HasPasswordAndCannotBeTreatedAsPendingInvitation`; `ActivatedStaff_IsActiveEvenWhenLegacyEmailConfirmationIsFalse` in `tests/NEXORA.Tests/StaffAccountStatePolicyTests.cs` | Passed in `Phase61Final.trx` | Invitation requires inactive + unconfirmed + no password + one staff role. An inactive account with a password is suspended, not pending. A valid active staff account is not rejected solely for legacy email-confirmation state. |
| Can Admin Unlock activate a pending account? | `StaffInvitation_ExpiredTokenAndSuspendedAccount_CannotActivateOrBeUnlockedAsPending` and `UnlockDoesNotReactivateAnAccountWithMultipleRoles` in `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Pending invitation is not unlocked as an activated account; a multi-role account is not repaired by unlock. These were prior SQL integration runs on the QA database, not rerun in this remediation turn. |
| Can an old invitation activate a locked account? | `StaffInvitation_ExpiredTokenAndSuspendedAccount_CannotActivateOrBeUnlockedAsPending` in `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Expired/old invitation state does not reactivate the suspended fixture. This test does not mutate UserId 82. |
| Expiry | `ExpiredStaffInvitationToken_IsRejected` in `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Expired invitation token is rejected. |
| Replay / one-time activation | `StaffInvitation_CreateActivateAndReplay_AreAtomicPurposeBoundAndOneUse` in `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Activation is purpose-bound and a replay is rejected. |
| Resend invalidates prior link | `StaffInvitation_ResendInvalidatesOldToken_AndDoesNotCreateDuplicate` in `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Old token is invalidated and resend does not create a duplicate user. |
| Concurrent activation | `StaffInvitation_ConcurrentRedemption_AllowsOnlyOneActivation` in `tests/NEXORA.Tests/AccountAdministrationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Concurrent redemption permits one activation. |
| Lock invalidates cookie next request | `AccountLock_RejectsExistingCookieOnTheVeryNextProtectedRequest` in `tests/NEXORA.Tests/AccountSessionRevocationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Existing cookie is denied on the first protected request after account lock commits. |
| Role change invalidates old cookie | `RoleChange_RejectsOldRoleCookieOnTheVeryNextProtectedRequest` in `tests/NEXORA.Tests/AccountSessionRevocationSqlServerIntegrationTests.cs` | Passed in `Phase61Final.trx` | Sales/Warehouse stale role cookie is denied on the next protected request. |
| Old matching multi-role cookie | No test in the earlier POC covered the case where cookie and DB both contained Admin + Customer. The gap is addressed by `MatchingMultiRoleCookieAndPersistedRoles_AreRejected` in `tests/NEXORA.Tests/AccountAuthenticationCookieHttpTests.cs`. | Passed in the earlier isolated 33-test remediation run; not rerun after the current test-harness changes | The actual cookie middleware rejects the multi-role cookie through a fake authoritative state reader. No SQL write is involved. |
| Multi-role login | `MultiRoleLogin_DoesNotIssueAuthenticationCookie` in `tests/NEXORA.Tests/AccountLoginPolicyHttpTests.cs` | Passed in the earlier isolated 33-test remediation run; not rerun after the current test-harness changes | A valid password is checked, role lookup occurs, the public response is generic, no application auth cookie is issued, and the protected Admin endpoint redirects to login. |
| Zero-role / inactive login | `ZeroRoleLogin_DoesNotIssueAuthenticationCookie` and `InactiveUserLogin_DoesNotIssueAuthenticationCookie` in `tests/NEXORA.Tests/AccountLoginPolicyHttpTests.cs` | Added to current source; execution BLOCKED | Intended to prove no cookie is issued for zero-role or inactive users. These HTTP cases have not run. |
| Single-role login for each role | `SingleRoleLogin_StillIssuesCookieAndCanAccessItsRoleEndpoint` (four theory cases: Admin, Customer, Sales, Warehouse) in `tests/NEXORA.Tests/AccountLoginPolicyHttpTests.cs` | Current four-role matrix added; execution BLOCKED | Unit policy theory cases for all four roles passed in the earlier isolated run, but endpoint login and cookie issuance for the current implementation have not been verified. A preceding Customer-only HTTP attempt failed before the fake role store was corrected. |
| Cookie role cardinality / stale role / inactive state | `MatchingMultiRoleCookieAndPersistedRoles_AreRejected`, `AdminCookie_IsRejectedAfterPersistedRoleChangesToCustomer`, `SalesCookie_IsRejectedAfterPersistedRoleChangesToWarehouse`, `Cookie_IsRejectedImmediatelyAfterAccountBecomesInactive`, and `RejectedCookieRedirectsToAnonymousLoginWithoutRedirectLoop` in `tests/NEXORA.Tests/AccountAuthenticationCookieHttpTests.cs` | Passed in the earlier isolated 33-test remediation run; not rerun against the latest test assembly | Covers multi-role cookie rejection, role-change staleness, inactive account rejection, and a protected-request redirect to a functioning anonymous login page. |

### URL fragment, antiforgery, and token leakage

Existing HTTP coverage is `StaffActivationKeepsInvitationSecretsOutOfRequestUrlAndValidatesWithAntiforgery` in `tests/NEXORA.Tests/AccountAdministrationHttpIntegrationTests.cs`, recorded Passed in `Phase61Final.trx`. It verifies that the invitation token is handled from the URL fragment by `src/NEXORA.Web/wwwroot/js/staff-activation.js`, validated through an antiforgery-protected POST, and absent from the request query. The final activation POST is also antiforgery-protected. Tokens/passwords are not included in application logs. This is automated HTTP/JS-flow coverage; it is not browser UAT or real mailbox delivery.

## 5. Remediation-turn build and test results

### Build

- `dotnet restore NEXORA.sln`: Passed; dependencies were up to date.
- Fresh `dotnet build NEXORA.sln --no-restore -p:OutDir=D:\Projects\NEXORA\tests\NEXORA.Tests\bin\Phase61SecurityRemediation\Debug\net10.0\`: Passed after the latest login-test and SQL-guard changes, 0 warnings and 0 errors. The isolated output avoided assemblies held by the running QA website.
- `git diff --check`: Passed after the latest source and report edits; Git emitted only LF/CRLF normalization warnings.

### Windows Application Control blocker

The latest test execution attempt was blocked by Windows Code Integrity before the test framework ran. `Microsoft-Windows-CodeIntegrity/Operational` Event 3077 records `testhost.exe` attempting to load the unsigned `NEXORA.Tests.dll`; the enforced policy is `VerifiedAndReputableDesktop` (policy name from the event, policy ID `27555.1000.240208`, policy GUID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`). The event reports requested signing level `2`, validated signing level `1`, and status `0xc0e90002`. The file identified in the event was under the project test `bin` directory. `Get-AuthenticodeSignature` reported `NEXORA.Tests.dll` as `NotSigned`, while `testhost.exe` was Microsoft-signed. The latest build output also reports all five custom assemblies (`NEXORA.Tests`, `NEXORA.Web`, `NEXORA.Infrastructure`, `NEXORA.Application`, and `NEXORA.Domain`) as `NotSigned`; allowing only the test DLL may reveal further blocks as those assemblies load. No matching AppLocker EXE/DLL events were found; this is evidence of a Code Integrity/WDAC-style enforcement block, not an AppLocker test failure.

Read-only `CiTool.exe -lp -json` returned Access Denied (`OperationResult -2147024891`), so the full active policy configuration could not be inspected from this account. No Windows security setting or policy was changed. Moving or copying the unsigned assemblies to another writable directory, changing execution flags, or disabling enforcement would not be an acceptable resolution. A compliant next step is for the machine administrator/security owner to approve the complete custom test artifact through the existing organization-approved signing/allowlisting process, or to run the same source and tests in an approved CI/test environment. After that approval, rebuild and rerun the current source from that approved process; do not treat the historical TRX as current evidence.

### Safe tests actually executed

The first isolated run after the security changes ran policy, cookie HTTP, login HTTP, profile HTTP, and account-administration HTTP tests: **33 passed, 0 failed, 0 skipped**. It used fake stores/readers and did not connect to SQL Server. This run included multi-role login denial, the cookie cardinality/stale-role/inactive/redirect-loop cases, profile/logout, and invitation-antiforgery coverage. The run predates the current positive-login test matrix and corrected role-store fake.


The next run added a Customer-only login HTTP test and reported **33 passed, 1 failed, 0 skipped**: `SingleRoleCustomerLogin_StillIssuesCookieAndCanAccessCustomerEndpoint` returned HTTP 500. Inspection found the fake `IRoleStore<AppRole>` omitted `IRoleClaimStore<AppRole>`, which Identity's role claims principal factory requires when issuing a successful cookie. The fake now implements both interfaces and returns an empty role-claim collection. The current source expands the positive login test to all four roles and adds zero-role and inactive-login HTTP cases. The harness now implements `IUserStore`, `IUserEmailStore`, `IUserPasswordStore`, `IUserRoleStore`, `IRoleStore`, and `IRoleClaimStore`; compilation succeeds, but runtime compatibility after the correction is **NOT VERIFIED** because Windows Application Control blocked the rebuilt test DLL (`0x800711C7`).

The SQL fact attributes were also tightened to use `QaStaffProvisioningCommand.IsApprovedQaConnectionString`, which requires exact `PC01\\SQLEXPRESS`, exact `NEXORA_IntegrationTests`, and Windows Authentication. `AdminProvisioningCommandTests.QaStaffProvisioning_OnlyAcceptsTheDedicatedIntegratedSecurityDatabase` includes a lookalike server-name rejection case. These guard changes compile, but their tests are not executed in the current source run.

Current per-case disposition:

| Required check | Current-source test | Result in this remediation turn |
|---|---|---|
| Admin-only login | `SingleRoleLogin_StillIssuesCookieAndCanAccessItsRoleEndpoint` `[InlineData(Admin)]` | BLOCKED; not executed |
| Customer-only login | Same theory `[InlineData(Customer)]` | BLOCKED; the earlier version of this case failed with HTTP 500 before `IRoleClaimStore` was added; corrected case not rerun |
| Sales-only login | Same theory `[InlineData(Sales)]` | BLOCKED; not executed |
| Warehouse-only login | Same theory `[InlineData(Warehouse)]` | BLOCKED; not executed |
| Zero-role login | `ZeroRoleLogin_DoesNotIssueAuthenticationCookie` | BLOCKED; added after the last runnable test attempt |
| Multi-role login denied | `MultiRoleLogin_DoesNotIssueAuthenticationCookie` | PASS in earlier isolated 33-test run; not rerun on current test assembly |
| Multi-role cookie denied | `MatchingMultiRoleCookieAndPersistedRoles_AreRejected` | PASS in earlier isolated 33-test run; not rerun on current test assembly |
| Stale role cookie denied | `AdminCookie_IsRejectedAfterPersistedRoleChangesToCustomer`; `SalesCookie_IsRejectedAfterPersistedRoleChangesToWarehouse` | PASS in earlier isolated 33-test run; SQL-backed revocation test is historical baseline only |
| Inactive account denied | `Cookie_IsRejectedImmediatelyAfterAccountBecomesInactive`; current login case `InactiveUserLogin_DoesNotIssueAuthenticationCookie` | Cookie case PASS in earlier isolated run; current login case BLOCKED |
| Pending invitation unaffected | `PendingInvitation_CannotIssueCookie`; `StaffActivationKeepsInvitationSecretsOutOfRequestUrlAndValidatesWithAntiforgery` | Policy and HTTP flow PASS in earlier isolated run; SQL activation lifecycle evidence is from historical `Phase61Final.trx` |
| No redirect loop | `RejectedCookieRedirectsToAnonymousLoginWithoutRedirectLoop` | PASS in earlier isolated 33-test run; not rerun on current test assembly |
| Identity role-store runtime compatibility | `TestRoleStore : IRoleStore<AppRole>, IRoleClaimStore<AppRole>` in `AccountLoginPolicyHttpTests.cs` | Build PASS; successful cookie issuance after correction BLOCKED, so runtime compatibility remains unverified |

No test was run against the main `NEXORA` database. SQL integration tests that write or clean fixtures were not run in this remediation turn; QA SQL access remained read-only.

The old 240/240 `Phase61Final.trx` is historical baseline evidence only. It is not the result for the current source tree.

### Approved test environment availability and source reproduction

No approved test runner is available in this workspace: there is no `.github/workflows` directory, no runner-related environment variables or Windows service, and `gh`, `act`, and Docker are unavailable. `wsl.exe` exists, but this WSL installation has not been designated as an approved test environment and was not used. The local Code Integrity policy is enforced, and `CiTool` policy inspection is denied to this account. Therefore no new TRX was produced; the only TRX located is the historical `Phase61Final.trx`.

The exact source base is branch `master` at Git HEAD `ab5d945018b89d69beb72d9897594cf270e15949`, plus the current uncommitted working-tree overlay. The working tree contains **10 modified tracked files and 31 untracked files**; checking out HEAD alone will omit the Phase 6.1 Identity, controller, view, test, and guard changes. A source-input fingerprint was calculated over the sorted relative paths and SHA-256 file hashes for all 309 files under `src/`, `tests/`, and the solution/build entry files, excluding `bin/` and `obj/`: `b87b773754a15375622757541b2a7a9191d76562a5348f1b7022e7fad1a43d6b`. The latest unsigned test artifact SHA-256 is `d237809a3108f6c797cb536452795ef9f0609376e547afa1118ea879e21a740d`.

To reproduce on a security-approved runner, transfer the entire current working tree through the approved internal channel, preserving all modified and untracked source files; alternatively, start from the exact base commit, create a binary-safe tracked patch with `git diff HEAD --binary --output=<secure-transfer-path>\NEXORA-P61-tracked.patch`, copy each path listed by `git ls-files --others --exclude-standard` while preserving its relative path, then apply the patch with `git apply --binary`. Do not run from a HEAD-only checkout. Recompute the source-input fingerprint before building. Exclude build outputs and local secrets from transfer; keep QA connection configuration in that runner's protected environment.

Once the approved runner can reach the QA instance, configure only its process-level test environment with Windows Authentication to the dedicated database, then run restore/build/test and save a new TRX outside the source tree. The SQL test attributes now reject any connection string other than the exact approved target; Checkout SQL tests additionally require explicit `NEXORA_CHECKOUT_SQL_TESTS=1`. If that runner cannot verify or reach `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`, record the SQL tests as **NOT RUN** and do not point them at `NEXORA` or substitute mocks for SQL evidence.

## 6. QA-only mapping change proposal — NOT APPROVED FOR EXECUTION

### Exact intended change

Target: `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`
User: `dbo.users.id = 82`
Before, verified: active; one `Admin` mapping (`role_id = 4`) and one `Customer` mapping (`role_id = 1`).
After: same user, active state and Admin role; remove exactly the single Customer mapping.
Expected direct row count deleted: **1**. Expected global `user_roles` mapping count: 7 → 6.

Unchanged: user row, name/email, password, `IsActive`, role rows, other user mappings, orders, cart, favorites, reviews, inventory actor history, all other business data. No schema change or migration.

### Transaction and concurrency plan

The verified mapping table is `dbo.user_roles`, explicitly mapped by `AppDbContext` as `user_roles`. `IdentityAccountAdministrationService` acquires `NEXORA:ACCOUNT:POLICY` with `sp_getapplock`, `LockMode='Exclusive'`, `LockOwner='Transaction'`, the current database's default `public` principal, and a bounded 15-second timeout while its operations run inside EF Core's transaction. The reviewed script must:

1. Use a new SQL connection explicitly targeting the approved instance/database; verify `SERVERPROPERTY('ServerName')`, `DB_NAME()`, and ONLINE state before business reads.
2. Begin a `SERIALIZABLE` transaction with `XACT_ABORT ON`.
3. Acquire `NEXORA:ACCOUNT` and then the service's exact `NEXORA:ACCOUNT:POLICY` resource, each with `public`, Exclusive, Transaction ownership, and 15-second timeout. Names are not hierarchical; no root `NEXORA` lock is used. The second key serializes against account/role mutations made through the application.
4. Hold transaction-duration table locks on the affected Identity and migration-history tables, then validate the mapped schema and migration baseline inside the transaction. Require an operator-confirmed schema-change freeze because arbitrary DDL outside these locked objects is not coordinated by the script. Resolve Admin/Customer IDs by canonical names, with IDs 4/1 used only as drift checks. Require UserId 82 active, exactly two mappings (one Admin and one Customer), no third role, four roles total, six users, and seven mappings before mutation. Any mismatch aborts.
5. Delete only `dbo.user_roles` where `user_id = 82` and `role_id = 1`; require `@@ROWCOUNT = 1`.
6. Before commit, re-read and require UserId 82 still active with exactly one Admin mapping and no Customer mapping; require no other role for UserId 82, the active Admin-role account count still equals one, and total mappings exactly one lower (7 → 6). The only permitted affected-row count is **1**. Roll back on any failed assertion.
7. Verify from a fresh read after commit. If the connection drops or commit outcome is uncertain, inspect the target state first; never blindly repeat the delete.

This transaction does not update SecurityStamp. The current cookie validator rejects a cookie carrying both Admin and Customer after persisted roles become Admin-only, because it rejects multi-role cookie claims and role/database mismatches. It does **not** guarantee rejection of every previously issued cookie: a still-valid cookie containing only Admin now matches the persisted role and unchanged security stamp, so it may continue to be accepted. This one-row SQL change must not be described as global session revocation. If the owner requires every existing session to be invalidated, prepare a separate, explicitly approved Identity session-revocation operation (for example, `UserManager.UpdateSecurityStampAsync`) and test it independently; it is intentionally absent from these SQL scripts. The owner must verify Admin login and protected access after remediation. The strict guard must not be deployed before confirming fresh Admin-only login and protected access work. Because this account is currently the only active Admin-role user, a failed post-change login is a stop condition; do not create another Admin or add a bypass.

### Script of record

The SQL code outline formerly shown here is removed because it predates the final guards and could be mistaken for the executable script. The only remediation script for owner review is `docs/PHASE6_1_QA_ROLE82_REMEDIATION.sql`; it remains unexecuted, with `@OwnerExecutionApproved = 0` and `@SchemaChangeFreezeConfirmed = 0`. The postcheck is `docs/PHASE6_1_QA_ROLE82_POSTCHECK.sql`. Both exact files and a fresh read-only preflight must be reviewed again before any separately authorized execution. Do not copy an older outline from prior versions of this document.

## 7. Rollback and recovery

- **Before commit:** any failed guard, timeout, unexpected row count, changed target state, or SQL error rolls the transaction back; no partial mapping change is retained.
- **After commit:** do not automatically restore the Customer mapping because that recreates the approved multi-role anomaly. First capture the actual post-state and compare it with the recorded pre-state; prepare a narrowly scoped recovery request and obtain owner approval before any further SQL change. Do not restore the full database for this one-row change.
- **Admin access:** the planned change leaves UserId 82 active and Admin-only. A cookie still containing Admin + Customer is expected to be rejected by role-cardinality/mismatch validation. An already issued Admin-only cookie may still be accepted because SecurityStamp is unchanged. The owner must explicitly sign in and verify a fresh Admin-only session. If login or protected access fails, stop. Do not create an Admin, assign an extra role, reset a password, or use an unapproved bypass; use a separately reviewed privileged recovery process.
- **Deployment order:** approve and apply the exact QA mapping correction first; verify the database postcondition through a new connection; explicitly sign in and verify Admin-only access on the intended application version; then separately approve/deploy the fail-closed code and verify its behavior. Do not infer authentication success from the SQL postcheck, and do not deploy the guard against the current multi-role pre-state.

## 8. Change Request for owner review (prior draft; see Section 10 for current package)

### Change Request

- **Change ID:** `NEXORA-P61-QA-ROLE82`
- **Target:** `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`
- **User:** UserId `82`
- **Current roles:** `Admin`, `Customer` (read-only verified)
- **Expected roles:** `Admin` only
- **Exact data change:** remove one and only one `Customer` mapping for UserId 82 (`dbo.user_roles.user_id = 82`, `role_id = 1`); expected affected rows = 1
- **Unchanged:** user identity/status/password/email, Admin role, all other mappings/users, and business history/data
- **Risk:** temporary or persistent Admin lockout, stale cookies, loss of Customer access for this account, data drift, concurrent account mutation, ambiguous commit
- **Mitigations:** transaction-owned application lock, SERIALIZABLE + row/range locks, strict pre/post assertions, fresh-connection verification, no blind retry
- **Rollback:** transaction rollback before commit; after commit, separate owner-approved narrow recovery only. Do not automatically restore Customer.
- **Evidence:** verified QA preflight, build result, isolated HTTP guard tests, and this unexecuted plan
- **SQL design readiness at the time of this draft:** only a review plan existed; Section 10 now contains the prepared, unexecuted SQL files. A fresh preflight remains mandatory immediately before any separately approved data change.
- **Implementation/deployment readiness at the time of this draft:** the local test host was blocked. Section 10 records the later owner-provided SQL-free CI result. Guard deployment remains blocked until UserId 82 is verified Admin-only and the required deployment approval is granted.
- **Approval status:** **WAITING FOR EXPLICIT OWNER APPROVAL**. Selection of Option A is policy direction, not permission to execute SQL.

## 9. Final status (superseded by the 2026-10-22 update below)

- **Conclusion: BLOCKED – current-source single-role login and corrected Identity role-store runtime checks have not been verified; Windows Application Control blocked the unsigned test assembly. The strict guard also cannot be deployed while UserId 82 remains the only active Admin and has Admin + Customer.**
- **SECURITY POC VERIFIED (bounded):** prior invitation/session/role-transition evidence and the earlier 33-test isolated guard run are listed by exact test above. This does not certify the latest source tree.
- **Windows Application Control:** enforced Code Integrity event 3077 blocked loading `NEXORA.Tests.dll` under `VerifiedAndReputableDesktop`; no policy bypass or security-setting change was attempted. Administrator/security-owner action through approved signing/allowlisting or sanctioned CI is required.
- **Permitted runner:** none found locally. The owner must supply access to a sanctioned CI/test runner or have the administrator/security owner approve the complete custom assembly set/publisher through the established policy process. Approving only `NEXORA.Tests.dll` may be insufficient because the referenced NEXORA assemblies are unsigned too. This approval must permit execution without weakening or bypassing endpoint policy.
- **Current-source login matrix:** Admin, Customer, Sales, Warehouse positive HTTP login; zero-role; inactive-user login; and runtime role-claim-store compatibility are BLOCKED/not executed. Multi-role denial and cookie guard outcomes only have evidence from the earlier isolated run.
- **Admin lockout risk:** high/blocking. Read-only QA preflight showed UserId 82 is the only active account carrying Admin and has both Admin + Customer; there are zero active Admin-only accounts. Strict-guard deployment against this state would deny the only Admin login.
- **MANUAL UAT NOT RUN:** the 20-case browser checklist remains NOT RUN; no screenshot or browser result is claimed.
- **REAL EMAIL UAT BLOCKED/PENDING:** no mailbox/provider was confirmed, and no email was sent.
- **QA remediation readiness:** `dbo.user_roles` is the actual mapping table. The intended single delete is `user_id=82, role_id=1` (Customer), expected affected rows **1**, retaining active `role_id=4` Admin. The read-only preflight recorded in the prior remediation turn was not repeated today; immediately before any separately approved data change, require an active user, exact Admin + Customer mappings with IDs verified by canonical role names, no extra mappings, four canonical roles, six users, and seven mappings. Require the post-state to remain active with exactly Admin, the active Admin count to remain one, and the global mapping count to be six; rollback on any mismatch. After commit uncertainty, read current state before any retry; never blindly reinsert Customer. Owner must separately approve this exact QA-only data change after final script review.
- **QA data:** UserId 82 and every mapping remain unchanged; no SQL write or SQL integration test ran in this turn. The prior read-only preflight is not a substitute for a fresh preflight before any approved change. The main `NEXORA` database was not accessed.
- **Schema:** unchanged; no migration was created or applied.
- **Repository:** no stage, commit, push, cleanup, or reset. Existing `docs/PHASE4_SCHEMA_REVIEW.sql` and all pre-existing working-tree changes remain preserved.
- **Phase 6.1:** not marked COMPLETED; current-source security runtime tests remain blocked. Phase 6.2–6.4 code work is covered by separate current acceptance records and does not resolve this account remediation blocker.

Safe deployment order (plan only): (1) retain the owner-provided CI evidence against commit `d8331a7` and review the exact SQL files in Section 10; (2) obtain separate explicit owner approval for Change `NEXORA-P61-QA-ROLE82`; (3) repeat guarded read-only preflight on `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`; (4) only in a separately authorized turn, run the approved one-row transaction and read-only postcheck; (5) verify Admin-only login and stale-cookie rejection; (6) deploy the fail-closed guard only after the account state and deployment plan are verified; (7) perform Manual UAT and report. None of steps 2–7 were executed in this turn.

## 10. Owner-provided CI update and SQL review package — 2026-10-22

This section records the latest evidence and supersedes older local test-host status above where they conflict. It does not represent a database recheck or an execution approval.

- **CI evidence:** the owner reports GitHub Actions commit `d8331a7` completed with **168 PASS, 0 FAIL, 0 SKIPPED**. SQL integration tests were **NOT RUN**. The prior local Windows Code Integrity test-host block is therefore resolved for the SQL-free CI run, based on the owner's supplied result. No TRX was independently available in this review, so this document does not claim individual test names or independently verified CI artifacts.
- **QA state:** the latest owner-supplied preflight baseline remains 6 users, 7 mappings, four canonical roles; UserId 82 is active with Admin + Customer, is the only active Admin, and no active Admin-only account exists. This turn performed **no SQL connection or recheck**. UserId 82 remains unchanged to the extent evidenced by that prior read-only preflight; a fresh preflight is mandatory before any separately approved execution.
- **SQL integration:** **NOT RUN**. No SQL was executed, no database was connected to, and no SQL dry-run/rollback was attempted in this turn.
- **Manual UAT:** **NOT RUN**. No Phase 6 website was started and no browser result is claimed.
- **Schema:** no schema or migration change was made or proposed by these scripts.
- **Change approval:** **WAITING FOR EXPLICIT OWNER APPROVAL**. The current request authorizes preparation and static review only, not execution.

### Prepared SQL files

- `docs/PHASE6_1_QA_ROLE82_REMEDIATION.sql` is an unexecuted, guarded transaction script. It is fail-closed for any server/database other than the exact QA target, checks ONLINE state, required EF migration history and mapped Identity schema, resolves role IDs from canonical names, validates the 6-user/4-role/7-mapping baseline, and permits only one delete from `dbo.user_roles` for UserId 82's resolved Customer role. It checks exactly one affected row and all postconditions before commit. The approval variable defaults to `0`.
- `docs/PHASE6_1_QA_ROLE82_POSTCHECK.sql` is an unexecuted read-only postcheck. It verifies the exact QA target and the expected 6-user/4-role/6-mapping state, active UserId 82 with Admin only, one active Admin, one active Admin-only account, and no remaining multi-role users. It outputs counts/status only and selects no credentials or personal profile fields.

### Application-lock compatibility

Static source review found `IdentityAccountAdministrationService` uses `NEXORA:ACCOUNT:POLICY`, Exclusive, Transaction-owned, with a 15-second timeout and the current database's default `public` principal. The remediation script explicitly specifies `@DbPrincipal = N'public'`, matching that principal. Application-lock names are exact keys, not hierarchical names: `NEXORA`, `NEXORA:ACCOUNT`, and `NEXORA:ACCOUNT:POLICY` do not implicitly lock one another. The script retains the separately requested `NEXORA:ACCOUNT` coordination key and also takes the service's exact `NEXORA:ACCOUNT:POLICY` key, in that order. The service takes only the policy key, so source review found no reverse acquisition order in that service. Any future participant taking both must use this order. There is no reason to take a `NEXORA` root lock because the service does not use it and it would not replace either exact key. Negative `sp_getapplock` return values, including timeout and deadlock-victim results, throw into the rollback handler; the SQL script does not retry. SQL Server errors such as deadlock 1205 also enter that rollback path. The service's EF execution strategy may retry its own application transaction; that is separate from this one-off SQL script and is not a reason to retry an ambiguous manual commit. No production Identity service was changed.

### Static review limits and execution readiness

- The scripts use the EF-mapped tables/columns confirmed from `AppDbContext` and the InitialFoundation migration: `dbo.users(id,is_active)`, `dbo.roles(id,name,normalized_name)`, and `dbo.user_roles(user_id,role_id)` with composite PK and the two expected FKs. Role IDs 4 and 1 are drift checks only; actual IDs are resolved by exact canonical names `Admin` and `Customer`.
- Both scripts explicitly reject `NULL` for `SERVERPROPERTY('ServerName')`, `DB_NAME()`, and `DATABASEPROPERTYEX(...,'Status')`. Server/database checks require matching `DATALENGTH` and a `Latin1_General_100_BIN2` exact-case comparison, avoiding SQL Server's trailing-space equality behavior; ONLINE status also checks its length and binary value. The exact target remains `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; no `USE` or fallback database is present.
- Every canonical `Admin`, `Customer`, `Sales`, and `Warehouse` role-name and normalized-name comparison in both files checks `DATALENGTH` as well as `Latin1_General_100_BIN2`. A trailing space on either stored value cannot pass the canonical predicate. No role ID or deletion predicate changed.
- `SET LOCK_TIMEOUT 15000` is set in both scripts. Remediation also retains explicit 15,000 ms `sp_getapplock` timeouts for both application-lock resources. Application-lock waits use the procedure parameter; table/data lock waits use the session setting. A negative application-lock result throws; SQL lock timeout 1222 inside the remediation transaction enters `CATCH`, which rolls back whenever `XACT_STATE() <> 0`. The postcheck uses the same bounded lock wait and rolls back its read transaction on a caught error. Use a dedicated SQL session because `LOCK_TIMEOUT` is session-scoped.
- In the remediation script, metadata and migration-history checks run inside the SERIALIZABLE transaction. It takes transaction-duration table locks on Identity and migration-history tables before validating their mapped schema. It requires an operator-confirmed schema-change freeze (`@SchemaChangeFreezeConfirmed = 1`, default `0`) and repeats the database table-count/migration-count guard immediately before the only DML. Those table locks protect the affected Identity/history objects; arbitrary concurrent DDL elsewhere in the database is not coordinated by this script, so the freeze is a required execution-window precondition that SQL itself cannot prove.
- Static source review of InitialFoundation found the two `user_roles` foreign keys to `users` and `roles` use CASCADE, and no migration-created DML trigger on `user_roles`. These are child-to-parent references; deleting a child mapping does not invoke a parent-delete cascade. The remediation script now fails closed unless live metadata shows exactly those two enabled, trusted, single-column outgoing FKs, no incoming FK, and no trigger on `user_roles`. Live metadata has not been queried in this turn; these checks require a separately approved execution window.
- The remediation scope contains exactly one DML statement: a `DELETE` against `dbo.user_roles` constrained by `user_id = 82` and the dynamically resolved Customer role ID. It does not modify `dbo.users`, the Admin mapping, any other role mapping, or commerce/history tables. Any failed guard before commit enters `CATCH` and rolls back.
- The postcheck is read-only and should run from a newly opened SQL connection after the remediation attempt. `DatabasePostcheckPassed = 1` means only that database state matches 6 users, 4 roles, 6 mappings, active Admin-only UserId 82, one active Admin, one active Admin-only account, and no multi-role users. It is not evidence that Admin login succeeds or that cookies were revoked.
- Cookie/session boundary: mixed-role and mismatched-role cookies are rejected by the current validator on the next authenticated request. A previously issued Admin-only cookie may remain valid because this SQL change does not rotate SecurityStamp. Blanket invalidation requires a separately approved and tested Identity session-revocation action; no SecurityStamp update is included here.
- If the client disconnects while `COMMIT` is in flight, the outcome is ambiguous. Do not retry the delete and do not reinsert Customer. Reconnect only to the exact QA target, run the separate postcheck, compare actual state, and request owner direction for any recovery.
- `TRY/CATCH` does not catch every same-level error, including some compile-time and name-resolution errors. A static parse would not prove live object metadata or runtime behavior. If execution stops unexpectedly, do not assume `CATCH` ran: stop using that session. If it remains connected, issue no further commands in it; close/terminate the session so SQL Server can roll back any open transaction. Then open a separate connection to the exact QA target and run only the read-only postcheck. If termination occurred during `COMMIT`, treat the outcome as unknown; never retry `DELETE` or automatically reinsert Customer.
- No SQL Server offline parser was available in this review. `sqlcmd` is installed but requires a SQL connection to submit a batch, so it was not used. T-SQL was reviewed statically only; no parse by SQL Server or execution was attempted.
- Static inspection and repository-source comparison were performed; neither script was parsed or executed by SQL Server. Live server-name behavior, live schema/trigger/FK compatibility, lock acquisition, transaction behavior, commit result, and postcheck result remain **UNVERIFIED until a separately approved execution window**. SQL syntax was reviewed statically, not runtime-certified.
- **Conclusion: APPROVABLE FOR OWNER REVIEW ONLY; NOT APPROVED OR READY FOR EXECUTION.** Separate explicit owner execution approval, fresh read-only preflight, operator confirmation of the schema-change freeze, review of these exact file contents, and an approved execution window are still required. SQL integration tests remain NOT RUN, and Manual UAT remains NOT RUN. Phase 6 is not marked COMPLETED.
