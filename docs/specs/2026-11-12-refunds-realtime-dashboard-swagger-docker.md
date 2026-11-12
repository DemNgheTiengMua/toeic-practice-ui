# Refunds, a dashboard that cannot lie, Swagger, and a container run

**Status:** ready-for-agent
**Date:** 2026-11-12
**ADRs touched:** ADR-0001 (database-first), ADR-0002 (MVC proxies API), ADR-0012 (one writer per derived fact)

---

## Problem Statement

Three separate gaps, one of them a correctness defect rather than a missing feature.

**The dashboard cannot tell the truth about money.** The only revenue figure is "revenue this month", computed as `SUM(Orders.Amount)` over paid orders. Every amount in the schema is `CHECK (Amount >= 0)`, so the sum can never be negative. That is wrong in a real month: if a learner is refunded for an order paid earlier in the same month, the money that came in is no longer the money the business kept. Today the dashboard shows the gross figure and calls it revenue. Worse, the number is not even stable across screens: the dashboard counts a paid order whose `PaidAt` is null by falling back to `CreatedAt`, while the orders screen counts only orders with `PaidAt` inside the month. Two admin screens, one question, two answers. And both cut the month at UTC midnight, so an accountant working in Vietnam reads a month boundary that is seven hours wrong.

**There is no way to give money back.** The product states it outright in the complaint screen: there are no cash refunds. A complaint can grant up to 20 credits as a make-good, recorded as `admin_grant`, and that is the end of the story. Support cannot record that a learner was promised a bank transfer. Accounting has no list to pay from. Nothing on the dashboard knows a refund happened, so the revenue figure above is not merely gross, it is gross with no way to ever become net.

**The dashboard is a photograph.** It is server-rendered once and never changes until someone reloads the page. An admin sitting on it will not see a new order, a new complaint, or a complaint changing status. During a launch, that is the screen a staff member leaves open.

**Two supporting gaps.** The API has no discoverable documentation: 22 endpoint groups, no OpenAPI document, nothing a reviewer can browse. And the solution cannot be run without a Windows LocalDB, a hand-provisioned signing key, and a shell script that assumes `bash` and `netstat` — so a teammate who wants to try the product, or a reviewer who wants to see it, starts with an hour of setup or does not start at all.

---

## Solution

**Refunds become a first-class record with two methods and a lifecycle.** A staff member records a refund against a learner, optionally tied to the complaint that prompted it and optionally tied to the order that was paid. The refund is either **credit** (paid immediately out of the ledger when approved) or **money** (paid by the accountant outside the system, and only marked settled here once the transfer has actually happened). Money refunds can be exported as a CSV that accounting opens in Excel. A credit refund posts a new `refund_credit` ledger row so that "credits refunded this month" becomes a number the product can answer.

**Revenue becomes net, and is therefore allowed to be negative.** A new card shows today's net revenue beside the month's. Both are paid orders minus money refunds settled in the same window, both cut at the accountant's day boundary (UTC+7), and both share one definition of "a paid order in this period" with the orders screen so the two can no longer disagree. A negative figure renders in Sign Red with a true minus sign; it is a business fact, not an error state.

**The dashboard updates itself without reloading.** A server-sent event stream carries one thing: a revision number. When it changes, the page fetches a small HTML fragment and replaces only the metric region. No full-page reload, no scroll position lost, no animation replayed, no number rendered twice by two different formatters.

**The API documents itself, and the solution runs in containers.** An OpenAPI document and a browsable UI are always available, with a one-click sign-in so a reviewer can call an authenticated endpoint without minting a token by hand. A compose file brings up the database, the schema, the API and the web app, and works on a machine that has never had SQL Server installed.

---

## User Stories

### Refunds

