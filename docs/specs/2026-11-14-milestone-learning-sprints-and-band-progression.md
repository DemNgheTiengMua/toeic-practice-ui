# Milestone Learning Sprints, Self-Paced Progression, and Enterprise PayOS Embedded Checkout

**Status:** ready-for-agent
**Date:** 2026-11-14
**ADRs touched:** ADR-0001 (database-first EF Core), ADR-0003 (orders & payment gateways), ADR-0015 (React + HeroUI v3 SPA), ADR-0017 (simplify exams, placement, learning path, and AI grading)

```[tasklist]
### Implementation Tasks
- [ ] #316
- [ ] #317
- [ ] #318
- [ ] #319
```

---

## Problem Statement

The platform currently operates with two interrelated experience and architectural bottlenecks that limit enterprise maturity, financial flexibility, and learner momentum:

### 1. Pedagogical Ambiguities in Learning Path Sprints & Progression
- **Multi-Band Long-Term Goal Disconnect**: When a learner sets an ambitious target (e.g., TOEIC 450 to 800+ or IELTS 5.0 to 7.5), a single 4-week path attempts to cover an impossible span in one month, leaving the learner stranded without consecutive progressive milestones.
- **Inactivity Fear vs. Calendar Expiration**: Learners who invest credits in a 4-week path worry that real-world interruptions will cause their paid curriculum to expire after 28 calendar days, causing them to forfeit credits and progress.
- **Artificial Calendar Pacing**: Highly motivated learners who complete a week in 2 days are blocked from accelerating if subsequent weeks are locked by wall-clock dates.
- **Unverified Band Upgrade & Missing Exit Milestone**: Completing Week 4 finishes the curriculum but does not promote `CurrentBandCode`. The platform lacks a celebratory milestone guiding the learner to sit an Exit Mock Exam (or Placement Retake) to officially certify and display their new band.

### 2. Under-developed Credit Purchasing & Pop-Out Payment Flow
- **Rigid Package Buckets vs. Exact Need**: Currently, learners must buy rigid, pre-defined packages (5, 10, 20 credits). If a learner needs exactly 3 credits for mentor sessions or 5 credits for a path, they cannot simply specify the exact credit quantity.
- **Fragmented Pop-Out Tab Experience**: When initiating PayOS payment, the application opens an external browser tab to a hosted web page via an unstyled external link. Learners leave the application context, risking lost tabs, uncaptured callbacks, and broken mobile/browser flows.
- **Pending Order Deadlocks**: A unique constraint allows only one pending order per user. If a learner starts checkout, changes their mind about the credit amount, and returns to checkout, the system refuses with an order conflict error rather than seamlessly superseding or cancelling the un-paid order.
- **Lack of Context-Aware Return**: When a learner is gated by insufficient credits on a feature page, there is no automated flow that pre-selects the needed credits on checkout and redirects back to the original activity upon payment completion.

---

## Solution

Unify the **Milestone Learning Sprint** and **Enterprise PayOS Embedded Checkout** into a seamless, robust, enterprise-grade architecture:

### 1. Progressive Milestone Sprints with Zero Expiration
- **Multi-Band Slicing**: When a learner's goal spans multiple bands, the path generator slices the journey into 4-week Sprints targeting the immediate next achievable band (`SprintTargetBand`, e.g., 450 $\rightarrow$ 550).
- **Self-Paced & Non-Expiring ("No Aggressive Expiry")**: A paid sprint belongs to the learner indefinitely. Inactivity never expires or deletes a path. Completing Week $N$ immediately unlocks Week $N+1$ without calendar waiting.
- **Sprint Completion Milestone & Exit Mock Exam**: Finishing Week 4 sets status to completed, unlocks permanent free practice for those sections, and renders an interactive celebration card with a direct Call-To-Action (CTA): **"Take Exit Mock Exam"**.
- **Automatic Band Upgrade**: Scoring at the target band on the Exit Mock Exam automatically promotes `CurrentBandCode` across the Dashboard, Profile, and Goal Standing because the mock exam carries a newer timestamp than the initial placement test.
- **Next Sprint Continuity**: Completing a sprint enables purchasing Sprint 2 (5 credits) towards the next band level, inheriting all verified capabilities.

### 2. Enterprise PayOS Embedded Checkout & Flexible Credit Slider
- **Flat Conversion Rate**: A simple, transparent, uniform rate of **20,000 VND = 1 Credit**.
- **Interactive Credit Slider & Custom Input**:
  - The checkout UI provides a fluid slider (1 to 100 credits) paired with a numeric input field, allowing learners to purchase any exact credit count.
  - Price is calculated dynamically: Total Amount = Credits $\times$ 20,000 VND.
  - Quick Preset Chips (5, 10, 20 credits) allow instant selection.
- **In-App PayOS Embedded Form**:
  - Replaces external tab pop-outs with an in-app embedded VietQR checkout container using the official PayOS React SDK embedded hook.
  - Displays dynamic VietQR code, bank transfer details, countdown timer, and live status within a focused modal/card.
