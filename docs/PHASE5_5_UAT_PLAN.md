# NEXORA – Phase 5.5 Browser and Manual UAT Plan

**Status:** IN PROGRESS – browser smoke has started; authenticated role workflows and owner manual UAT are pending.
**QA target:** `PC01\SQLEXPRESS / NEXORA_IntegrationTests` only.
**Website:** `http://127.0.0.1:5059` (kept running for owner testing).
**Date prepared:** 2026-10-20.

## 1. Environment and safety preconditions

| Check | Required result | Actual result |
|---|---|---|
| SQL instance/database | `PC01\SQLEXPRESS / NEXORA_IntegrationTests` | PASS, read-only preflight |
| Database state | ONLINE | PASS |
| Migration history | `20261015173046_InitialFoundation`, `20261016133620_AddCheckoutRequestId`, `20261019174632_AddOrderCancellationRequestFields` | PASS, exact three IDs |
| Physical tables | 27 total: 22 business, four Identity support, one `__EFMigrationsHistory` | PASS |
| Phase 5 schema | `orders.cancel_request_status`, `orders.completed_at`, `inventories.row_version` present | PASS |
| QA fixture baseline | 12 active products/inventories; 11 demo items available at 10 each, one existing QA out-of-stock fixture at 0; two pre-existing CANCELLED orders | PASS, read only; do not repurpose the two orders |
| Promotion coverage | One `PERCENTAGE`, one `FIXED_AMOUNT` row | PASS, read only |
| Employee identities | Sales 0, Warehouse 0 before provisioning | CONFIRMED; local creation is waiting for owner input |
| Main database | Must not be connected to or changed | No connection was made to `NEXORA` during this Phase 5.5 work |

`appsettings.Development.json` defaults to the Development database `NEXORA`. Every QA website, provisioning, and SQL test process must explicitly override `ConnectionStrings__DefaultConnection` with the QA connection shown below. The `NEXORA_SQLSERVER_TEST_CONNECTION` test variable must also point to that same QA database. Do not run QA tests with the default appsettings value.

The application startup code now skips Catalog, Consultation, and Inventory demo seeders unless the connection string identifies the approved Development database `PC01\SQLEXPRESS / NEXORA`. With the QA override, the app logs that these seeders were skipped. The QA account command performs a read-only schema/role preflight before it accepts account data.

### Start or restart the QA website