1. As a support agent, I want to record a refund against a learner, so that a promise made in a complaint reply exists as a record rather than a memory.
2. As a support agent, I want to attach the refund to the complaint that caused it, so that the complaint, the reason and the money are one line of evidence.
3. As a support agent, I want to attach the refund to an order when the money came from a purchase, so that accounting can reconcile it against the gateway statement.
4. As a support agent, I want to record a refund that has no order behind it, so that a technical failure or a mentor no-show can still be compensated.
5. As a support agent, I want to choose between a credit refund and a money refund, so that the cheap fix (credits) does not require the accountant.
6. As a support agent, I want a reason to be mandatory on every refund, so that no payment leaves the company without a recorded justification.
7. As a support agent, I want the request to be rejected if it exceeds what the learner actually paid for that order, so that a typo cannot refund more than came in.
8. As a support agent, I want the request to be rejected if it exceeds the configured ceiling when there is no order, so that a refund with no ceiling is impossible.
9. As a support agent, I want to see the learner's email and full name on the refund, so that accounting can identify the person without a second lookup.
10. As a support agent, I want to see the list of refunds filtered by status and date range, so that I can find the one I am chasing.
11. As an admin, I want to approve a refund, so that the decision is separated from the request.
12. As an admin, I want to reject a refund with a reason, so that the learner is not paid but the refusal is on record.
13. As an admin, I want approving a credit refund to post the credits immediately, so that the learner can use them without waiting for accounting.
14. As an admin, I want the credit refund to appear in the learner's credit ledger with its own reason, so that the ledger distinguishes a refund from an administrative adjustment.
15. As an admin, I want a credit refund to be irreversible once approved, so that the append-only ledger stays append-only and a mistake is corrected by an opposing entry, not a deletion.
16. As an accountant, I want approved money refunds to sit in a queue until I mark them settled, so that the dashboard never claims money went out before it did.
17. As an accountant, I want to mark a money refund settled with the date it happened, so that it lands in the right reporting day.
18. As an accountant, I want to export the approved money refunds for a date range as a CSV, so that I can pay them in a batch.
19. As an accountant, I want the CSV to open in Excel with Vietnamese diacritics intact, so that names are not mojibake.
20. As an accountant, I want the export to carry the refund id, dates, learner email and name, order code, method, amount or credits, reason, and the deciding admin, so that the file is sufficient on its own.
21. As an accountant, I want exporting to not change a refund's status, so that exporting twice does not lose the settlement state.
22. As a staff member, I want a refund's status to be one of requested, approved, settled or rejected, so that everyone describes it the same way.
23. As a staff member, I want the refund record to remember who requested, decided and settled it, so that a future split into separate support and accounting roles needs no data repair.
24. As a learner, I want the credits from an approved refund to appear in my balance, so that the compensation is real.

### Dashboard revenue

25. As an owner, I want to see today's net revenue, so that I know what the day actually earned.
26. As an owner, I want to see this month's net revenue, so that the month total is not a gross figure I have to correct by hand.
27. As an owner, I want net revenue to be allowed to go negative, so that a heavy refund day reads as a loss instead of as a stuck or broken number.
28. As an owner, I want a negative amount rendered in red with a true minus sign, so that it is unmistakable and still legible.
29. As an owner, I want the day and month to be cut at the accountant's boundary, so that an order paid at 11:30pm Vietnam time belongs to that day.
30. As an owner, I want credits refunded to be counted separately from revenue, so that a credit make-good is not mistaken for money leaving the bank.
31. As an owner, I want the dashboard and the orders screen to define a paid order the same way, so that two screens never show two totals for one question.
32. As an owner, I want a paid order with a missing payment timestamp to be counted once and consistently, so that the defect in the data does not become a defect in the report.

### Dashboard realtime

33. As an admin, I want the dashboard's numbers to update on their own, so that I do not have to keep reloading during a launch.
34. As an admin, I want a new paid order to appear without a reload, so that I see revenue move when it moves.
35. As an admin, I want a new complaint to appear without a reload, so that support load is visible as it happens.
36. As an admin, I want a complaint changing status to be reflected without a reload, so that the "needs attention" count is never stale.
37. As an admin, I want a new or decided refund to be reflected without a reload, so that the queue matches what I just approved in another tab.
38. As an admin, I want the update to replace only the numbers and lists, so that my scroll position and any form I am filling stay where they are.
39. As an admin, I want no animation to replay on update, so that a constantly refreshing dashboard is not a flickering one.
40. As an admin, I want to see when the numbers were last refreshed, so that I can tell a quiet dashboard from a dead one.
41. As an admin, I want a clear indicator when the live connection drops, so that I never mistake stale numbers for current ones.
42. As an admin, I want the live connection to recover on its own, so that a network blip does not require a reload.

