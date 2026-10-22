# NEXORA – Phase 6.2 Manual UAT Checklist

**Status:** NOT RUN. Leave all result boxes blank until a person performs the steps.
**Environment/account:** ____________________  **Date/device:** ____________________

For each case, record PASS/FAIL and any defect below. Attach a real screenshot only if one is actually captured during the test; redact personal information before attaching.

## F01 – Add a product to Favorites

- [ ] PASS  [ ] FAIL
- Steps: Sign in as a Customer; open `/Catalog`; use the favorite control on an active product.
- Expected: A success message appears; the item appears once in `/Favorites`; the product is unchanged in Catalog.
- Result/defect: ____________________  Evidence path: ____________________

## F02 – Duplicate favorite request

- [ ] PASS  [ ] FAIL
- Steps: On the same product, submit Add again or repeat the action after returning to Catalog.
- Expected: The favorite remains a single row; the UI reports it is already saved or equivalent; no duplicate is created.
- Result/defect: ____________________  Evidence path: ____________________

## F03 – Remove favorite

- [ ] PASS  [ ] FAIL
- Steps: Open `/Favorites`; remove one item.
- Expected: Only that Customer's selected favorite disappears and a confirmation is shown.
- Result/defect: ____________________  Evidence path: ____________________

## F04 – Empty Favorites state

- [ ] PASS  [ ] FAIL
- Steps: Remove all test favorites or use a Customer account with none; open `/Favorites`.
- Expected: A clear empty state and Catalog navigation appear; no exception or blank page.
- Result/defect: ____________________  Evidence path: ____________________

## R01 – Submit a review for an eligible completed order line

- [ ] PASS  [ ] FAIL
- Steps: Sign in as the Customer who owns a COMPLETED order with an unreviewed line; open `/Reviews`; submit a rating 1–5 and a short comment for that line.
- Expected: Review is accepted and shown to its owner as PENDING; it is not public yet.
- Result/defect: ____________________  Evidence path: ____________________

## R02 – Ineligible order line

- [ ] PASS  [ ] FAIL
- Steps: Attempt to submit a review for an order that is not COMPLETED, if a safe QA fixture is available.
- Expected: Server rejects the submission; no review is created.
- Result/defect: ____________________  Evidence path: ____________________

## R03 – Ownership and ID tampering

- [ ] PASS  [ ] FAIL
- Steps: As Customer A, change a submitted form's order-detail identifier to a line known to belong to Customer B, then submit with a valid antiforgery token from A's own page.
- Expected: Server rejects it; A cannot create, view, or modify B's review/order data.
- Result/defect: ____________________  Evidence path: ____________________

## R04 – Review input validation and encoded output

- [ ] PASS  [ ] FAIL
- Steps: Try a rating outside 1–5 and a comment longer than 3,000 characters. For XSS encoding, use a harmless literal such as `<script>alert(1)</script>` in a QA review, then inspect the rendered page without executing copied markup.
- Expected: Invalid input is rejected; stored text renders encoded and does not execute as HTML/script.
- Result/defect: ____________________  Evidence path: ____________________

## R05 – Review visibility by owner and public status

- [ ] PASS  [ ] FAIL
- Steps: While the review is PENDING, check the owner's `/Reviews`, the product detail as another Customer or anonymous visitor, and later repeat after Admin moderation approves it.
- Expected: Owner sees own PENDING status; other users/public do not see PENDING; after approval only the APPROVED review is public.
- Result/defect: ____________________  Evidence path: ____________________

## R06 – No customer edit/delete after submission

- [ ] PASS  [ ] FAIL
- Steps: Inspect `/Reviews` and attempt only normal UI actions available to the Customer.
- Expected: Customer can see their submitted review/status but has no edit or delete control.
- Result/defect: ____________________  Evidence path: ____________________

## Cross-user visibility

- [ ] PASS  [ ] FAIL
- Steps: Sign in with a second Customer account and open `/Favorites` and `/Reviews`.
- Expected: Only the second Customer's own data is displayed.
- Result/defect: ____________________  Evidence path: ____________________

**Overall result:** ____________________  **Tester:** ____________________  **Date:** ____________________
