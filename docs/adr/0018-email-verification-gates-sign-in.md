# ADR-0018: Email verification gates sign-in

**Status:** Accepted (2026-11-15). Supersedes the sentence in
`src/BusinessObjects/Auth/AuthUser.cs` that reads *"`EmailVerified` gates buying credits, never the
free loop"*, and reverses `AccountEmailTests.Unverified_learner_can_sign_in`.

## Context

Before this decision the product verified email addresses lazily. `AuthUser.EmailVerified` was
described as a gate on checkout only, and `AuthService.SignInAsync` never read `EmailVerifiedAt`: an
unverified account signed in normally and hit `403 AUTH_EMAIL_UNVERIFIED` the moment it tried to buy
credits (`PaymentService.AssertUserEmailVerifiedAsync`).

That was a deliberate earlier choice, and it had one real virtue: a learner could practise
immediately and was only asked to prove the mailbox when money entered the picture.

It also had three problems that only showed up once real mail delivery was added:

1. **`PRODUCT.md:253` already claimed the stricter rule** — *"email must be verified (activation
   tokens stored hashed, re-sendable by admin)"* — so documentation and behaviour disagreed, and the
   code was the one that was wrong.
2. **Unverified accounts accumulate unowned.** Every signup that used a typo or a throwaway address
   became a live session that could burn AI grading quota, occupy mentor slots, and appear in admin
   dashboards as a real learner.
3. **Nothing in the product needs a session before verification.** Practice, placement and the free
   loop all require a certificate and, in the seeded flow, a verified account anyway. The "verify
   later" window was buying almost nothing.

## Decision

**A verified mailbox is a precondition of holding a session, not a limitation on what the session
may do.**

Concretely:

- `AuthService.SignInAsync` throws `AuthException.EmailUnverified` when `EmailVerifiedAt` is null.
  The check sits **after** password verification, so a wrong password still answers
  `AUTH_INVALID_CREDENTIALS` and the gate cannot be used to enumerate which addresses exist.
  `LastLoginAt` is not written, because nothing was logged in.
- `POST /api/auth/sign-in` maps that to **403** with `AUTH_EMAIL_UNVERIFIED` (not 401: the
  credentials were right, the mailbox is unproven). The SPA routes to `/auth/check-email`.
- `POST /api/auth/register` returns **201 with no token and no cookie**. Handing back a credential
  for an account that cannot use it would be a lie the client has to discover later.
- `POST /api/auth/resend-activation` is **unauthenticated** and takes an address instead of a
  session. This is forced by the gate: an unverified account has no token with which to ask for a
  link, so requiring one would make a lost activation mail unrecoverable. It mirrors
  `forgot-password` exactly — always `204`, a failed send is logged not raised, unknown and
  already-verified addresses are indistinguishable.
- `POST /api/admin/users/{id}/verify-email` is the escape route. A verification gate plus a
  misconfigured relay locks every new account out with no fix short of editing the database.
- No role is exempt. A second rule for staff would be a branch every future reader has to reason
  about, and the seeded `admin@demo.local` is verified anyway.

## Consequences

**Good.** Code matches `PRODUCT.md`. Unowned accounts can no longer consume AI quota or appear as
learner activity. The free loop never silently degrades into a 403 at checkout, which was a
genuinely confusing failure: the learner had a session, could navigate, and was ambushed by the pay
wall.

**Costs, accepted knowingly.**

- A learner who mistypes their address at signup cannot get in and cannot self-serve a fix. Admin
  `verify-email` and re-registration are the two exits. This is the price of the gate.
- Every account that existed before this change without `EmailVerifiedAt` stops being able to sign
  in. Dev databases need `UPDATE Users SET EmailVerifiedAt = SYSDATETIMEOFFSET() WHERE
  EmailVerifiedAt IS NULL` (documented in `docs/DOCKER-SETUP-VI.md`); there is no migration, the
  product is database-first and pre-launch.
- The two UI banners that said *"verify your email to unlock credit purchases"* are now dead code
  and were deleted rather than reworded: they described the old model.

## Alternatives rejected

- **Verified-only routes, session still granted** (403 on ~40 business endpoints). More code, one
  more place per endpoint to forget, and the SPA would render an authenticated shell it cannot use.
- **Grace period** (N days to verify). Needs a config value, a sweep job, and a second meaning for
  `EmailVerified`, for a window in which the account can still burn paid quota.
- **CSV or SQLite mail drop instead of SMTP.** Not a decision about the gate, but adjacent: the
  transport is now selected by `Email:Transport` so tests keep the folder drop and the container
  stack sends real mail (`SmtpEmailSender`, BCL `SmtpClient`, no new package).
