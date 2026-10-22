# NEXORA – Phase 6.1 Account Administration Acceptance

**Current acceptance status:** `BLOCKED – CURRENT-SOURCE SECURITY RUNTIME TESTS NOT VERIFIED`<br>
**Historical implementation status:** `PHASE 6.1 IMPLEMENTED – READY FOR OWNER UAT` at the earlier baseline recorded below.<br>
**Review date:** 2026-10-22<br>
**Final owner acceptance:** NOT GIVEN<br>
**Production deployment:** NOT STARTED

## Scope

Phase 6.1 implements Admin account lookup and management, staff invitations for Sales/Warehouse, staff activation, lock/unlock, Sales↔Warehouse role changes, and authoritative session-state checks. It follows the approved Phase 6.0 policy: the Admin UI cannot grant Admin, Customer cannot be converted to staff, and the application keeps one Identity authentication system and the four existing roles.

## Delivered behavior

- Admin-only `/Admin/Users` search, role/status filter, bounded paging, details, staff creation, lock/unlock, role change and invitation resend.
- Admin UI offers only Sales and Warehouse. A staff invitation creates an inactive account with exactly one staff role and no password; the invited user sets their own password before activation.
- Invitation tokens are purpose-specific, expire after 24 hours, and are invalidated by resend/security-stamp change or activation. The token and email are placed in the link fragment so they are not transmitted in the HTTP request URL; the browser validates them through an antiforgery-protected POST. The activation form's final POST is also antiforgery-protected.
- Development uses the in-memory mailbox. It is only available to an Admin on loopback in Development. Production uses `UnconfiguredEmailSender` until an approved email provider is configured; no email was sent to a real recipient in this work.
- Lock/unlock and role transitions rotate the security stamp. Every authenticated request checks persisted `IsActive`, security stamp and current role claims; account-state lookup failures reject the cookie.
- SQL mutations run as one execution-strategy transaction using the same scoped `AppDbContext` as `UserManager` and `RoleManager`. A transaction-owned SQL application lock serializes role/account policy operations, duplicate staff creation and last-active-Admin checks. Email dispatch occurs only after commit.
- Logging uses structured `ILogger` events containing actor/target IDs, action, role and outcome. Email addresses, passwords, tokens, hashes and security stamps are excluded.

## Automated verification

The results in this table are historical and predate the later fail-closed single-role guard remediation. They do not verify the current source. See [`PHASE6_1_MULTIROLE_REMEDIATION.md`](./PHASE6_1_MULTIROLE_REMEDIATION.md) for the current blocker and required owner approvals.

| Check | Actual result |
|---|---|
| `dotnet restore NEXORA.sln` | PASS; all projects up to date |
| `dotnet build NEXORA.sln --no-restore` | PASS; 0 warnings, 0 errors; output isolated under `tests/NEXORA.Tests/bin/Phase61` because the running website held the normal Web output DLLs |
| `dotnet test NEXORA.sln --no-build --no-restore` | **240 passed, 0 failed, 0 skipped** |
| Test breakdown from `Phase61Final.trx` | 178 unit/domain/controller; 6 HTTP test-host; 56 SQL Server integration |
| `node --check` | PASS for `wwwroot/js/staff-activation.js` |
| `git diff --check` | PASS; Git emitted only existing LF/CRLF normalization warnings |
| EF model/snapshot | `dotnet ef migrations has-pending-model-changes` reports no pending model changes |

The SQL test opt-in was enabled only after the dedicated target was checked. Read-only SQL preflight/postflight confirmed `PC01\SQLEXPRESS / NEXORA_IntegrationTests`, ONLINE; 27 physical tables (22 business, 4 Identity, `__EFMigrationsHistory`); the three existing migration IDs; and the four standard roles. The database had 6 users before and after the tests. No `qa.phase61.*` fixtures remained. One pre-existing multi-role account anomaly remained unchanged. These checks did not access the main `NEXORA` database.

SQL integration scenarios include transaction rollback on Identity role-assignment failure, duplicate staff creation race, activation/replay/expiry/resend invalidation, concurrent redemption, delivery failure, pending versus suspended state, Customer-promotion rejection, role swap with stale-cookie rejection, lock rejection at the next request, last-active-Admin protection, and preventing unlock of a multi-role account. No migration was created or applied.

## Manual UAT and production-email status

Browser/manual UAT has **not been performed by Codex and has not yet been confirmed by the owner**. The 20-case checklist is at [`PHASE6_1_MANUAL_UAT.md`](./PHASE6_1_MANUAL_UAT.md); every case starts as NOT RUN. No screenshots or manual PASS results are claimed.

Real mailbox delivery is not testable yet: no production email provider, approved recipient, or mailbox credentials were configured or supplied. Development-mailbox testing can validate the invitation flow in QA without sending real email, but does not prove external delivery. The existing multi-role anomaly must not be edited as part of UAT.

## Known limits and follow-up risks

1. **Email delivery:** production sender is intentionally unconfigured; configure it through an approved secret channel and perform owner-authorized delivery UAT before final acceptance.
2. **Commit ambiguity fault injection:** code verifies persisted state after an uncertain commit outcome, but no SQL network-fault injection test at the exact commit boundary was run.
3. **Per-request database checks:** authoritative session validation adds account/role reads on authenticated requests; performance has not been measured at production load.
4. **Existing inconsistent roles:** one pre-existing multi-role account was only counted in read-only QA queries; it was neither repaired nor used as a test fixture.
5. **Audit durability:** account events are operational structured logs, not a persisted audit-history table.
6. **Admin provisioning:** creating or recovering Admin remains outside this Admin UI and uses the separately controlled provisioning process.
7. **Later Phase 6 work:** Favorites/Reviews, moderation, reporting and Phase 6 cross-feature UAT remain NOT STARTED.

## Schema and repository safety

- No entity/schema/migration change; 22 official business tables remain unchanged.
- No database update, new database, or main `NEXORA` connection was used.
- No commit or push was performed.
- Existing unrelated Phase 6.0 documentation edits and untracked `docs/PHASE4_SCHEMA_REVIEW.sql` were preserved.

## Conclusion

Implementation and automated Development/QA regression are ready for owner review. Phase 6.1 is **not finally accepted or completed** until the owner finishes the manual UAT checklist and decides the email-delivery acceptance path. Production remains NOT DEPLOYED.