### Documentation

43. As a backend developer, I want to browse every API route with its parameters, so that I can find the endpoint I need without reading source.
44. As a backend developer, I want to call an endpoint from the documentation page, so that I can check a response shape myself.
45. As a backend developer, I want to sign in from the documentation page without minting a token by hand, so that authenticated endpoints are as easy to try as anonymous ones.
46. As a reviewer, I want the documentation available in every environment, so that I can inspect the API on a deployed build.

### Containers

47. As a teammate, I want one command to bring up the whole solution, so that I can try the product without installing SQL Server.
48. As a teammate, I want the database schema and demo data loaded automatically on first run, so that the app is usable immediately rather than empty.
49. As a teammate, I want a clear way to reach the two apps in a browser, so that I do not have to discover ports.
50. As a teammate, I want uploaded audio to survive a container restart, so that I do not lose an attempt recording to a redeploy.
51. As a teammate, I want to flip between the mock payment gateway and the real one without editing code, so that I can demo safely and still test a real checkout.
52. As a maintainer, I want to know how to apply a schema change in the container environment, so that a schema edit does not silently do nothing.

---

## Implementation Decisions

### Refunds

- A new `Refunds` table is added to the schema file first, as ADR-0001 requires, then the model is re-scaffolded.
- `Refunds` carries: an identity primary key, a required learner, an optional complaint, an optional order, a required method (`money` or `credit`), a required reason, a status, and the three actor stamps (requester, decider, settler) with their timestamps, plus an optional link to the ledger row a credit refund posted.
- Method determines the amount column, not both: a money refund carries a money amount and no credit count; a credit refund carries a credit count and no money amount. The schema enforces this as a check constraint rather than trusting the service.
- The money amount uses the same decimal scale as order amounts, so the two can be subtracted without a conversion that rounds.
- Status is `requested`, `approved`, `settled`, `rejected`. `settled` is legal only for a money refund; a credit refund terminates at `approved` because the ledger, not a bank, is where it lands.
- A credit refund posts through the existing credit ledger adjust path inside the same transaction that approves it. It writes a **new reason, `refund_credit`**, with a positive delta. This requires extending three existing ledger check constraints: the allowed-reason list, the sign-per-reason rule, and the admin-actor rule (a refund credit must carry the deciding admin and a note, exactly as an administrative grant does).
- `refund_credit` carries no source column. It cannot carry the order reference because the ledger already has a unique index forbidding two rows for one order, and a purchase row owns that slot. The link back to the order lives on the refund, not on the ledger row.
- The complaint screen's existing make-good grant stays as it is. A refund is a different act: the make-good is a bounded apology, the refund is a return of value. Both may exist on one complaint.
- Ceilings are enforced at the service boundary with the same validation shape the complaint resolution already uses: when the refund is tied to an order, the cumulative money or credits refunded against that order may not exceed what was paid; when it is not tied to an order, a configured ceiling applies.
- Refunds are append-and-transition only. No delete path exists. A rejected refund stays as a rejected refund.
- The CSV export is a pure function over a list of rows, so it is unit-testable without a database or an HTTP request. It writes UTF-8 with a byte-order mark. It records an export timestamp per row but changes no status.
- The refund list and detail screens live under the existing admin area and use the existing staff permission namespace for support triage, not the admin-only one.

### Dashboard revenue

- One shared definition of "a paid order in a period" is extracted and used by both the dashboard and the orders screen. It treats the payment timestamp as the reporting date and falls back to the creation timestamp only when the payment timestamp is missing, so an order with the defective shape is counted exactly once, in exactly one period, on exactly one date.
- Day and month boundaries are computed in a fixed UTC+7 offset, held in one place, and used by every admin reporting query. The container's timezone setting must not be able to change a report.
- Net revenue for a window is paid-order amount minus money refunds whose settlement timestamp falls inside that window. Credit refunds are excluded from revenue and counted in their own figure, because no money moved.
- "Credits used this month" subtracts refunded credits. It must count the new `refund_credit` reason alongside the existing booking-cancellation reason, or the figure it already shows is wrong.
- The currency formatter gains a negative branch that renders a true minus sign. Positive amounts keep their current rendering, so no existing screen changes.
- The dashboard DTO gains a today figure and a refunded-credits figure; the existing month figure changes meaning from gross to net.

