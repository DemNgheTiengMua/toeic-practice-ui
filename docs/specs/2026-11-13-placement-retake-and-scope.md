# Placement, paid on retake, with a skill count the learner chooses

**Status:** ready-for-agent
**ADRs touched:** ADR-0001 (database-first), ADR-0012 (one writer per derived fact)

---

## Problem Statement

Placement is free, forever, for everyone. That is written into the product's own rules: the free
diagnostic loop — practice, weakness analysis, placement — is "completely free and never writes a ledger
row", and a test pins that placement performs no balance read on its path.

The consequence is that the loop has no cost at all. A learner who dislikes their band can sit the
placement again immediately, and again, and the product pays for the grading either way. There is no
reason to think about the result, and nothing that makes a second attempt a decision rather than a click.

There is a second problem on the same screen. Every certificate has exactly one seeded placement paper
and that paper decides how many skills are examined. The learner has no say. Someone who wants only a
reading estimate sits a four-skill paper, answers three skills they did not want, and waits for grading
they did not ask for. Someone preparing for a two-skill certificate and a four-skill one cannot tell the
two apart on the page.

The screen also offers the wrong verb. A learner who already has a band sees "Start placement test",
which reads as though nothing has happened yet.

---

## Solution

**The first placement is free. Every placement after that costs credits.**

**The learner chooses how many skills to measure: all of them, or the objective pair only.**

One text-only placement paper per certificate covers all four skills. The learner picks a scope before
the paper starts:

- **All four skills** — Listening, Reading, Speaking, Writing — costs **5 credits** on a retake.
- **Listening and Reading only** — costs **3 credits** on a retake.

The scope is not a filter applied afterwards. Choosing two skills **removes** the other two from the
sitting: they are not served, not answered, not graded, and not reported. The learner is told this before
they confirm, in the words that matter: the skills they drop are not measured this time, and their
previous measurement for those skills is untouched.

On the placement screen, a learner with no result yet sees **Start placement test** and pays nothing. A
learner who already has a band sees **Retake placement test**, and the click opens a confirmation screen
that names the two scopes, the price of each, and what happens to the skills that are left out.

---

## User Stories

### The free first attempt

1. As a learner who has never taken placement, I want to start it without paying, so that the price never stands between me and knowing my level.
2. As a learner, I want the first attempt to say clearly that it is free, so that I am not surprised by a charge.
3. As a learner on my first attempt, I want to choose how many skills to measure, so that the choice is mine even when it costs nothing.

### Retakes cost

4. As a learner who already has a band, I want the button to say "Retake placement test", so that I understand this is not my first time.
5. As a learner, I want a confirmation screen before a paid retake, so that I cannot spend credits by mis-clicking.
6. As a learner, I want to see the exact price of each scope before I confirm, so that I can decide with the number in front of me.
7. As a learner, I want the retake to cost 3 credits when I measure two skills and 5 when I measure four, so that measuring less costs less.
8. As a learner, I want to be told my credit balance is insufficient before anything starts, so that I am not dropped into a paper I cannot pay for.
9. As a learner with too few credits, I want a route to buy more, so that the refusal is not a dead end.
10. As a learner, I want the charge to appear in my wallet as its own line, so that I can see what I paid for.
11. As a learner, I want the ledger line to name the scope I chose, so that two retakes at different prices are distinguishable.
12. As a learner, I want a retake I abandon to still have charged me, so that the price reflects the grading work that was committed.

### The scope choice

13. As a learner, I want to choose all four skills, so that I get a complete picture of my level.
14. As a learner, I want to choose Listening and Reading only, so that I get the objective estimate I actually came for.
15. As a learner, I want the confirmation screen to name the skills I am leaving out, so that the choice is explicit rather than implied by a number.
16. As a learner, I want to be told that a skill I leave out is not measured this time, so that I do not read its absence as a zero.
17. As a learner, I want to be told that leaving a skill out does not erase my previous measurement of it, so that choosing a smaller scope feels safe.
18. As a learner, I want the two-skill paper to end after Reading, so that I am not walked into speaking tasks I declined.
19. As a learner, I want the result screen to show only the skills I chose, so that it does not present a partial sitting as a whole one.

### The paper

20. As a learner, I want the placement paper to be the same paper whichever scope I choose, so that a two-skill result and a four-skill result come from the same questions.
21. As a learner, I want the four-skill paper to cover Listening, Reading, Speaking and Writing, so that "all four" means what it says.
22. As a learner on a two-skill sitting, I want the speaking and writing content absent entirely, so that the paper is shorter rather than partly dead.

