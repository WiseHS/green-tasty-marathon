# Homework 8 — group pre-order

**Direction 1. Approach: Playwright Test + Page Object Model + API fixtures.**
Tests target our integrated portable .NET/Angular implementation. The original .NET BDD framework next door is preserved. This independent suite has a smaller setup and tests the actual group-order contract.

## Run

Node 24.22.2 or compatible is required. From this folder:

```powershell
npm ci
npx playwright install chromium
$env:UI_URL='http://localhost:4200'
$env:API_URL='http://127.0.0.1:5080'
$env:WAITER_USERNAME='demo-waiter@example.com'
# Set WAITER_PASSWORD privately in your environment; do not commit it.
npm test
npm run test:ui
npm run test:api
npm test -- --grep @L1
npm run report
```

Start the backend and frontend using the repository deployment instructions. The demo seed must exist. UI_URL/API_URL can instead point to your own cloud deployment. No default production URL or credentials are embedded. Authentication requests are paced below the application limit; there are no retries masking failed tests. Avoid simultaneous runs on one deployment: authentication is limited to 20 requests per minute.

## Coverage and chosen behaviour

- L1: owner sees selections and subtotals; submitted and draft guests; automatic refresh; empty group; read authorization and missing reservation.
- L2: review checkbox; submitted-only consolidation by dish ID; draft exclusion; waiter reads the same order in the UI; empty submission refusal; stale revision rejected; repeated submission returns the same order.
- L3: submitted guest removal through UI, total recalculation and subsequent submission; foreign/missing participant and foreign owner refusals.

The homework suite covers 23 scenarios across L1–L3. Negative UI scenarios separately cover draft-only carts, submitted empty carts, missing orders, canceled reservations, expired pre-order deadlines, unavailable dishes (OnStop and OutOfStock), and missing groups. The removal flow continues through submission to the waiter UI; the last guest and locked submitted groups are also checked. API checks exercise the role matrix and missing/foreign resources. Order and group statuses are verified after submission.

A group without joined guests or organizer dishes returns `participants: []` and `total: 0`. Once dishes or guests exist, the organizer row is shown. The organizer's current selection is included without a separate guest submission. Open group access: anonymous 401, unrelated customer 403, guest session token 401, assigned waiter 404 until submission; assigned waiter can read the immutable submitted snapshot. Repeated submit is idempotent (200, same order ID), stale revision 409, empty submission 400. A missing reservation order prevents sending (400), including when a guest has submitted; the UI suggests refreshing or contacting the restaurant, matching the workshop requirement. An organizer with an existing empty order can submit guest dishes without ordering food personally; a separate positive UI scenario verifies this. These choices describe our current implementation rather than silently changing expected results.


Seven additional @feature-requests scenarios cover the competition feature: guest to owner to waiter delivery, persistence after reload, validation and guest isolation, revision invalidation, immutable submission, duplicate guest names and literal HTML text, removed items/participants, and deadline/closed-group restrictions. Total: 30 scenarios. The full role flow attaches screenshots from all three screens. Run this subset with npm test -- --grep @feature-requests.

## Isolated edge cases

Eleven scenarios tagged @isolated require AQA_ISOLATED_DATABASE=true and AQA_DATABASE_URL pointing to the disposable PostgreSQL database named restaurant on localhost. API_URL must also be loopback. The helper rejects remote databases and APIs, uses parameterized SQL, saves original records, restores them before reservation cancellation, and closes its connection even after a failed assertion. No fault-injection endpoint is added to the application. These scenarios are explicitly skipped when running against a public site; the CI workflow enables and runs them all. Do not set the isolation flag for a shared or production database. Use npm test -- --grep-invert @isolated for the public-site subset.

Authentication pacing uses a temporary file containing only request timestamps, preserving the budget across test files and worker replacement. Global teardown removes it. The suite intentionally uses one worker to respect the shared server limit.

The past-deadline fixtures temporarily change only synthetic reservation times; unavailable dish fixtures temporarily alter the disposable seed menu. Missing-order, missing-group and empty-submitted-cart states are prepared directly because ordinary UI actions prevent creating them.

## Data and evidence

Each test creates unique `aqa-<guid>@example.com` customers and future table slots. Reservations are created by the configured waiter for the synthetic customer, ensuring assignment. Teardown cancels only tracked synthetic reservations, including after failures. There is no account deletion API: uniquely prefixed accounts and canceled reservation history remain. CI uses an ephemeral PostgreSQL service, destroyed after the job; production data is never used by CI.

HTML report: `playwright-report/index.html`; JUnit: `test-results/results.xml`; failed UI scenarios have screenshots. API/network traces and videos are disabled to avoid recording passwords and bearer tokens. Assertions never print credentials or sign-in responses. Generated evidence and local settings are ignored by Git.

The GitHub Actions workflow `.github/workflows/group-order-e2e.yml` builds both applications, seeds its isolated database, runs all scenarios and uploads reports even after test failures. It then marks the job failed if tests failed. The workflow passing must be verified in Actions; configuration alone is not proof of a successful cloud run. Report Portal is optional extra credit and is not configured.
