# NEXORA – Phase 6.4 Manual UAT Checklist

**Status:** NOT RUN. Leave all result boxes blank until performed.
**Admin tester:** ____________________  **Date/device:** ____________________

Use the configured Development/QA site and authorized QA accounts. Do not use the main `NEXORA` database for write-based fixture preparation.

## P01 – Admin Dashboard live metrics

- [ ] PASS  [ ] FAIL
- Steps: Sign in as Admin and open `/Admin` (or the Dashboard link in Admin navigation). Compare cards to current catalog/consultation/order state visible in the QA environment.
- Expected: Displayed counts come from current stored records; no fabricated revenue, orders, or inventory values appear.
- Result/defect: ____________________  Evidence path: ____________________

## P02 – Order report date range and status counts

- [ ] PASS  [ ] FAIL
- Steps: Open `/Admin/Reports/Orders`; select a short date range around known QA orders; submit the filter.
- Expected: Orders are grouped by their `OrderDate` and status; changing date range changes only included orders; order count is not shown as revenue.
- Result/defect: ____________________  Evidence path: ____________________

## P03 – Revenue report completion rules

- [ ] PASS  [ ] FAIL
- Steps: Open `/Admin/Reports/Revenue`; select a range containing a known completed QA order and, if available, a non-completed QA order.
- Expected: Only COMPLETED orders with `CompletedAt` in range contribute; displayed revenue matches stored order totals once per order, not the current product price or multiplied line totals.
- Result/defect: ____________________  Evidence path: ____________________

## P04 – Product sales report

- [ ] PASS  [ ] FAIL
- Steps: Open `/Admin/Reports/Products`; filter a range containing a known completed QA order; inspect product and quantity rows and pagination.
- Expected: Quantities are summed from completed order-line snapshots; pending/cancelled/non-completed orders are excluded; product snapshot names remain readable.
- Result/defect: ____________________  Evidence path: ____________________

## P05 – Inventory thresholds and missing data

- [ ] PASS  [ ] FAIL
- Steps: Open `/Admin/Reports/Inventory`; test threshold 0 and a positive integer; inspect known zero, low, normal, missing, or anomalous rows if the QA data contains them.
- Expected: Available is physical minus reserved; zero is Out of stock; positive values up to threshold are Low; above threshold are Normal; missing/anomalous data are labeled distinctly; threshold is not saved.
- Result/defect: ____________________  Evidence path: ____________________

## P06 – Date boundaries and empty results

- [ ] PASS  [ ] FAIL
- Steps: Filter across midnight, month end, and year end using known QA dates where available; also choose a range with no records.
- Expected: Local date boundaries include the full selected Vietnam day and exclude the next local midnight; empty range shows an explicit empty state; invalid reversed range shows a validation message.
- Result/defect: ____________________  Evidence path: ____________________

## P07 – Authorization and read-only behavior

- [ ] PASS  [ ] FAIL
- Steps: Visit each report URL as Customer, Sales, Warehouse, and anonymous user. As Admin, inspect available actions.
- Expected: Only Admin can read reports; unauthorized roles are denied/redirected; the report UI has no stock mutation or export action.
- Result/defect: ____________________  Evidence path: ____________________

## P08 – Responsive and accessibility review

- [ ] PASS  [ ] FAIL
- Steps: Check dashboard and each report at 1440×900, 768×1024, and 390×844. Use keyboard Tab/Enter and inspect focus indication/labels.
- Expected: No page-level horizontal overflow or overlapping text; tables remain usable through their responsive container; labels and focus states are visible; filters/buttons can be operated on mobile.
- Result/defect: ____________________  Evidence path: ____________________

**Overall result:** ____________________  **Tester:** ____________________  **Date:** ____________________
