# Test Automation homework #8

**Direction:** 1, group pre-order. **Approach:** TUnit API end-to-end tests for levels 1–3, plus two Playwright UI smoke scenarios from Direction 2. The API tests exercise the deployed backend through HTTP; the UI scenarios open the deployed storefront in a real browser. The provided .NET Reqnroll/Playwright framework is under [`restaurant-aqa-framework`](restaurant-aqa-framework).

The frontend group-order page in this repository has not yet been deployed to the CloudFront URL, so the assignment's backend-only path applies. The suite does not replace a future UI suite for an integrated deployment.

## Coverage

| Level | Scenarios |
| --- | --- |
| 1 | Participant names, dish IDs and quantities, totals, pending and empty carts, changed cart, empty group, Owner-only access (customer, guest, waiter, anonymous), missing group |
| 2 | Owner session, submitted-only consolidation by dish ID, waiter-visible order and both statuses, no submitted cart, empty cart, missing order/group/reservation, cross-group reservation, repeat submit, non-Owner |
| 3 | Remove a submitted guest, recalculate snapshot, submit without removed guest and verify waiter order, remove last guest, cross-group guest, missing guest, non-Owner, protect Owner session |

Additional UI coverage checks that the home page loads, dish and location cards display real images, and the Book a table link opens the table search form.

The empty submitted-cart server branch cannot be reached through public API routes: `POST /guest/sessions/{id}/submit` rejects an empty cart. The test checks this refusal and then verifies that group submission remains blocked. The backend unit test `GroupPreOrderServiceTests.SubmitAsync_RejectsEmptySubmittedSessions` covers the internal guard. A group that has no sessions returns `participants: []` and `total: 0`. Repeat submit returns HTTP 400. A non-Owner customer or waiter gets HTTP 403; anonymous and guest tokens are rejected by the gateway or handler with HTTP 401/403. Removing the last guest returns an empty snapshot. A reservation ID that does not match the group returns HTTP 400. Unknown reservation on the Owner-session route returns HTTP 404.

## Configure

Use your own deployed application. From `aqa/restaurant-aqa-framework`, copy `.env.example` to `.env` and set:

```dotenv
UI_URL=https://YOUR-CLOUDFRONT-DOMAIN
API_URL=https://YOUR-API-ID.execute-api.eu-north-1.amazonaws.com/develop
WAITER_USERNAME=YOUR-WAITER-EMAIL
WAITER_PASSWORD=YOUR-WAITER-PASSWORD
TESTTABLE_ID=OPTIONAL-TABLE-ID
```

The standing waiter must belong to the deployment. The API cannot register a waiter; ordinary registration creates a Customer. The selected table's location needs a waiter and at least two available dishes. `TESTTABLE_ID` is optional; without it the fixture searches available tables. Optional `AQA_OWNER_USERNAME` and `AQA_OWNER_PASSWORD` reuse a standing Customer account. Otherwise each test registers a unique Customer email. The public API has no user-deletion route. Each test uses a random future reservation time, cancels its reservation in teardown, and never relies on another test's data. Cancellation changes status but does not delete the DynamoDB record; the unique booking IDs and dates prevent collisions. No credentials or `.env` files are committed.

The latest backend code must be deployed for these scenarios. The earlier AWS deployment predates the Owner endpoints in this repository. Deploy it using the root [deployment guide](../README.md) before a live run; no infrastructure creation is required for test development.

## Run

From `aqa/restaurant-aqa-framework`:

```powershell
dotnet restore
dotnet build
$env:SKIP_PRE_RUN_CLEANUP="true"
dotnet run --no-build -- --report-trx --treenode-filter "/*/*/*/*[Category=group-preorder]"
```

Run one level by replacing `group-preorder` with `group-l1`, `group-l2`, or `group-l3`. Run only the UI smoke scenarios with `group-ui`. The framework installs Chromium automatically on the first run. To avoid load on a small AWS deployment, set `MAX_PARALLEL_TESTS=2` in `.env`.

Set `SKIP_PRE_RUN_CLEANUP=true` when running this subset: the starter framework's sweep targets unrelated `AQA-Sample-` records, while these group scenarios manage their own bookings. Reports and failing UI traces are written to `aqa/restaurant-aqa-framework/TestResults/`. Failed API assertions include the HTTP status and response body in the TRX and HTML report. All generated results are ignored by Git.

## CI

`.github/workflows/aqa-quality.yml` builds and discovers tests on each change. `.github/workflows/aqa-tests.yml` runs against the deployed app through **Actions → AQA Group Pre-order → Run workflow**. Select `all`, `l1`, `l2`, `l3`, or `ui`. Add repository variables `AQA_UI_URL`, `AQA_API_URL`, and optionally `AQA_TESTTABLE_ID`; add secrets `AQA_WAITER_USERNAME`, `AQA_WAITER_PASSWORD`, and optionally `AQA_OWNER_USERNAME`, `AQA_OWNER_PASSWORD`. Failed scenarios do not prevent report upload; the job fails after publishing artifacts. The workflow uses GitHub Actions, so the optional ReportPortal bonus is not included.