### Dashboard realtime

- No schema change and no new timestamp column is introduced for change detection. Every change the dashboard must reflect moves a countable aggregate: a paid order changes the paid count, a complaint status change moves one status bucket to another, a refund status change does the same. The revision is a small tuple of counts and sums over already-indexed columns.
- The revision is exposed as a comparable value with a single equality test, so the comparison is the module's whole contract and is testable without HTTP.
- The API exposes the revision; the web app owns the stream. The browser connects only to the web app, consistent with ADR-0002, and never to the API directly.
- The stream sends the revision and nothing else. Payload-bearing events would force the number formatting to be duplicated in client script, and two formatters drift.
- The web app fetches the dashboard fragment as HTML from its own route and replaces the metric region in place. The page is never reloaded, so scroll position, focus and any in-progress form survive, and no reveal animation replays.
- The stream re-sends periodically even when the revision is unchanged, because one alert on the dashboard is time-based ("orders waiting over 30 minutes") and must be able to appear without any row changing.
- A last-updated timestamp is rendered in the fragment, so staleness is visible. When the stream drops, an indicator changes state rather than leaving old numbers looking current.
- Because the admin reporting queries now filter by status and settlement date on tables whose current indexes do not serve those lookups, an index is added only if measured to matter. A comment records the decision so it is not rediscovered as a bug.

### Documentation

- The API gains an OpenAPI document and a browsable UI, available in every environment as requested, gated by one configuration flag so it can be switched off without a code change.
- Response schemas are explicitly out of scope: the endpoints return an untyped result union, so describing response bodies would mean touching every handler in the API. The document lists every route, its parameters, and its authentication requirement, which is what a reviewer needs to navigate and call it.
- One-click sign-in is provided by a small endpoint that mints a token for a configured demo account, and the UI is preconfigured to attach it. It is disabled by the same configuration flag. The default in the shipped configuration is on, per the owner's instruction, and the flag and its consequence are documented prominently.

### Containers

- A single multi-stage Dockerfile exposes two targets, one per web app, so the two images cannot drift apart.
- Compose brings up four things: the database, a one-shot schema-and-seed loader, the API, and the web app. The two apps start only after the loader exits successfully.
- The loader runs the schema file and the demo seed against a fresh database only. A schema change therefore requires discarding the database volume, and that instruction is documented rather than automated, because the project is database-first and has no migration engine to automate it with.
- The API's health endpoint, which already exists, is the container health check.
- The API's audio store is a named volume. It is the only writable media path; the web app serves committed assets and writes nothing.
- Unconditional HTTPS redirection is made configurable, defaulted to the current behaviour, and turned off in compose. No self-signed certificate is baked into an image.
- The payment gateway is chosen by configuration with a safe default and one documented line to switch to the real gateway, because the team must be able to both demo safely and test a real checkout.
- Secrets come from an environment file, never from a committed file. The signing key is mandatory and the API refuses to start without one, so the compose file declares it as required.

---

## Testing Decisions

A good test here asserts an externally observable outcome: a refund's status after a transition, the ledger row a credit refund produced, the number a dashboard reports for a given set of rows, whether a stream emits when nothing changed, the bytes of a CSV. It does not assert which query was issued, how many statements ran, or which private method was called. The existing suite sets the bar: database-backed tests construct a real database from the schema file per test, so triggers and check constraints are part of the evidence rather than mocked away.

**Schema harness.** The refund table's own constraints are verified as schema tests: method and amount are mutually exclusive, a settled refund is always a money refund, a status with a required actor stamp cannot be reached without it, and the extended ledger reason accepts a positive `refund_credit` while still refusing a negative one. Prior art is every existing constraint proof in the schema test suite; the guards that refuse a second ledger row for one order and that refuse a rewriting of a ledger row must keep passing.