### What the result reports

23. As a learner who chose two skills, I want my result to report exactly two skills, so that the page reflects what I sat.
24. As a learner who chose four, I want my result to report all four.
25. As a learner, I want my overall level to keep its current meaning — the weakest measured skill — so that choosing a narrower scope changes which skills are considered, not how the level is computed.
26. As a learner, I want a previously measured skill that I did not sit this time to keep its stored level, so that a smaller retake does not wipe evidence.

### Administration

27. As an administrator, I want to see whether a placement was paid and what scope it used, so that I can answer a billing question without reading logs.
28. As an administrator, I want the price of each scope to be a configuration value, so that changing it is not a code change.

---

## Implementation Decisions

### The rule change

The product rule "the free diagnostic loop is completely free and never writes a ledger row" becomes:
**the first placement per certificate per learner is free and writes no ledger row; every subsequent
placement charges.** The rule document and the test that pins the old behaviour must both change in the
same ticket, because a test asserting the old rule passing beside code implementing the new one is a lie
either way round.

Practice and weakness analysis stay free. Only placement acquires a price, and only after the first.

### Scope is a property of the sitting, not a filter on the result

The chosen scope is recorded on the placement and on its attempt. It determines what the sitting serves:
the sections outside the scope are never bound into the attempt's paper, so nothing downstream — the take
view, the navigator, the submission, the grading queue, the result writer — has to learn a new concept.
A skill that is not on the paper simply is not there, which is the same path a certificate with two
skills already takes today.

Two scopes are offered: **all four skills**, and **Listening and Reading only**. The objective pair is
offered because it is the pair a placement can actually measure by drawing items; speaking and writing
are graded by rubric and cost real grading work, which is why they carry the higher price.

### Pricing is per scope and configured

Two configuration values, one per scope. The service reads them; no price is hard-coded in a screen. The
charge is posted through the existing credit ledger as a **new reason, `placement_retake`**, which
requires extending the ledger's reason check constraint, its sign rule (negative), and its source rule
(no order, no attempt, no booking). This is a schema change and follows the database-first rule: the
schema file is edited and validated first, then the model is re-scaffolded.

The charge happens **once, at the start of the paid sitting**, inside the same transaction that creates
the placement. A retake that is abandoned, times out, or fails grading is still charged: the grading work
and the item draw were committed at start. This is stated on the confirmation screen rather than left to
be discovered.

The retake cooldown and the sitting lifetime are **not changed by this work**. The existing rule — 24
hours after a finished sitting, waived only when the previous sitting measured nothing because every
skill's grading failed — applies to a paid retake exactly as it applies to a free one. A learner who
walks away from a paid sitting is charged, waits the same window, and resumes within the same 48-hour
inactivity / 72-hour absolute lifetime as before.

### The cooldown governs, and it already exists

A retake is not free-form. The existing rule applies unchanged:

> **24 hours after a finished sitting**, measured from the most recent one only. There is no lifetime
> cap — a tenth placement is treated like a second.

The window is waived in exactly one case: **the previous sitting measured nothing because every skill's
grading failed.** That is a system failure, not a learner choice.

A learner who **skipped** their tasks does not get the waiver. A learner whose sitting **timed out** does
not get it either — from the stored row, a timer that fired during an outage is indistinguishable from a
learner who walked away, and waiving on it hands back the reroll the cooldown exists to prevent.

### What happens when a learner leaves a paid sitting

Leaving mid-sitting does not reverse the charge and does not shorten the cooldown.

- **Closing the tab, going offline, or navigating away** — the sitting stays in progress. It resumes when
  they come back, for up to **48 hours since the last write**, capped at **72 hours since the start**.
  This is the existing placement lifetime, unchanged.
- **Not coming back** — the sitting expires on that cap. It counts as the most recent finished sitting
  for the cooldown, so the next retake waits the usual 24 hours from that expiry. The charge stands.
- **Abandoning by starting another sitting** — the older in-progress sitting is marked abandoned and its
  attempt with it. The charge for the abandoned one stands.
- **Starting a retake while one is still in progress** — the resume path wins. A learner who returns to a
  sitting they already paid for is sent back into it, not charged again.

The rule in one sentence: **the charge is for the sitting, the cooldown is for the result.** Everything
that ends a sitting without a result still costs, and only a system-side grading failure buys the early
retake.

This is deliberately the same behaviour a retake has today, minus the price. The new work is the price,
not a second lifecycle.