- **Automatic Order Superseding & Cancellation Lifecycle**:
  - Selecting a new credit quantity automatically cancels any existing un-paid pending order for that user, releasing the pending order lock.
  - When the learner clicks "Cancel" or closes the PayOS modal, the client triggers order cancellation in the background to free the lock.
- **Context-Aware Return Flow**:
  - Gated learning actions redirect to checkout with pre-filled credits and a return path parameter.
  - The checkout slider automatically pre-selects the requested credit amount.
  - Upon successful payment (client success callback or webhook settlement), the application displays a confirmation message and automatically navigates back to the return path.
- **Webhook as Single Source of Truth**:
  - Only cryptographically verified PayOS webhooks or verified gateway status queries write to the credit ledger. Client-side events only trigger UI state updates and polling.
- **Dual-Gateway Support Preserved**:
  - When configured with the mock gateway, the embedded form provides one-click simulation controls (Success, Failed, Expired) to ensure automated CI test suites and demo environments remain 100% operational.

---

## User Stories

1. As a learner with an ambitious multi-band goal (e.g., TOEIC 450 to 800), I want my curriculum divided into 4-week milestone sprints, so that I have achievable, motivating targets.
2. As a learner who paid 5 credits for a sprint, I want my curriculum to never expire due to inactivity, so that my investment is protected when life gets busy.
3. As an intensive learner, I want subsequent weeks to unlock immediately upon finishing the current week's steps, so that I can study at my own pace.
4. As a learner finishing a sprint, I want a milestone summary of my study hours, accuracy, and mastered topics, so that I feel a strong sense of accomplishment.
5. As a learner completing a sprint, I want a direct "Take Exit Mock Exam" CTA, so that I can immediately sit a test to officially certify my new band.
6. As a learner scoring at or above my target band on the Exit Mock Exam, I want my current band on the Dashboard and Profile to upgrade immediately, so that my verified proficiency is visible across the app.
7. As a learner completing Sprint 1 who still has a higher goal, I want to start Sprint 2 for 5 credits, so that I continue seamlessly toward my ultimate target.
8. As a learner who needs credits, I want to use a slider or type a custom number of credits, so that I only purchase exactly the amount I need.
9. As a learner purchasing credits, I want a flat, transparent price of 20,000 VND per credit, so that the total cost is instantly understandable.
10. As a learner initiating checkout, I want to pay via an embedded VietQR form directly inside the app, so that I don't have to navigate away to external tabs.
11. As a learner scanning the VietQR code on my banking app, I want the embedded form to detect settlement automatically, so that my credits appear without manual refreshing.
12. As a learner who changes my mind about the credit amount during checkout, I want the system to let me change the slider and recreate the order without throwing an "order already open" error.
13. As a learner closing the PayOS payment modal, I want my pending order cancelled cleanly, so that I am not locked out of creating a new order.
14. As a learner redirected to checkout because of insufficient credits (e.g., while starting a learning path), I want the checkout page to pre-fill the exact credits required and remember where I came from.
15. As a learner completing payment from a redirected action, I want the app to automatically return me to my previous page, so that I can resume immediately.
16. As a developer running unit tests or local demos without real bank accounts, I want the Mock Gateway simulator to work within the embedded checkout flow, so that automated tests pass reliably.
17. As an administrator reviewing order history, I want custom credit orders to record exact credit quantities and amounts, so that financial ledgers remain accurate.
18. As a system security engineer, I want return path parameters strictly validated against internal application routes, so that malicious open redirects are blocked.
19. As a system auditor, I want credit grants to be gated exclusively by verified PayOS webhook signatures, so that client-side tampering cannot inflate balances.
20. As a learner reviewing completed sprints, I want all topics covered in those sprints to remain permanently unlocked in Free Practice, so that I can drill them at any time.
21. As a learner with 0 credits attempting to start a learning path, I want to see an informative credit top-up dialog showing the 5-credit cost, so that I can transition directly to payment without friction.
22. As a learner with an active sprint, I want to see visual indicators on completed weeks versus the current active week, so that I can easily track my progression.
23. As a learner who completes Week 1 on Day 2, I want Week 2 steps to become immediately clickable, so that I am not forced to wait for Day 8.
24. As a learner who takes an Exit Mock Exam and misses the target score, I want the system to keep my current band intact and suggest remediation steps, so that I am not penalized with a downgraded status.
25. As a learner choosing between quick presets (5, 10, 20 credits) and custom input, I want the slider and chips to remain synchronized in real-time, so that I have complete confidence in the amount being charged.
26. As a finance officer, I want every paid custom credit order to post an immutable purchase row to the append-only credit transactions ledger, so that the total wallet balance is always the exact mathematical sum of ledger entries.

---

## Implementation Decisions

### 1. Database & Financial Domain Model
- **Orders Entity**:
  - The package association is relaxed to optional (nullable), allowing custom credit purchases that are not bound to a predefined package catalog row.
  - A dedicated positive integer column `Credits` is added directly to the orders schema to record the exact quantity of credits purchased.
  - When ordering custom credits: the package identifier is null, `Credits` records the requested count, and `Amount` is computed as `Credits * 20000.00`.
  - The credit ledger purchase recording routine reads `Credits` directly from the order rather than resolving through a package lookup.
  - Predefined package rows remain in the catalog as quick-preset shortcuts.