**Refund service, against a real database.** Requesting, approving, rejecting, settling, the order ceiling, the no-order ceiling, the credit-refund posting to the ledger, and the refusal to settle a credit refund. Prior art is the complaint resolution tests: same transaction shape, same validation-error shape, same "already resolved" guard pattern.

**Dashboard figures, against a real database.** The seam is the existing dashboard service and its existing test class. Cases that must exist: a month with only income is positive; a month whose refunds exceed its income is negative; a credit refund does not change revenue; a money refund lands in the settlement day, not the request day; a paid order at 23:30 Vietnam time belongs to that day and not the next; the dashboard and the orders screen report the same paid total for the same seeded rows.

**Live revision, against a real database.** The seam is a new revision provider, tested for: unchanged when nothing changed, changed by a new paid order, changed by a complaint moving between two statuses, changed by a refund being settled.

**Live stream and fragment, in the web app test project.** The web app's stream is driven by a stub revision source so the test can control change without a database and without sleeping. Cases: no event when the revision does not change; one event when it changes; the fragment route returns the metric region and not a full page; the fragment renders a negative amount as a negative amount. Prior art is the existing admin controller tests, which drive controllers with a stubbed API client.

**Currency formatting.** A unit test for the negative branch, because the true-minus-sign decision is invisible to every other test.

**CSV.** A unit test over the pure formatter: header order, a Vietnamese name and a reason with diacritics survive, a money refund and a credit refund each fill their own column and leave the other empty, and the output begins with a byte-order mark.

**Swagger and compose.** Neither gets a unit test. The evidence is the thing itself: the OpenAPI document is fetched and checked for a non-zero route count, and the compose stack is brought up and walked through the dashboard and a refund. This is the same standard the project already applies to browser journeys.

**Not tested:** the web app's stream route is not asserted for its heartbeat cadence or its reconnection behaviour; those are browser concerns and are walked manually.

---

## Out of Scope

- Response schemas in the OpenAPI document. Every route is listed; response bodies are not described. Noted deliberately, not an oversight.
- Any real payment gateway integration for refunds. Money leaves the building by bank transfer performed by a human; the product records it and exports it.
- Partial-refund authoring beyond what the ceiling check enforces, and multi-step approval workflows beyond requested, approved, settled, rejected.
- Separating support and accounting into distinct roles. The actor stamps are recorded so the split can happen later without repairing data.
- A learner-facing view of their own refunds.
- Chat, attachments, or threads on complaints.
- Any change to the make-good credit grant on the complaint screen beyond counting its new reason on the dashboard.
- Migrations. The project is database-first; the container loader recreates a database, it does not upgrade one.
- Windows service hosting, IIS configuration, or a production TLS story. Compose is for a developer's machine and for review.

---

## Further Notes

**The realtime decision deliberately avoids a schema change.** The obvious design is an `UpdatedAt` column on the mutable tables, maintained by a trigger. That was rejected: it touches the two hottest tables in the domain, needs a trigger nesting guard to avoid self-recursion, needs trigger declarations in the model mapping to avoid an output-clause save failure, and introduces a column that must then be kept correct forever. Counting a handful of indexed aggregates is cheaper to build, cheaper to keep correct, and honest about its one real limit, which is recorded below.

**The known limit of the revision approach.** An edit that changes neither a count nor a sum is invisible to the revision. On the dashboard that is acceptable, because the dashboard shows counts and sums. It must be written down, because the same mechanism would be wrong for a screen showing free text.

**A defect this work removes.** The dashboard and the orders screen currently disagree about which paid orders belong to the month. Whatever else is decided, they must not disagree after this work, and a test pins that.

**The security consequence of the owner's choices.** The documentation UI is enabled in every environment, and it signs in automatically as a demo account. That is a complete back door if this is ever deployed to the internet. The configuration flag is the off switch, it is documented in the deployment notes, and it is called out in the pull request.

**Why the payment gateway defaults to the mock in containers.** Two instructions pull in opposite directions: a teammate must be able to run the stack immediately, and the team must be able to test a real checkout. One line in the environment file resolves it, and the default is the one that cannot charge anyone.