### Idempotence: a retake must not double-charge

A double-clicked confirmation, a retried request, or a re-entered URL must produce one charge and one
sitting. The existing pattern for this is a uniqueness guarantee in the database plus a service-level
guard inside a serializable transaction, which is what the refund lifecycle already does. Reuse that
shape rather than inventing a second one.

### The screen

The placement screen distinguishes three states:

- **Never sat** — "Start placement test", free, no confirmation step.
- **Sat before** — "Retake placement test", opens the confirmation screen.
- **In progress** — the existing resume behaviour.

The confirmation screen states the two scopes as a choice, the price beside each, the skills each leaves
out, that the left-out skills are not measured and their previous measurement survives, and that the
charge is made at start rather than at submission. It refuses to proceed when the balance is short, and
offers the pricing page instead.

### The result

The result reports the skills the sitting measured. A two-skill result reports two rows and says the
sitting covered two skills; it does not render four rows with two marked absent, because nothing was
attempted and nothing is missing.

The overall level keeps its current meaning. A stricter reading is available but is a separate decision,
and this spec does not take it.

---

## Testing Decisions

A good test asserts what a learner or an operator can observe: the price charged, the balance afterwards,
the ledger row written, the skills the paper served, the skills the result reports, whether a second
click charged twice. It does not assert which query ran or how many round trips a service made.

**The seating seam.** Existing placement service tests already build a real database from the schema file
per test and start sittings through the service. Retake pricing, scope, and the double-charge guard all
belong there: one test per rule, driven through the service entry point.

**The ledger seam.** The existing credit-ledger tests cover every write path and assert that balance
equals the ledger sum. `placement_retake` joins them: a negative row, an admin actor absent, a source
column absent, and the balance after it.

**The schema harness.** The extended reason constraint gets a constraint proof in the schema test set —
the new reason accepted with a negative delta, refused with a positive one, and refused when it names a
source. Prior art is every other ledger-reason proof in that directory.

**The screen seam.** The MVC admin and learner controller tests already drive controllers with a stubbed
API client. The three placement states and the confirmation screen's refusal-on-short-balance are tested
there. Prior art is the placement controller tests that already cover the take and result screens.

**Not tested.** The exact copy on the confirmation screen. It is content, and pinning it makes every
wording improvement a test change.

---

## Out of Scope

- Any price on practice or weakness analysis. The free diagnostic loop keeps them free; only placement
  acquires a price, and only after the first.
- Charging per skill beyond the two offered scopes. Three-skill scopes, single-skill scopes, and a
  la carte pricing are not in this work.
- Refunding a paid retake. A retake is charged at start and is not reversible; a learner who wants their
  credits back is a refund, which is its own feature with its own approval lifecycle.
- A stricter definition of the overall level that excludes skills never measured in any sitting.
- Making the placement paper differ between scopes beyond omitting sections. The same questions are
  served; a two-skill sitting simply stops earlier.
- Free retakes after a set period, promotional credits, or a subscription. The price is a flat
  configuration value per scope.
- Changing what speaking and writing cost elsewhere — AI grading keeps its existing charge.

---

## Further Notes

**Why the objective pair is its own scope rather than a size.** Listening and Reading are measured by
drawing items and counting correct answers; speaking and writing are graded by rubric, and each graded
task costs real work. Offering "two skills" as a cheaper tier is honest precisely because the cheap tier
is the cheap half to grade. A future three-skill scope would have to argue which side of that line it
falls on.

**One paper, two scopes, and why that is not a contradiction.** The paper carries all four skills. A
two-skill sitting serves only the objective sections — the productive ones are not bound into the
attempt at all. This is the same mechanism a certificate with only two skills already uses, which is why
it needs no new concept downstream.

**The invariant this changes is written down, and will be cited back.** The rules document states the
diagnostic loop is free and writes no ledger row. That sentence must be corrected in the same change as
the code, with the reason: a first attempt is free because it establishes the starting point a learner
cannot otherwise obtain, and a retake is paid because it re-does work the product already did at that
learner's request.

**A first attempt is free even though it costs the product grading.** That asymmetry is deliberate. The
first measurement is what makes every other feature of the product usable — the path, the practice pool,
the weakness views all read it. Charging for it would price the product's own prerequisite.

**The scope must be visible on the confirmation screen as a choice, not a default with an escape.** A
pre-selected "four skills" with a smaller option below it reads as the recommended path, and the larger
price as the norm. Neither is true; the two are a choice between a complete picture and a cheap one.
