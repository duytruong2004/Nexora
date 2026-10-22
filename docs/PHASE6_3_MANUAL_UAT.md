# NEXORA – Phase 6.3 Manual UAT Checklist

**Status:** NOT RUN. Leave all result boxes blank until performed.
**Admin tester:** ____________________  **Date/device:** ____________________

Use a QA review fixture or a review created through the approved test flow. Do not edit customer review text/rating or create fake evidence.

## M01 – Admin review queue, filter, search and paging

- [ ] PASS  [ ] FAIL
- Steps: Sign in as Admin; open `/Admin/Reviews`; filter by PENDING; search for a known QA product/customer marker; move to the next page when available.
- Expected: Results reflect the selected filter/search/page; rating/comment are displayed read-only.
- Result/defect: ____________________  Evidence path: ____________________

## M02 – Approve pending review

- [ ] PASS  [ ] FAIL
- Steps: On a QA PENDING review, select Approve.
- Expected: State becomes APPROVED; customer text/rating remain unchanged; the review becomes visible on its product detail page.
- Result/defect: ____________________  Evidence path: ____________________

## M03 – Hide pending and approved review

- [ ] PASS  [ ] FAIL
- Steps: Hide one PENDING review; then hide a separate APPROVED review.
- Expected: Both become HIDDEN; neither is shown publicly; content is preserved.
- Result/defect: ____________________  Evidence path: ____________________

## M04 – Restore hidden review

- [ ] PASS  [ ] FAIL
- Steps: Filter HIDDEN and restore a QA review.
- Expected: State becomes APPROVED and is publicly visible; content remains unchanged.
- Result/defect: ____________________  Evidence path: ____________________

## M05 – Same-state action and concurrent change

- [ ] PASS  [ ] FAIL
- Steps: Repeat an action that requests the review's current state. If two Admin sessions are available, submit conflicting actions for the same review.
- Expected: Same-state action is a safe no-op; a stale competing action reports conflict/reload and does not overwrite the latest state.
- Result/defect: ____________________  Evidence path: ____________________

## M06 – Unknown status fail-closed

- [ ] PASS  [ ] FAIL
- Steps: Only if a dedicated QA fixture with unknown status is already available, inspect Admin queue and product detail. Do not edit the database manually for this UAT.
- Expected: Admin sees an unknown-state warning; no moderation transition is applied; public page does not show the review.
- Result/defect: ____________________  Evidence path: ____________________

## M07 – Role authorization and antiforgery

- [ ] PASS  [ ] FAIL
- Steps: Try `/Admin/Reviews` as Customer, Sales, Warehouse, and anonymous user. For an authenticated non-Admin, attempt the POST route without using a valid Admin session. Inspect that normal Admin form submissions include antiforgery.
- Expected: Only Admin can view/mutate; other roles are denied/redirected by authorization; invalid/missing antiforgery is rejected.
- Result/defect: ____________________  Evidence path: ____________________

## M08 – Responsive review queue

- [ ] PASS  [ ] FAIL
- Steps: Inspect queue at desktop 1440×900, tablet 768×1024, and mobile 390×844 using browser responsive tools.
- Expected: Filter controls and row actions remain usable; no page-level horizontal overflow or content overlap.
- Result/defect: ____________________  Evidence path: ____________________

**Overall result:** ____________________  **Tester:** ____________________  **Date:** ____________________
