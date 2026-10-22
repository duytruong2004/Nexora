# NEXORA – Phase 6.4 Acceptance Record

**Status:** IMPLEMENTED – BUILD VERIFIED; SQL/HTTP TESTS NOT RUN; MANUAL UAT NOT RUN.
**Date:** 2026-10-22
**Owner acceptance:** NOT PROVIDED.

## Scope and implementation

The Admin dashboard/reporting code addresses FR46–FR50 with server-side query services and read-only Admin GET endpoints:

- Dashboard: database-backed catalog and consultation counts, order counts by status, and products missing inventory; no invented metrics.
- Orders report: groups by `Order.OrderDate` and status.
- Revenue report: COMPLETED orders only, filtered by `CompletedAt`, sums each stored `Order.TotalAmount` once, and groups by Vietnam local date.
- Product sales: COMPLETED orders only, filtered by `CompletedAt`, sums `OrderDetail.Quantity` by product and uses line snapshot name.
- Inventory report: `QuantityInStock - ReservedQuantity`; distinguishes zero, low, normal, missing inventory, and anomalous stock values; threshold is request input and is not persisted; Admin cannot adjust stock through this report.
- Date filters are mapped from `Asia/Ho_Chi_Minh` local dates to UTC half-open intervals `[startUtc, endExclusiveUtc)`.
- Reports are paged where rows can grow; the reporting service performs filtering/grouping/aggregation through EF queries rather than loading complete source tables for client-side totals.

## Evidence for this source revision

- `dotnet restore NEXORA.sln`: PASS.
- Fresh isolated `dotnet build NEXORA.sln --no-restore`: PASS, 0 warnings, 0 errors.
- EF model/snapshot: Release `dotnet ef migrations has-pending-model-changes` reports no pending changes; design-time connection was set to an unreachable loopback endpoint and no SQL database was accessed.
- Test code compiled but `dotnet test` was **NOT RUN** because Windows Code Integrity previously blocked the unsigned test assembly (`0x800711C7`, event 3077) and no approved test runner is available. SQL query translation and fixture outputs are therefore not runtime-verified in this revision.
- Browser/manual UAT: **NOT RUN**.
- No database was connected, changed, or migrated in this turn.

## Test coverage prepared, not executed

- Unit date boundaries/range validation: `Phase6ReportingPolicyTests.ReportRange_UsesVietnamLocalMidnightsAndUtcHalfOpenBounds` and `ReportRange_RejectsReversedDatesAndUnrepresentableExclusiveEnd`.
- HTTP/admin authorization and antiforgery boundary: `Phase6EngagementHttpIntegrationTests.FavoritesReviewsModerationAndReports_EnforceRoleBoundariesAndAntiforgery`; static check `Phase6AuthorizationTests.ReviewModerationAndReports_AreAdminOnlyAndReadOnlyReportsHaveNoMutationActions`.
- SQL definitions/fixture validation: `Phase6ReportingSqlServerIntegrationTests.Reports_UseApprovedDateAndOrderSnapshotDefinitions_AndInventoryIsReadOnly`; not executed in this revision.
- Dashboard query regression: `AdminDashboardSqlServerIntegrationTests.DashboardMetrics_MatchCurrentSqlServerRows`; not executed in this revision.

## Acceptance still required

Run fresh current-source SQL integration tests only against the verified QA database `PC01\\SQLEXPRESS / NEXORA_IntegrationTests`, validate query translation and results, complete [`PHASE6_4_MANUAL_UAT.md`](./PHASE6_4_MANUAL_UAT.md), and obtain owner acceptance. No performance benchmark is claimed; query-plan/latency review remains a Phase 6.5 hardening item.
