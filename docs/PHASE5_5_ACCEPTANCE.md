# NEXORA – Phase 5.5 Acceptance

**Date:** 2026-10-21 (final review update)
**Phase 5 Development/QA status:** **COMPLETED**.
**Owner sign-off:** **APPROVED** for Development/QA scope (2026-10-21).
**Main NEXORA database deployment:** **NOT COMPLETED**. The Phase 5.2 migration on the main database remains subject to separate approval.
**Git:** `master`, HEAD `b53478c` (`feat: complete phase 4 shopping checkout and customer orders`). Phase 5 changes remain uncommitted; no commit or push was made.

## A. Repository preflight

- Repository is `D:\Projects\NEXORA`, branch `master`, HEAD `b53478c`.
- Working tree already contained Phase 5.1–5.4 changes, plus the pre-existing untracked `docs/PHASE4_SCHEMA_REVIEW.sql`. These files were preserved.
- Phase 5.5 added a QA-only provisioning command, safety tests, and acceptance/UAT documentation; none was staged or committed.
- No reset, clean, branch deletion, or unrelated-file overwrite was performed.

## B. Database QA preflight and postflight

Read-only SQL checks were executed only against `PC01\SQLEXPRESS / NEXORA_IntegrationTests`.

| Check | Result |
|---|---|
| Actual SQL Server | `PC01\SQLEXPRESS` |
| Database | `NEXORA_IntegrationTests`, `ONLINE` |
| Migration history | Exactly `20261015173046_InitialFoundation`, `20261016133620_AddCheckoutRequestId`, `20261019174632_AddOrderCancellationRequestFields` |
| Physical schema | 27 physical tables: 22 business, four Identity support, one migration history |
| Required Phase 5 fields | `orders.cancel_request_status`, `orders.completed_at`, `inventories.row_version` present |
| Counts at the earlier Phase 5.5 preparation preflight (2026-10-20) | 12 products, 12 inventories, 2 existing orders, 3 existing user rows; test-only checkout users/products left behind: 0. Sales/Warehouse QA accounts were provisioned afterward by the owner. |
| Existing QA inventory test fixture | `NEXORA-QA-OOS-20261019` remains; it was present before this task and was not modified or deleted |
| Database NEXORA | Not connected to or changed during this task. `appsettings.Development.json` was read and shown to default to NEXORA; runtime and tests explicitly overrode it to QA. |

No migration was created or applied. No schema change was made. The QA database was not reset.

## C. QA account provisioning

The QA database currently has one Sales and one Warehouse user-role assignment. Read-only role counts before and after the final regression remained unchanged. The owner-confirmed manual UAT demonstrates use of the QA role accounts; this report does not record or request their passwords. Codex did not inspect password hashes or change existing users' roles.

Added the internal command `--provision-qa-staff`. It is restricted to Development/QA, requires exact QA target plus read-only database/schema/migration/role checks, accepts only a new `qa.*@nexora.test` account and Sales/Warehouse role, uses Identity's existing password policy, hides password input, and refuses to modify duplicates. It skips role seeding in provisioning mode and is not activated at web startup.

The guarded local provisioning command and instructions remain documented at [QA_ACCOUNT_PROVISIONING.md](./QA_ACCOUNT_PROVISIONING.md). Regression tests create only random `qa.provisioning.*@nexora.test` fixtures and clean up those generated fixtures; they do not target the persistent QA Sales/Warehouse accounts. Those accounts were retained.

## D. Website QA and browser during Phase 5.5 preparation (historical)