### 2. Auto-Superseding Order Lifecycle
- When an order creation request arrives for a user who already holds an open pending order:
  - If the requested credit amount or parameters differ, the payment domain service automatically marks the prior open order as cancelled or expired, releasing the single-pending-order database constraint.
  - A dedicated order cancellation operation is exposed to client-side abandonment callbacks to ensure clean lifecycle transitions.

### 3. In-App PayOS Embedded Checkout Form
- The web frontend integrates the official PayOS React SDK (`@payos/payos-checkout`) using the embedded integration mode (`embedded: true`).
- A dedicated embedded checkout dialog component is created:
  - Hosts the unique container element referenced by the PayOS checkout hook.
  - Binds client callbacks: success (invalidates wallet queries, displays notification, triggers redirect), cancel (invokes cancellation endpoint, closes dialog), and exit (cleans up gateway frame).
  - In mock gateway mode, renders simulated payment outcome buttons so offline tests and demonstrations operate seamlessly without external banking dependencies.

### 4. Interactive Credit Slider & Context-Aware Return
- The checkout interface is restructured around a synchronized HeroUI v3 slider and numeric field:
  - Range: 1 to 100 credits, default 5.
  - Quick-preset selection chips for 5, 10, and 20 credits.
  - Real-time calculation: Total VND = Credits $\times$ 20,000 VND.
  - Reads query parameters `credits` and `returnUrl`.
  - Validates `returnUrl` against internal path patterns (must start with `/` and contain no scheme or authority) to prevent open-redirect vulnerabilities.
  - On payment settlement, automatically navigates the learner back to `returnUrl` after a short celebration pause.

### 5. Milestone Learning Sprint Slicing
- When generating a learning path for a multi-band goal:
  - The planning service evaluates the distance between the learner's starting band and target band. If the gap spans more than one score band, the curriculum bounds itself to the immediate next band increment (`SprintTargetBand`).
  - Sprint 1 focuses exclusively on topics spanning from the current band to the sprint target band.
  - The long-term overarching target band remains stored in the learner's goal entity for roadmap visualization.
- On completing the final step of Week 4:
  - The path transitions to completed status and records a completion timestamp.
  - The path summary view includes sprint completion flags, the achieved milestone band, and recommended next actions.
  - The learning path interface displays a celebratory milestone card with study metrics and a primary CTA linking to the certificate's published Mock Exam.
  - Sitting the mock exam produces an attempt with a newer timestamp than the placement test, which the learner standing service automatically uses to resolve the updated current band code across all learner screens.

---

## Testing Decisions

### What Makes a Good Test
- Tests must assert observable behaviors, ledger invariants, and API contracts rather than private helper internals.
- Tests must verify:
  1. Creating an order with custom credits stores `Credits` and `Amount = Credits * 20000`, with `PackageId = null`.
  2. Initiating a new order when an open pending order exists cancels or supersedes the prior order without conflict errors.
  3. Slicing multi-band goals bounds the 4-week sprint to the immediate next band.
  4. Completing Week 4 flips the sprint to completed and unlocks permanent practice.
  5. Sittings of Exit Mock Exams upgrade the current band in goal standing calculations.
  6. Frontend component tests verify PayOS hook initialization parameters (`embedded: true`), modal rendering, and slider price calculations.

### Tested Modules
- **Payment & Credit Ledger Services** (`tests/Services.Tests/PaymentServiceTests.cs`, `CreditLedgerTests.cs`):
  - Custom credit ordering, flat 20k calculation, and pending order superseding.
- **Learning Path & Step Completion Services** (`tests/Services.Tests/LearningPathServiceTests.cs`, `PathStepCompletionTests.cs`):
  - Milestone sprint slicing and completion lifecycle.
- **Learner Standing Service** (`tests/Services.Tests/LearnerStandingTests.cs`):
  - Mock exam sitting overriding placement band.
- **Frontend Vitest Component Suites** (`src/web`):
  - Checkout slider and custom input calculation.
  - PayOS hook lifecycle and callback events.
  - Milestone celebration card and Exit Mock Exam CTA.

### Prior Art
- `tests/Services.Tests/PaymentServiceTests.cs`
- `tests/Services.Tests/LearningPathServiceTests.cs`
- `src/web/src/features/wallet/__tests__/wallet-checkout.test.tsx`

---

## Out of Scope

- Integrating additional payment gateways (MoMo, ZaloPay, VNPay) — payOS is the sole designated production gateway per ADR-0003.
- Partial credit purchases (fractions of 1 credit).
- Automatic recurring subscription billing (all orders are one-time credit top-ups).
- Native mobile client embedding (`extra_efficient_mobile`).

---

## Further Notes

- HeroUI v3 semantic tokens and Signage Brand Hues (`--accent: #0b4ea2`, `--success: #0a6b3c`, `--warning: #f2b705`, `--danger: #b31217`) must be applied across all new UI elements per `DESIGN.md`.
- All file links must use GitHub-style markdown links with forward slashes and the `file://` scheme.
