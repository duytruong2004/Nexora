# NEXORA – Phase 5.2 Acceptance

**Scope:** Customer cancellation request after order confirmation; Sales/Admin decision.
**Date:** 2026-10-20
**Phase status:** Phase 5.2 implemented and automated-test accepted on QA. Overall Phase 5 remains IN PROGRESS.
**Database:** PC01\SQLEXPRESS / NEXORA_IntegrationTests only.

## Delivered behavior

- Customer may submit one cancellation request for their own order in CONFIRMED or PREPARING. A caller-provided request GUID makes a retry of the same request idempotent. A second, different request is rejected.
- A PENDING request is stored separately from Order status. It does not change the order, reservation, stock, order-line snapshots, or inventory ledger.
- Sales and Admin may approve or reject a pending request. Customer and Warehouse cannot review it. HTTP POST actions require authorization and antiforgery validation.
- Approval atomically changes the order to CANCELLED, records reviewer/time/decision fields, releases each reserved line and writes RELEASE ledger entries. Rejection records reviewer/time/reason without changing order or inventory.
- Compare-and-swap permits only one of concurrent reviewers to win. A failed ledger write rolls back the approval and release.
- The Domain export policy rejects export while a cancellation request is PENDING. The actual Warehouse export flow is not implemented in this phase and no export race is claimed as tested.
- The existing direct Customer cancellation of a PENDING order remains unchanged.

## Schema and migration

Migration 20261019174632_AddOrderCancellationRequestFields was generated from AppDbContext and applied only to the QA database listed above. It adds seven nullable columns to orders, the request consistency CHECK, a nullable reviewer FK to users with NO ACTION, a unique filtered request-ID index, a filtered pending-review index, and the FK support index. No business table was added; QA has 22 business tables, four Identity tables and __EFMigrationsHistory (27 physical tables total). See [PHASE5_SCHEMA_CHANGE_REVIEW.md](./PHASE5_SCHEMA_CHANGE_REVIEW.md) and the review-only [SQL preview](./PHASE5_2_CANCELLATION_REQUEST_PREVIEW.sql).

The two pre-existing orders remain present and have NULL cancellation-request fields. Post-test read-only inspection found 0 QA fixture orders, products, or users remaining and 2 total pre-existing orders. The Development database NEXORA was not accessed or changed.

## Verification results

- Restore: dotnet restore NEXORA.sln succeeded.
- Build: solution build succeeded with 0 warnings and 0 errors, using an isolated output directory because a running website held the normal output files.
- Automated test suite: **172 passed, 0 failed, 0 skipped**. SQL Server integration tests used only NEXORA_IntegrationTests.
- Unit/domain coverage includes request eligibility and export-block policy, invalid state and role outcomes.
- HTTP integration coverage uses the ASP.NET Core test host and test-only identities to check rendered customer/staff views, antiforgery, route protection, role denials and owner scoping. This is not browser automation.
- SQL Server integration coverage includes owner isolation, request replay and duplicate rejection, request/handoff race, concurrent duplicate request, concurrent reviewers, approval release exactly once, rejection without inventory changes, later completion after rejection, transactional rollback on ledger failure, plus migration constraint/index checks.
- dotnet ef migrations has-pending-model-changes reported no model changes after the migration.
- Read-only post-test QA check confirmed the target, ONLINE state, 27 physical tables, all three expected migration IDs, two existing orders retained, and no leftover marked fixtures.
- git diff --check reported no whitespace errors; Git emitted only its configured LF-to-CRLF notices.

## Not performed / remaining limits

- No real browser session or manual UI UAT was performed. Do not treat the HTTP test host as manual browser acceptance.
- No real Sales/Warehouse employee account was used or provisioned.
- The Warehouse export action does not exist yet. The race between request/approval and a committed export, double-export protection, stock decrement and OUT ledger atomicity remain Phase 5.3 tests.
- Inventory receipt/adjustment, warehouse ledger UI, and full four-role end-to-end UAT remain later Phase 5 work.
- No application was started for this acceptance and no HTTP smoke/browser claim is made.
- No changes were made to the Development NEXORA database, no rollback was run, and no Git commit or push was performed.

## Acceptance conclusion

Phase 5.2 code, QA migration and automated checks are complete for the requested cancellation-request/review scope. Manual/browser acceptance is still outstanding. Phase 5 as a whole is not complete; proceed to Warehouse Fulfillment only as a separately scoped Phase 5.3 task.