- Website is running and was kept alive at `http://127.0.0.1:5059`.
- Process connection string was explicitly set to `PC01\SQLEXPRESS / NEXORA_IntegrationTests`. Startup log confirmed the Development catalog/consultation/inventory demo seeders were skipped because the target was not the approved Development database.
- A read-only SQL session check showed the website's SqlClient connection in the QA database context.
- HTTP smoke: Home, Catalog, Login, Register, `site.css`, and Bootstrap CSS returned HTTP 200. Anonymous Customer Orders, Sales Orders, Admin Orders, Warehouse Orders/Inventory/History returned a login redirect (302).
- Browser automation ran in the **ChatGPT in-app browser**, not the user's Chrome/Edge profile. Home, Catalog, Product Detail, Consultation rendered. Anonymous access to `/Warehouse/Inventory` redirected to Login. No authenticated workflow was submitted.
- The Login page displayed browser autofill. Codex did not use, copy, or submit those values and navigated away; owner confirmation is pending before that identity could be considered for QA login.
- No Chrome/Edge desktop browser was exposed in the computer inventory. No custom viewport override was available. Desktop/tablet/mobile responsive testing remains NOT RUN.
- No browser screenshot was saved; this report does not fabricate screenshot evidence.

## E. Automated test results recorded before final review (2026-10-20)

Tests were run after setting both `ConnectionStrings__DefaultConnection` and `NEXORA_SQLSERVER_TEST_CONNECTION` process-scoped to the exact QA target. The Checkout SQL concurrency fact requires explicit opt-in; after checking the approved QA migration baseline, `NEXORA_CHECKOUT_SQL_TESTS=1` was set for the final run.

| Category | Actual result |
|---|---|
| Restore | `dotnet restore NEXORA.sln` succeeded; projects were up to date |
| Build | `dotnet build NEXORA.sln --no-restore` succeeded, 0 warnings, 0 errors |
| Full automated suite | **206 passed, 0 failed, 0 skipped** |
| SQL integration | Used only `PC01\SQLEXPRESS / NEXORA_IntegrationTests`; fixture counts were read afterward and no Phase 5 test fixture remained |
| HTTP integration | Included in the full test suite; test-host evidence is not browser UAT |
| `git diff --check` | PASS; only normal Git LF→CRLF notices were printed |
| Model/migration consistency | `dotnet ef migrations has-pending-model-changes` reported no model changes; no entity, persistence mapping, migration, or snapshot files were changed by Phase 5.5 |

These are the results from the earlier Phase 5.5 preparation run. The final review reran the full suite on the same isolated QA target; its superseding results are in section J below.

## F. Manual UAT state before owner confirmation (historical)

| Category | State |
|---|---|
| Anonymous browser smoke | PARTIAL PASS: browser rendered public pages and redirected one protected route to Login |
| Authenticated Customer workflows | WAITING FOR QA account selection/login and manual UAT |
| Authenticated Sales workflows | WAITING for local creation of QA Sales account and browser UAT |
| Authenticated Warehouse workflows | WAITING for local creation of QA Warehouse account and browser UAT |
| Authenticated Admin workflows | WAITING for owner to use an accepted QA Admin identity and browser UAT |
| Responsive desktop/tablet/mobile | NOT RUN; owner Chrome/Edge DevTools needed |
| Manual UAT confirmation | NOT RECEIVED |

The later owner-confirmed summary is recorded in section K. Only those nine summary items are marked PASS; detailed case rows in [PHASE5_5_UAT_PLAN.md](./PHASE5_5_UAT_PLAN.md) are not promoted to PASS without separate case-level evidence.

## G. Known limitations and risks

- Only the nine owner-confirmed summary items in section K are recorded as PASS. Detailed steps, edge cases and viewport-specific checks in the UAT plan remain unconfirmed unless separately reported. No screenshot was attached for the final UAT.
- The current Development settings default to the main `NEXORA` DB. Future QA runs must retain the explicit QA process override. The runtime seeder guard added in this task prevents QA demo seeding, but the operator must still verify the connection string and instance before start.
- `inventory_transactions` has no unique operation ID schema column; existing application lock/idempotency behavior is not an absolute exactly-once guarantee against direct SQL or bypassing application locks.
- Warehouse preparation has no separate actor/time audit fields; the UI and report must not invent this history.
- The existing QA out-of-stock fixture was not created in this task and was intentionally retained.

## H. Changed files in this Phase 5.5 turn

