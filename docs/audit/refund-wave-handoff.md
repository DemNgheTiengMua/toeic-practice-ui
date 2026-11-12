# Refund wave — handoff

Nine tickets (#279–#287), all closed, on `integ/cefr-rebuild`.
Spec: `docs/specs/2026-11-12-refunds-realtime-dashboard-swagger-docker.md`.
Screenshots to audit: `docs/audit/screenshots/full-audit/` (see its README).

## Final state

| Suite | Result |
|---|---|
| `Services.Tests` | 1998 pass, 0 fail, 2 skip |
| `WebMVC.Tests` | 1078 pass, 0 fail |
| `WebAPI.Tests` | 124 pass, 0 fail |
| Schema harness | 61/61, `ALL CHECKS PASSED` |
| Browser campaign | 7/7, 91 images across 5 roles |

The 2 skips are content debt, not this work: `SeedFeedsStageTwoTests` and
`PlacementDrawOnTheShippedSeedTests` wait for per-band item volume the seed does
not ship (#275, #278).

## What each ticket delivered

| # | Result |
|---|---|
| 279 | `ReportingPeriod` — one definition of "a paid order in a period", cut at a fixed UTC+7. The dashboard and the orders screen no longer disagree. |
| 280 | `Refunds` table, 8 constraints, 2 triggers, and a `refund_credit` ledger reason. Append-only. |
| 281 | Credit refund: request → approve (posts credits immediately) / reject, with a cumulative order ceiling and an orderless ceiling (`Refunds:OrderlessCeiling`, default 200). |
| 282 | Money refund: request → approve → **settle**. Settlement is when money counts. Ceiling is what the order was paid. |
| 283 | CSV export of settled money refunds, UTF-8 BOM, RFC 4180 quoting, selected on settlement date. |
| 284 | Net revenue today and this month, may be negative, Sign Red with U+2212. Credits refunded is its own figure. |
| 285 | OpenAPI + Scalar + one-click demo sign-in behind `OpenApi:Enabled`. |
| 286 | Live dashboard: a revision of counts and sums, SSE carrying only a change token, fragment swap. |
| 287 | `docker compose up` — 4 services, 2 volumes, healthcheck, `docs/DOCKER.md`. |

## Defects found by running it, not by reading it

These are the ones worth knowing about, because a test suite stayed green through
all of them.

1. **`CurrencyFormatter` abbreviated money.** `20,249,999 ₫` rendered as `20.2M ₫` —
   49,999 short — and that was the revenue figure on the dashboard. Fixed: every
   amount is now exact, sub-dong amounts keep their two stored places, and a
   negative renders U+2212 rather than an ASCII hyphen. Three writes also cast
   `decimal` to `long`, so opening a package's edit form and saving rewrote
   199,000.50 as 199,000. All fixed, with a test that every stored `DECIMAL(12,2)`
   survives format-then-reparse unchanged.
2. **The SSE stream wrote nothing for thirty seconds.** `#286` treated the first
   reading purely as a baseline, so the response opened with zero bytes and the
   browser never dispatched `open`. Every freshly loaded dashboard said
   "Connecting…" for half a minute on a healthy stream. Fixed with a comment frame
   before the first reading; verified in the browser (badge reads "Live" in 2.5 s).
   **This was found by looking at a screenshot**, which is why the campaign exists.
3. **The refusal page told an admin to sign in as an administrator.** The sentence
   was hard-coded for one direction of a two-direction boundary. Fixed to say what
   is true in both.
4. **The containers omitted every served asset.** `.dockerignore` dropped 11 exam
   audio tracks and the favicon from the image. Also: `down -v` after an
   interrupted load left orphan `.mdf` files, and `.gitignore` swallowed
   `.env.docker.example`.
5. **The monthly credit figures had no upper bound.** A start-only window means
   next month's rows join this month's total once the clock crosses. Nobody
   notices a figure that is merely too large.
6. **Seven source files were not clean UTF-8**, so the guard that checks every file
   was red on this branch and had been for weeks. Six BOMs stripped, one
   Windows-1252 em-dash re-encoded.

## Decisions recorded so they are not re-litigated

- **ADR-0013** — no `UpdatedAt`, no `rowversion`. Change detection is counts and
  sums. Its blind spot (an edit changing neither) is deliberate and is correct for
  a surface showing only counts and sums.
- **ADR-0014** — revenue is net and may be negative; a money refund lands on its
  **settlement** date; reporting days are cut at UTC+7, never the container's
  timezone.
- **The orders screen stays gross**, labelled "Paid receipts", because an order
  list is what the gateway took and subtracting refunds it does not display would
  make a row-sum disagree with the total. The dashboard says "Net revenue".

## Security, stated plainly

`OpenApi:Enabled` defaults to **on**, and it exposes `POST /api/auth/demo-sign-in`,
which mints a full admin token with no credentials. That is a complete back door
on the internet. It is documented in `docs/DOCKER.md`, `docs/MANUAL-TEST-CREDENTIALS.md`
and the class doc comment. **Turn it off before any deployment reachable from
outside.**

## Known limits, not defects

- The campaign captures the screens around a sitting (instructions, history,
  result, review) but does not open one, because that mutates seeded state and
  would make the next campaign photograph a different build.
- `/admin/audit` has no parameterless route; the queue is scoped to a batch and the
  seed ships none. `/admin/orders/{id}/detail` is JSON for script use; the screen
  is `/admin/orders?detailId=N`.
- The live revision costs one grouped query per connected admin per 30 s tick.
  Fine for a handful of staff, unmeasured beyond that; raise
  `DashboardLiveOptions` before adding machinery.
- `UserCellMasteries`-style derived facts are untouched by this wave. ADR-0012
  still governs them.
