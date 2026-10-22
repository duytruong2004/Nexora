# NEXORA – Phase 6.2 Acceptance Record

**Status:** IMPLEMENTED – BUILD VERIFIED; TESTS NOT RUN; MANUAL UAT NOT RUN.
**Date:** 2026-10-22
**Owner acceptance:** NOT PROVIDED.

## Scope

Implements Customer Favorites (FR12 / UC-C10 / BR09) and Customer Reviews (FR17 / UC-C15 / BR02) using the existing `favorites`, `reviews`, `orders`, and `order_details` model. The work does not add entities, columns, tables, or migrations.

## Implemented

- Customer favorite list, add, and remove. The current user ID is derived from the authenticated principal; duplicate add is idempotent; list/remove are scoped to that user; POST actions use antiforgery.
- Review submission is limited to an order line owned by the current customer, belonging to a COMPLETED order, matching the product/order relationship, and not already reviewed. Rating/comment validation is enforced server-side. New reviews are PENDING.
- Customers can view their own review content/status; there is no customer edit/delete action after submission.
- Public product detail displays only reviews whose stored status is the exact canonical `APPROVED` value. Unknown, PENDING, and HIDDEN values remain private. Razor output encoding is retained.
- Database uniqueness and existing foreign keys remain the persistence backstop for duplicate/race cases.

## Evidence for this source revision

- `dotnet restore NEXORA.sln`: PASS; all projects up to date.
- Fresh isolated `dotnet build NEXORA.sln --no-restore`: PASS, 0 warnings, 0 errors. The output was written under `tests/NEXORA.Tests/bin/Phase62Dev/Debug/net10.0` to use the current source without replacing a potentially locked normal output.
- EF model/snapshot: `dotnet ef migrations has-pending-model-changes` PASS in Release configuration; no pending model changes. The design-time connection string was overridden to a loopback-only, non-existent endpoint; the command did not access SQL Server.
- Test source was compiled as part of the solution build. Test execution: **NOT RUN**. Windows Code Integrity previously blocked the unsigned test assembly (`0x800711C7`, event 3077); no approved CI/test runner is available. There is no new TRX and no PASS count for this revision.
- Browser/manual UAT: **NOT RUN**.
- No database was accessed in this implementation turn. No migration or schema change was made.

## Test coverage prepared, not executed

- Unit/policy: `Phase6EngagementPolicyTests` covers review content bounds and canonical moderation transitions/unknown statuses.
- HTTP: `Phase6EngagementHttpIntegrationTests.FavoritesReviewsModerationAndReports_EnforceRoleBoundariesAndAntiforgery` exercises role boundaries, antiforgery and encoded Razor output with test-host services.
- SQL Server: `Phase6EngagementSqlServerIntegrationTests.FavoritesAndReviews_EnforceOwnershipUniquenessAndModerationVisibility` covers owner/eligibility, duplicate behavior, moderation visibility and fail-closed status handling with isolated QA fixtures. It has not run in this revision.
- Authorization source check: `Phase6AuthorizationTests.CustomerEngagementEndpoints_RequireCustomerRole`.

## Remaining acceptance work

Run the tests from this exact source on a Windows-approved runner; verify SQL tests only against `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`; then complete [`PHASE6_2_MANUAL_UAT.md`](./PHASE6_2_MANUAL_UAT.md), record actual results, and obtain owner acceptance. Do not infer acceptance from compilation.