In a local PowerShell terminal from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=PC01\SQLEXPRESS;Database=NEXORA_IntegrationTests;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10'
dotnet run --no-build --no-restore --no-launch-profile --project .\src\NEXORA.Web\NEXORA.Web.csproj --urls http://127.0.0.1:5059
```

Keep this terminal open. Do not run `database update`, migration scripts, `EnsureDeleted`, or a full QA reset.

### Prepare staff accounts

Follow [QA_ACCOUNT_PROVISIONING.md](./QA_ACCOUNT_PROVISIONING.md). It creates only a new `Sales` or `Warehouse` user, takes the password through hidden local console input, and refuses existing accounts. Current QA identities include Customer/Admin assignments, but the email domains were not QA-marked; they were not modified, reset, or used by Codex. The owner must decide whether those existing identities are suitable for browser login.

## 2. Route map verified from MVC Areas

| Actor | Read routes | Mutation routes |
|---|---|---|
| Customer | `/Orders`, `/Orders/Details/{id}` | `/Orders/Cancel`, `/Orders/RequestCancellation` (POST) |
| Sales | `/Sales/Orders`, `/Sales/Orders/Details/{id}` | `Confirm`, `Handoff`, `ReviewCancellation`, `MarkShipping`, `Complete` (POST) |
| Admin | `/Admin/Orders`, `/Admin/Orders/Details/{id}` | `Confirm`, `ReviewCancellation` (POST); no warehouse writes or Sales shipment transitions |
| Warehouse | `/Warehouse/Orders`, `/Warehouse/Orders/Details/{id}`, `/Warehouse/Inventory`, `/Warehouse/Inventory/History` | `Export`, `/Warehouse/Inventory/Receive`, `/Warehouse/Inventory/Adjust` (POST) |

The route map comes from the current controller `[Area]` attributes and the application's area route. All mutations use `[ValidateAntiForgeryToken]`; test authorization at the server, not only by looking for hidden buttons.

## 3. Test record format

For every case, record **Actual result**, **Status** (`PASS`, `FAIL`, `NOT RUN`, or `BLOCKED`), performer, date, and a local screenshot/error reference. Do not capture a password, cookie, anti-forgery token, or unnecessary customer personal data. Save sanitized screenshots outside the repository, for example under `%USERPROFILE%\Pictures\NEXORA-UAT\`; record the filename here. A blank/`NOT RUN` row is not a pass.

## 4. Customer scenarios

### TC-C01 – Checkout success

- **Steps:** Sign in as Customer; choose an available item; add it to Cart; verify line and total; proceed through Checkout; submit once; open the resulting order.
- **Expected:** Order is `PENDING`; `ReservedQuantity` rises by the ordered quantity; `QuantityInStock` does not fall; available quantity falls; one `RESERVE` ledger entry exists; order detail preserves the quoted price.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-C02 – Direct cancel of PENDING

- **Steps:** Use a separate newly created PENDING order; open its detail; cancel with a reason; refresh the order and inventory.
- **Expected:** Order becomes `CANCELLED`; one `RELEASE` is recorded; physical quantity is unchanged; a second cancellation is rejected without another release.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-C03 – Request cancellation after confirmation

- **Steps:** Create a separate order; have Sales confirm and hand it to Warehouse (`PREPARING`); Customer submits one cancellation reason; reload the detail.
- **Expected:** Request state becomes `PENDING`; order remains `CONFIRMED`/`PREPARING`; stock, reservation and ledger do not change; UI shows awaiting review; a second request is unavailable/rejected.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-C04A / TC-C04B – View approved/rejected cancellation decision

- **Steps:** Create two separate orders and requests. For A, ask Sales/Admin to approve. For B, ask Sales/Admin to reject with a reason. Customer reopens each order.
- **Expected A:** Order `CANCELLED`, reservation released once, result visible to owner.
- **Expected B:** Order remains in its pre-decision state; reservation and stock are unchanged; rejection reason is visible; a second request is not allowed.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

## 5. Sales scenarios

### TC-S01 – Order list, search, filter and detail

- **Steps:** Sign in as Sales; open `/Sales/Orders`; search for a newly created QA order; filter by status; open detail.
- **Expected:** The newly created order is found with correct items, amounts, status, and cancellation state; no unrelated personal fields are exposed beyond the current view's purpose.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-S02 – Confirm PENDING

- **Steps:** Open a Customer-created PENDING order and submit Confirm once; refresh and attempt the same transition again.
- **Expected:** `PENDING → CONFIRMED`; no stock/reservation change; duplicate confirmation does not create a second transition or inventory ledger row.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-S03 – Handoff to Warehouse

- **Steps:** Sales hands a confirmed order to Warehouse.
- **Expected:** `CONFIRMED → PREPARING`; it appears in Warehouse queue; no stock/reservation change and no `OUT` entry.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-S04A / TC-S04B – Approve and reject cancellation request

- **Steps:** On separate pending requests, Sales approves one and rejects the other with a reason; refresh the order and Warehouse queue.
- **Expected:** Approval cancels/releases once; rejection leaves order/reservation intact and saves the reason; repeat decision is refused; pending request cannot race through to export.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-S05 – Shipping and completion

- **Steps:** For an order Warehouse has exported, Sales marks Shipping, then completes it. Refresh the order detail.
- **Expected:** Only `EXPORTED → SHIPPING → COMPLETED`; no skipped transition; `completed_at` is populated; inventory is not adjusted a second time.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

## 6. Warehouse scenarios

### TC-W01 – Queue and order detail

- **Steps:** Sign in as Warehouse; open `/Warehouse/Orders`; search an order/SKU; open detail.
- **Expected:** Only `PREPARING` orders are actionable; SKU, line quantities and reservation are visible; unnecessary customer phone/email are not displayed.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W02 – Export once

- **Steps:** Export a PREPARING order with no pending cancellation; refresh and try replay only if the UI still offers it.
- **Expected:** `PREPARING → EXPORTED`; physical stock and reservation both decrease by shipped quantities; an `OUT` row exists; replay does not decrement or log OUT again.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W03 – Pending cancellation blocks export

- **Steps:** Warehouse opens an order with a pending cancellation request; attempt export if a control remains visible.
- **Expected:** Backend refuses; no status, inventory, reservation, or `OUT` ledger change. UI warning is visible.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W04 – Export after rejected cancellation

- **Steps:** After Sales rejects a request, Warehouse exports the still-PREPARING order.
- **Expected:** Export succeeds once with correct inventory and `OUT` transaction.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W05 – Inventory list/search/filter/paging

- **Steps:** Open `/Warehouse/Inventory`; search one demo SKU and product name; filter out of stock; move pages; compare physical/reserved/available values.
- **Expected:** Search/filter/paging work; available = physical − reserved; the existing QA OOS fixture is shown at zero and is not mistaken for available stock.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W06 – Receive stock

- **Steps:** Receive a positive quantity for a product with an Inventory row, with a unique reference. Record values before and after; view history.
- **Expected:** Physical increases, reservation is unchanged, available increases, one `IN` is recorded; zero/negative quantities are rejected; replay of the same form does not add stock twice.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W07 – Increase/decrease adjustment

- **Steps:** Apply a positive increase, a safe decrease, then try a decrease that would cross ReservedQuantity; also try blank reason and invalid quantity.
- **Expected:** Valid adjustments record `ADJUST` with direction/reason; unsafe/invalid changes are rejected; stock never goes below reservation; no failure creates a ledger row.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-W08 – Inventory transaction history

- **Steps:** Open `/Warehouse/Inventory/History`; filter by product, type and date; inspect `IN`, `OUT`, `ADJUST`, `RESERVE`, and `RELEASE` rows generated by this UAT.
- **Expected:** Only fields actually stored by the schema are shown; order/actor/reference/note values match; legacy adjustment direction is not invented.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

## 7. Admin and authorization scenarios

### TC-A01 – Admin oversight and permitted order actions

- **Steps:** Sign in as Admin QA; view `/Admin/Orders` and detail; confirm a PENDING order; approve/reject cancellation; open Warehouse Inventory and History; inspect whether warehouse-write/Sales-shipping actions are absent.
- **Expected:** Admin may monitor, confirm, and review cancellation; Admin cannot export, receive/adjust inventory, hand off, mark Shipping, or complete. Direct server POST authorization remains authoritative.
- **Actual:** Pending manual UAT.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

### TC-X01 – Cross-role authorization and antiforgery

- **Steps:** In separate browser sessions, try Customer → Sales/Warehouse/Admin; Sales → Warehouse writes; Warehouse → Sales confirmation/review; Admin → Warehouse writes and Sales-only transitions; Anonymous → all protected pages. Verify a POST without a valid antiforgery token is rejected.
- **Expected:** Server denies every unauthorized request regardless of menu visibility or altered URL/form values. Customer cannot open another customer's order. Anonymous receives a login challenge. Antiforgery-protected mutation rejects a missing token.
- **Automated evidence:** HTTP integration tests in the suite; this does not count as manual browser testing.
- **Actual:** Anonymous `/Warehouse/Inventory` was opened in browser and redirected to Login. All authenticated cross-role and POST checks remain pending manual/browser UAT.
- **Status / performer / date:** PARTIAL / Codex browser smoke / 2026-10-20.
- **Evidence:** No screenshot saved; browser DOM state showed the Login route with the requested return URL.

## 8. Responsive and visual scenarios

Use Chrome/Edge DevTools Device Toolbar and check desktop `1440×900`, tablet `768×1024`, mobile `390×844` on Customer Orders, Sales Orders, Admin Orders, Warehouse queue/detail, Inventory/History, and Receive/Adjust forms. Verify navigation, tables, form validation, confirmation/success/error states, no horizontal page overflow, and usable buttons. Do not submit production data. Record actual viewport and browser version.

- **Actual:** Codex's browser inventory exposed only the in-app browser, whose viewport override was not available through the connected control API. No Chrome/Edge desktop viewport test was performed.
- **Status / performer / date:** NOT RUN / — / —.
- **Evidence:** —.

## 9. Browser smoke completed by Codex

The ChatGPT in-app browser rendered the local application at the QA URL. Codex opened Home, Catalog, Product Details, Consultation, and the protected Warehouse Inventory route. Home/Catalog/Detail/Consultation rendered; Catalog showed 12 products, including the existing zero-stock QA fixture; the protected route redirected an anonymous session to Login. This is **browser automation smoke**, not authenticated browser UAT, not user manual UAT, and not a Chrome/Edge desktop viewport check. No screenshot artifact was saved.

The Login page exposed browser autofill. Codex did not use, copy, or submit the autofilled values and navigated away. The owner must confirm an identity is a QA account before it is used for login testing.

## 10. Final result recording

Do not change Phase 5 to COMPLETED until all required workflow, data-integrity, authorization, browser/manual, and owner approval criteria in `PHASE5_5_ACCEPTANCE.md` are met. Preserve existing QA data; only create or clean up fixtures that are uniquely identified and created by the tester, and only within `NEXORA_IntegrationTests`.