- `src/NEXORA.Web/Identity/DevelopmentAdminProvisioningCommand.cs` – recognize the new local QA staff provisioning switch.
- `src/NEXORA.Web/Identity/QaStaffProvisioningCommand.cs` – add guarded, interactive Sales/Warehouse-only provisioning.
- `src/NEXORA.Web/Program.cs` – skip QA role seeding for provisioning mode and prevent Development demo seeders from touching the QA database.
- `tests/NEXORA.Tests/AdminProvisioningCommandTests.cs` – add argument, DB target, and QA email guard tests.
- `docs/QA_ACCOUNT_PROVISIONING.md`
- `docs/PHASE5_5_UAT_PLAN.md`
- `docs/PHASE5_5_ACCEPTANCE.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/REQUIREMENTS_TRACEABILITY.md`

## I. Acceptance conclusion before final owner UAT (historical)

This was the conclusion as of 2026-10-20 and is superseded by sections J–L. The final review records the owner-confirmed UAT and regression results without claiming that Codex performed authenticated browser automation.

## J. Final regression, model and QA database review (2026-10-21)

| Check | Result |
|---|---|
| `dotnet restore NEXORA.sln` | PASS |
| Fresh solution build | PASS, 0 warnings and 0 errors, using isolated output `D:\NEXORA-Phase5-FinalQA-20261021` so the currently running `NEXORA.Web` process did not need to be stopped |
| `dotnet test NEXORA.sln` | **209 passed, 0 failed, 0 skipped**, from the freshly built isolated output; SQL tests used only `PC01\SQLEXPRESS / NEXORA_IntegrationTests` and the Checkout SQL opt-in was enabled after QA preflight |
| EF model/migration consistency | PASS: `dotnet ef migrations has-pending-model-changes` reported no model changes since the last migration |
| QA database postflight | PASS: ONLINE; 27 physical tables = 22 business tables, four Identity support tables (`AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`) and `__EFMigrationsHistory`; the same three expected migration IDs; QA Sales/Warehouse role assignment counts remained 1 each; Phase 5 marked test products/orders were 0 |
| Migration/schema actions in this review | None. No database update, migration creation, or DDL was run. The main `NEXORA` database was not accessed. |
| `git diff --check` | PASS after the final document/source edits; no staging, commit, or push was performed. |

The first test invocation against isolated output exposed three HTTP test-host failures because the helpers could not locate `src/NEXORA.Web`; setting the already-supported `NEXORA_REPOSITORY_ROOT` to this repository and rerunning the same fresh binary produced the 209/209 result above. A default-output build was also attempted but could not overwrite the `NEXORA.Web.dll` held by the running site; the isolated fresh build succeeded. These environment/worktree issues did not require source or schema changes.

As part of the secret scan, the SQL provisioning test fixture's fixed test-only password literal was replaced with a per-run generated fixture password. It is not written to logs; the regression suite was rebuilt and rerun afterward. No persistent QA account was created, changed or deleted by that test run.

### Seven documented business decision groups reviewed

1. The system retains exactly four roles: Customer, Sales, Warehouse and Admin; server endpoints enforce the role boundary.
2. Order status retains the seven persisted values `PENDING`, `CONFIRMED`, `PREPARING`, `EXPORTED`, `SHIPPING`, `COMPLETED` and `CANCELLED`; no cancellation-request status was added to `orders.status`.
3. The order transition matrix is compare-and-swap guarded: Sales/Admin may confirm, Sales hands off to Warehouse, Warehouse exports, and Sales advances shipping/completion; Admin remains read-only for warehouse mutations and cannot hand off/export/advance fulfillment.
4. The owner Customer may directly cancel only their own `PENDING` order. After confirmation, one separate cancellation request is allowed in `CONFIRMED`/`PREPARING`; Sales/Admin approve or reject it. An exported order is outside this cancellation path.
5. A pending cancellation request blocks export. Approval cancels the order and releases its reservation once; rejection leaves order status and inventory/reservation unchanged. Request, review and export compete through conditional updates/transactions.
6. Inventory timing is consistent: Checkout reserves and writes `RESERVE`; confirmation/handoff do not reserve again; cancellation releases and writes `RELEASE`; successful Warehouse export decreases physical and reserved quantity and writes `OUT` atomically. Warehouse receipt `IN` and adjustment `ADJUST` preserve the no-negative/no-below-reserved invariants; Admin has read-only inventory access.
7. `IN`/`ADJUST` replay protection is bounded to application paths using the stable operation marker and transaction-owned `sp_getapplock`. There is no schema-level unique operation ID, so this is not an unconditional exactly-once guarantee against direct SQL/bypass paths or an unverifiable commit outcome.

