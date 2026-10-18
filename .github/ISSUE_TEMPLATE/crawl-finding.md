---
name: Crawl finding
about: Defect found by the cross-role crawl (docs/specs/exhaustive-cross-role-testing-and-triage.md)
title: "[CRASH|LOGIC|REDUNDANCY] Loop N: <short description>"
labels: bug
---

## Summary

<!-- One or two sentences: what breaks, for whom, how bad. -->

**Category:** CRASH | LOGIC | REDUNDANCY
**Loop / flow:** Loop N — <flow name>
**Role(s):** learner | admin | both

## Reproduce

Environment: `scripts/crawl-env.sh fresh` (database `ExtraEfficiencyCrawl`, mock payment, fake AI).
Accounts use password `Demo1234!`.

1. Sign in as `learner@demo.local`
2. Go to `http://localhost:5000/...`
3. ...

## Expected

<!-- What should happen, and where that is written down (PRODUCT.md, CONTEXT.md, docs/adr/…, a trigger in docs/database-schema.sql). -->

## Actual

<!-- What happens: HTTP status, the message shown, the wrong number/state. -->

## Evidence

`scripts/crawl-env.sh errors` (trimmed to the relevant entry):

```text
fail: ...
```

Database state, if relevant:

```sql
-- query run against ExtraEfficiencyCrawl and its result
```

## Where to look

<!-- Suspected files and methods, from the stack trace or graphify. These are leads, not a diagnosis. -->

- `src/...`

## Invariants that must hold

<!-- Rules from AGENTS.md §1 this fix touches, e.g. ledger is append-only, block before you charge, HasTrigger, CertId isolation. -->

## Acceptance criteria

- [ ] A test in `tests/Services.Tests` (or `tests/WebMVC.Tests`) reproduces this and fails before the fix
- [ ] That test and the full `dotnet test` pass after the fix
- [ ] Reproduction steps above, re-walked after `scripts/crawl-env.sh fresh`, behave as Expected
- [ ] `scripts/crawl-env.sh errors` shows no new `fail:` entry for this flow
- [ ] PR says `Fixes #<this issue>` and explains the root cause

See `AGENTS.md` §7 "Fixing a crawl-finding issue".
