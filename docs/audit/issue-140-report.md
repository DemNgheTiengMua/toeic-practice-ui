# #140 — Degraded pages take ~12 s when the API is down

**Status:** done, uncommitted. **Verified:** 1347/1347 unit + integration, 6 new, manual browser pass against a genuinely killed API.

---

## What changed

All on the MVC side, inside the typed API client's HTTP pipeline. The API is untouched.

| File | What |
|---|---|
| `src/WebMVC/Infrastructure/ApiResilienceHandler.cs` | **new.** Fail-fast per request, breaker gate, connect/header budgets, log on outage enter/leave. |
| `src/WebMVC/Infrastructure/ApiCircuitBreaker.cs` | **new.** Process-wide closed → open → half-open, one probe. |
| `src/WebMVC/Infrastructure/ApiResilienceOptions.cs` | **new.** Budgets from `ApiResilience:*` config. |
| `src/WebMVC/Program.cs` | Handler wired outermost; `SocketsHttpHandler.ConnectTimeout` on the primary handler. |
| `src/WebMVC/appsettings.json` | `ApiResilience` section with defaults. |
| `tests/WebMVC.Tests/ApiResilienceTests.cs` | **new.** 6 cases at the real-host seam. |

Three layers, cheapest first:

1. **Per request.** The first transport fault stamps `HttpContext.Items`; every later API call in that same request throws immediately. This alone is what turns three 4 s failures into one — the ticket's core requirement.
2. **Across requests.** A shared breaker opens after 3 consecutive transport faults, then refuses for 10 s, then lets exactly one probe through. Closes on the first success.
3. **Per phase.** `ConnectTimeout` 2 s caps the refused-connection case; a 30 s header timer caps an API that accepts but never answers. Response bodies are never timed out — every streaming call already uses `ResponseHeadersRead`.

Every rejection surfaces as a plain `HttpRequestException`, never an `ApiException`. That is why no controller changed: the existing `catch (HttpRequestException ex) when (ex is not ApiException { ... })` arms in `ProfileController`, `LearnerControllerBase`, `TimeZoneMiddleware` and 12 others absorb the fail-fast path unchanged, and `AUTH_LOCKED` / `AUTH_REQUIRED` still reach the sign-out middleware.

No dependency added — hand-written, per your decision.

## Measured, API process actually killed

`scripts/seed-manual-test.sh fresh`, signed in as `learner@demo.local`, then `Stop-Process` on the pid listening on 5001.

| | Before (#140) | After |
|---|---|---|
| First degraded page | ~12 s | **2.1 s** |
| Second page, same outage | ~12 s | 2.1 s (breaker still filling) |
| Third page | ~12 s | **0.10 s** (breaker open) |
| Further pages while open | ~12 s | **0.07 s** |
| After API restart, no MVC restart | — | **0.7 s, normal render** |
| Healthy page, API up | 175 ms | 175 ms (unchanged) |

Screenshot: `docs/audit/issue-140-degraded-profile.png` — degraded view, session intact, design system intact.

## Two defects found by doing this, both fixed

1. **Misleading message.** The connect timeout surfaces as a `TaskCanceledException`, so my first version logged "The API did not answer within 30s" when it had actually given up after 2 s on the connect phase. Anyone paged at 3 a.m. would have chased the wrong budget. The message now names whichever phase actually expired.

2. **Handler state.** HttpClient pools and reuses handler instances across requests, so a fault flag stored on the handler would have leaked one request's outage into the next. The marker lives on `HttpContext.Items` instead. My first draft had this wrong.

## Test coverage (6, all green)

Drives the real host — real middleware, real cookie scheme, real typed client and its real pipeline — with only the socket replaced. A fake clock makes the break window deterministic.

1. Degraded page renders fast, session survives, **exactly one** upstream attempt.
2. Once the breaker opens, later pages make **zero** attempts.
3. After the break window and an answering API, pages render normally again.
4. A hanging API is cut off at the header budget.
5. An error answer from a *live* API never opens the breaker (12 attempts across 4 pages).
6. `AUTH_LOCKED` still signs the user out.

## Two things for you to decide

1. **Not committed.** Working tree is clean except this ticket's files; the transcript's user instruction was "commit lên remote đã" — I have not pushed. Say go and it lands as one commit.
2. **The ticket asked to check Context7 for the resilience library's API before writing it.** I did query Context7 (ASP.NET Core docs) and it confirmed the `SocketsHttpHandler.ConnectTimeout` shape and that factory handlers are pooled. I did not consult a resilience *library* because you chose to write it by hand.

Then #146 is next, in a fresh session, per your sequencing.

---

**Skipped:** did not test the 100 s no-timeout scenario against a real silent-accept API (the fake covers it); did not measure against a remote/slow API, only loopback. **Risk:** the header budget is 30 s — if a legitimate admin export on a slow link ever takes longer *to first byte*, it will now be cut off where before it would have waited.
