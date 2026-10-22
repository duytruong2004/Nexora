# NEXORA – Phase 6.5 Final Hardening Plan

**Status:** PREPARED – REGRESSION/UAT BLOCKED; NOT ACCEPTED.
**Preparation date:** 2026-10-22.

Phase 6.5 is a verification and handoff phase, not a license to add unapproved features. It cannot be accepted from compilation alone.

## Current blockers

1. Current-source test execution is blocked by Windows Application Control / Code Integrity. The unsigned `NEXORA.Tests.dll` was blocked with `0x800711C7` (Code Integrity event 3077). No approved CI runner, isolated approved test machine, or administrator-authorized signing/allowlisting workflow is available in this task. Do not disable or bypass policy. Obtain an approved runner or have the administrator authorize the build/test artifact through the existing policy process.
2. SQL integration tests require a separately verified exact QA target `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`, with fixture writes/cleanup confined to test-owned markers. No SQL connection or query was made during this implementation turn. SQL tests remain unverified.
3. Browser/manual UAT for Phase 6.2, 6.3, and 6.4 has not been performed. Use the blank checklists in the corresponding `PHASE6_*_MANUAL_UAT.md` files and record only observed outcomes.
4. Phase 6.1 remains separately blocked: current-source identity guard runtime tests are not verified; UserId 82's multi-role QA mapping remains untouched and its correction requires a separate owner approval; production email delivery has not been configured or tested.

## Required execution order

1. Preserve the current uncommitted working tree and record the exact source revision/hash and `git diff` before moving to an approved runner.
2. On an approved Windows runner, restore/build/test the exact current source, including uncommitted Phase 6.1 changes. Save a new TRX and report unit, HTTP, and SQL counts separately. Do not reuse historic 240-test results.
3. Before any SQL tests, enforce target guard and read-only preflight for server, database, ONLINE state, schema, and migration history. Stop on mismatch. Do not connect to the main `NEXORA` database. Use only isolated QA fixtures; retain unrelated QA rows.
4. Run the Phase 6.1 single-role/cookie/invitation regressions as well as Phase 6.2 favorite/review, Phase 6.3 moderation, Phase 6.4 report/dashboard, and Phase 1–5 regression suites.
5. Perform manual UAT using `PHASE6_1_MANUAL_UAT.md`, `PHASE6_2_MANUAL_UAT.md`, `PHASE6_3_MANUAL_UAT.md`, and `PHASE6_4_MANUAL_UAT.md`. Record tester, account role, date, viewport, result, and genuine evidence path.
6. Review authorization, IDOR, CSRF, XSS/output encoding, validation, concurrency, exception handling, structured logs, responsive layout, empty/error states, route navigation, and accessibility against the real UI.
7. Measure reporting queries on an approved QA dataset and document query count/latency/plan. Current code/build does not prove EF SQL translation or performance.
8. Review EF model/migration consistency without applying migrations. Preserve the 22 business-table policy. Any model drift or index/schema proposal requires a separate review and approval.
9. Review FR45 price-change audit coverage against NFR10. Current design has no persistent price history; do not add it. If operational structured logging is proposed, obtain scope approval before implementation.
10. Update traceability and acceptance records with actual outcomes; obtain owner sign-off for Development/QA. Production remains a separate database/deployment approval.

## Not claimed

- No current-source automated test count or TRX is available.
- No SQL integration result, query plan, or database state was checked in this turn.
- No browser automation or manual UAT was performed in this turn.
- No Phase 6.1 remediation, QA account change, email send, schema change, migration, commit, or push occurred.

**Exit gate:** all required current-source tests run in an approved environment with no unexplained skips; SQL checks use only the exact QA database; manual UAT records real evidence; Phase 6.1 blocker has an owner-approved safe path; FR/UC traceability and known limitations are current; owner signs off Development/QA. This plan does not mark Phase 6 or the project completed.
