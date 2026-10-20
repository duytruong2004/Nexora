# NEXORA – Phase 5.3 Warehouse Fulfillment Acceptance

**Date:** 2026-10-20
**Scope status:** Phase 5.3 implementation and automated QA complete; manual browser UAT remains outstanding. Phase 5 overall remains **IN PROGRESS**.
**SQL test target:** `PC01\SQLEXPRESS / NEXORA_IntegrationTests` only.

## Delivered

- Warehouse queue lists only `PREPARING` orders and supports search by order ID, SKU or product name with bounded pagination.
- Warehouse order detail shows order items/SKU, ordered quantity, physical stock, total reserved, reservation attributed to the order and available quantity. It displays a pending-cancellation warning and omits customer phone/email.
- Only the Warehouse role can access Warehouse GET/POST actions. Export is POST-only and validates ASP.NET Core antiforgery tokens.
- Export conditionally changes `PREPARING → EXPORTED` only while cancellation status is NULL or `REJECTED`. A pending cancellation request blocks export in the backend transaction, independent of UI state.
- One SQL Server `SERIALIZABLE` transaction performs the conditional Order transition, stable ascending ProductId inventory locks (`UPDLOCK`, `ROWLOCK`, `HOLDLOCK`), line/ledger/reservation validation, `Inventory.ExportReserved`, one `OUT` entry per product and commit. Any failure rolls back the entire order.
- Repeated export verifies the current Order state and complete OUT ledger before returning an already-exported result. Unknown/inconsistent commit outcomes are surfaced for inspection rather than blindly replayed.
- SQL Server deadlock victim error 1205 is confirmed rolled back by SQL Server, then Order/ledger state is checked before a bounded whole-transaction retry (maximum three attempts). Rowversion conflicts are returned as concurrency conflicts.
- Customer request, cancellation approval/rejection and export all claim/conditionally update the Order row before acquiring inventory locks. Inventory locks are acquired in ascending ProductId order by export and reservation-release flows.

## Automated verification

| Test category | Result | What it covers |
|---|---:|---|
| Unit/policy | Included in full-suite result | Six `CanExport` cases: PREPARING with no request/rejected request allowed; pending/approved/unknown request and non-PREPARING rejected |
| HTTP integration test host | Included in full-suite result | Anonymous challenge; Customer/Sales/Admin forbidden; Warehouse queue/detail allowed; missing order 404; export POST requires antiforgery; valid form token accepted; actor ID passed to service; private email not rendered |
| SQL Server integration | Included in full-suite result | Successful export; no double decrement/OUT on replay; pending blocks; rejection permits export; approval releases and blocks; missing inventory; insufficient reservation; empty order; failure while writing OUT rolls back; two simultaneous exports of one order; request-vs-export race; approval-vs-export race; two different orders sharing the same product |
| Full solution regression | **187 passed, 0 failed, 0 skipped** | `dotnet test NEXORA.sln --no-build --no-restore`; all existing Phase 1–5.2 tests ran with the new Phase 5.3 suite |
| Restore/build | **Passed** | `dotnet restore NEXORA.sln`; isolated output build succeeded with 0 warnings and 0 errors |
| Repository checks | **Passed** | `git diff --check` completed without whitespace errors |

The concurrent SQL tests synchronize independent requests through a start barrier and use separate `AppDbContext` instances against SQL Server. They are not sequential simulations or EF InMemory tests.

After the full suite, the shared-inventory/two-order SQL race was run three additional times; each run passed (1 passed, 0 failed, 0 skipped).

## Database and schema verification

The test connection was parsed and accepted only when it matched `PC01\SQLEXPRESS / NEXORA_IntegrationTests`. All write/cleanup fixtures used unique Phase 5.3 markers and the SQL test collection is non-parallel. A post-test read-only SQLCMD check confirmed:

- QA database is `ONLINE`.
- It has 27 physical tables: 22 business tables, four Identity tables and `__EFMigrationsHistory`.
- Migration history remains the three existing IDs: `20261015173046_InitialFoundation`, `20261016133620_AddCheckoutRequestId`, and `20261019174632_AddOrderCancellationRequestFields`.
- No Phase 5.3 QA orders, products or users remain after fixture cleanup.
- No migration was generated or applied in Phase 5.3. No schema/DDL statement was run. The Development database `NEXORA` was not accessed.

## Manual/browser status and known limitations

- No real browser automation or manual browser UAT was performed in this implementation turn. The HTTP test host is not browser acceptance.
- No Warehouse employee account was provisioned or elevated. Browser acceptance must use a properly authorized Warehouse account.
- Current schema has no warehouse-prepared-by or prepared-at columns and no order-status-history table. The UI does not persist a separate “received/prepared” action; the order remains `PREPARING` until export.
- `inventory_transactions` has no operation ID/unique key. Export replay protection is based on transactional Order state, and a complete per-order OUT ledger check. This is not an unconditional exactly-once guarantee for every possible privileged database mutation or corrupted ledger state.
- Inventory receipt (`IN`), stock adjustment (`ADJUST`), warehouse ledger management and their retry/idempotency policy remain Phase 5.4.

## Changed files for Phase 5.3

- `src/NEXORA.Application/Commerce/WarehouseFulfillmentContracts.cs`
- `src/NEXORA.Infrastructure/Commerce/WarehouseFulfillmentService.cs`
- `src/NEXORA.Infrastructure/Commerce/CustomerOrderService.cs` (stable inventory lock order when releasing a direct cancellation)
- `src/NEXORA.Infrastructure/Commerce/OrderCancellationReviewService.cs` (stable inventory lock order during approved cancellation)
- `src/NEXORA.Infrastructure/DependencyInjection.cs`
- `src/NEXORA.Domain/Entities/CommerceEntities.cs` (strict export policy for NULL/REJECTED cancellation only)
- `src/NEXORA.Web/Areas/Warehouse/Controllers/OrdersController.cs`
- `src/NEXORA.Web/Areas/Warehouse/Views/Orders/Index.cshtml`
- `src/NEXORA.Web/Areas/Warehouse/Views/Orders/Details.cshtml`
- `src/NEXORA.Web/Areas/Warehouse/Views/_ViewStart.cshtml`
- `src/NEXORA.Web/Areas/Warehouse/Views/_ViewImports.cshtml`
- `src/NEXORA.Web/Models/WarehouseViewModels.cs`
- `src/NEXORA.Web/Controllers/RoleLandingController.cs`
- `src/NEXORA.Web/Views/Shared/_Layout.cshtml`
- `tests/NEXORA.Tests/BusinessPolicyTests.cs`
- `tests/NEXORA.Tests/WarehouseAuthorizationHttpIntegrationTests.cs`
- `tests/NEXORA.Tests/WarehouseFulfillmentSqlServerIntegrationTests.cs`
- `docs/PHASE5_DESIGN_DRAFT.md`, `docs/IMPLEMENTATION_PLAN.md`, `docs/REQUIREMENTS_TRACEABILITY.md`, `docs/PHASE5_3_ACCEPTANCE.md`

Earlier uncommitted Phase 5.1/5.2 changes and the untracked `docs/PHASE4_SCHEMA_REVIEW.sql` were preserved. No Git add, commit or push was performed.

## Acceptance conclusion

The Phase 5.3 automated acceptance criteria for authorization, export transaction integrity, cancellation coordination, replay protection, inventory constraints, rollback and SQL Server concurrency are met on the isolated QA database. Phase 5.3 is **implemented / automated QA passed**, pending real-browser UAT. Phase 5 overall is not complete; Phase 5.4 and four-role UAT remain.