### Order state machine reviewed

| From | Action / actor | To | Inventory effect |
|---|---|---|---|
| `PENDING` | Confirm / Sales or Admin | `CONFIRMED` | None |
| `PENDING` | Direct cancel / owning Customer | `CANCELLED` | One `RELEASE`; physical stock unchanged |
| `CONFIRMED` | Handoff / Sales | `PREPARING` | None |
| `CONFIRMED` or `PREPARING` | Approved cancellation request / Sales or Admin | `CANCELLED` | One `RELEASE`; physical stock unchanged |
| `PREPARING` | Export / Warehouse, if cancellation request is NULL/REJECTED | `EXPORTED` | Decrease physical and reserved quantity; one `OUT` per product, in the same transaction |
| `EXPORTED` | Mark shipping / Sales | `SHIPPING` | None |
| `SHIPPING` | Complete / Sales | `COMPLETED` | None; set `completed_at` |

A cancellation request itself does not change the order state or reservation. `PENDING` cancellation requests block export; no cancellation is accepted through this flow after `EXPORTED`.

## K. Owner-confirmed Manual UAT summary (2026-10-21)

The project owner reported that they personally tested and confirmed all nine summary items PASS on the QA website. Evidence source: the owner's explicit confirmation in this conversation. No screenshot was attached or generated for this UAT. These are manual/browser results reported by the owner, not browser automation performed by Codex.

| Result | Case | Owner-confirmed summary |
|---|---|---|
| PASS | S01 | Sales signs in and views the order list |
| PASS | S02 | Sales confirms and hands off an order |
| PASS | C01 | Customer submits a cancellation request after confirmation |
| PASS | S03 | Sales/Admin reviews a cancellation request |
| PASS | W01 | Warehouse views the queue and exports an order |
| PASS | W02 | Warehouse receives stock and adjusts inventory |
| PASS | W03 | Warehouse views transaction history |
| PASS | A01 | Admin can view inventory but cannot edit it |
| PASS | R01 | Cross-role authorization and responsive UI are checked |

**Summary: 9/9 owner-confirmed UAT items PASS.** The confirmation is at summary-case level only; this report does not mark additional detailed steps, edge cases or individual viewport measurements as passed without separate evidence.

## L. Final scope and remaining limitations

- Phase 5 is **COMPLETED for Development/QA acceptance**; the owner approved this scope on 2026-10-21. This does not mean deployment on the main `NEXORA` database is complete.
- The Phase 5.2 migration `20261019174632_AddOrderCancellationRequestFields` is present on QA only. Applying it to the main database is a separate change and awaits explicit approval and the established backup/preflight procedure.
- No order status-history table or persistent actor/time fields for warehouse preparation were added; this remains a documented audit/history limitation.
- `IN`/`ADJUST` idempotency is application-layer protection, not strict exactly-once across direct SQL or callers that bypass the applock; an unavailable database may leave a commit outcome requiring inspection.
- Returns/refunds after export, automatic expiry/cancellation of pending orders, and background jobs remain outside Phase 5.
- No account or product data was deleted as part of the final review. Existing QA Sales/Warehouse accounts were retained.
- No Git add, commit or push was performed. See repository/file review in the accompanying final response; `docs/PHASE4_SCHEMA_REVIEW.sql` remains untracked and excluded from any proposed Phase 5 checkpoint.

### Files updated during the final review

- `docs/PHASE5_5_ACCEPTANCE.md`
- `docs/PHASE5_DESIGN_DRAFT.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/REQUIREMENTS_TRACEABILITY.md`
- `docs/PHASE5_4_ACCEPTANCE.md` (removed trailing whitespace found in the untracked file)
- `tests/NEXORA.Tests/QaStaffProvisioningSqlServerIntegrationTests.cs` (replaced a fixed test-only password with a per-run generated fixture password)

No application business logic, EF mapping, migration, or database schema was changed by this final review.
