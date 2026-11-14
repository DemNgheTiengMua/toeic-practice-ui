# Spec: Eradicate Legacy Packages Infrastructure & Unify Credit Wallet Ledger

**Status:** ready-for-agent  
**Date:** 2026-11-14  
**ADRs touched:** ADR-0001 (database-first EF Core), ADR-0003 (orders & payment gateways), ADR-0015 (React + HeroUI v3 SPA), ADR-0017 (streamlined direct ownership)

---

```[tasklist]
### Implementation Tasks
- [ ] #321
- [ ] #322
- [ ] #323
```

## Problem Statement

Following the introduction of the flat-rate credit purchasing model (20,000 VND = 1 Credit) and the in-app PayOS Embedded Checkout form with interactive slider, the platform suffers from architectural fragmentation and conflicting user experiences due to lingering legacy "Package" infrastructure:

1. **Conflicting Wallet Experience (`/wallet`)**:
   - When learners visit the `/wallet` page (via the sidebar or the header credit pill), the default tab is an obsolete "Credit packages" grid displaying three rigid packages (*Starter Pack 100,000 VND for 5 credits*, *Intensive Pack 190,000 VND for 10 credits at 19k/credit*, and *Master Pack 360,000 VND for 20 credits at 18k/credit*).
   - This directly contradicts the platform's core enterprise invariant of a transparent, flat conversion rate of 20,000 VND per credit.
   - The "Buy credits" button on `/wallet` simply activates this obsolete packages tab rather than directing learners to the newly built PayOS slider checkout experience.

2. **Outdated Pricing Page (`/pricing`)**:
   - The public `/pricing` page continues to render the three legacy packages and volume discount pricing cards instead of the unified credit slider and transparent pricing calculator.

3. **Scattered and Broken Insufficient Credit Redirects**:
   - Across the application (mock exam preflight, placement retake modal, speaking & writing studio AI grading gates, mentor bookings, and header credit pill), feature gates redirect learners to the legacy `/wallet` page with no parameter context, rather than forwarding them to `/checkout?credits={needed}&returnUrl={currentPath}`.

4. **Technical Debt and Schema Bloat**:
   - The database schema still maintains the `Packages` table, `Orders.PackageId` nullable column, seeding of package rows in `seed-demo.sql`, backend `Package` entity, `PackageDto`, and `GET /api/packages` endpoint.

---

## Solution

Completely eradicate all legacy "Package" artifacts, tables, endpoints, and UI tabs across the entire stack, and unify the learner's financial experience into two clearly distinct, cohesive surfaces:

1. **Purify `/wallet` as a Dedicated Ledger & Billing Audit Workspace**:
   - Remove the "Credit packages" tab entirely from `/wallet`.
   - `/wallet` becomes strictly **"Wallet & Billing History"** focused on financial transparency:
     - Retains the 4 financial metric cards: **Credit balance**, **Total purchased**, **Total spent**, and **Paid orders**.
     - Provides two clean, auditable tabs:
       - **Credit history**: The append-only ledger of credit transactions (`CreditTransactions`), displaying deltas, reasons, and timestamps.
       - **Orders & receipts**: The chronological list of orders (`Orders`) with payment statuses, merchant order codes, gateway transaction references, and downloadable/inspectable receipts.
     - The top-right **"Buy credits"** CTA button navigates directly to `/checkout`.

2. **Re-architect `/pricing` as a Flat Credit Calculator**:
   - Replace the legacy 3-package cards with an interactive Credit Calculator widget operating at the flat rate of 20,000 VND = 1 Credit.
   - Provide instant cost calculation and quick presets (5, 10, 20 credits) with a primary CTA button: **"Top up Credits"** linking directly to `/checkout?credits={selected}`.

3. **Standardize Context-Aware Credit Preflights Across the Entire App**:
   - Update every insufficient credit prompt (Mock Exam start, Placement retake, Speaking AI grading, Writing AI grading, Mentor session booking, Learning Path sprint unlock, and Header credit pill) to link directly to `/checkout?credits={required}&returnUrl={currentPath}`.
   - Upon completing payment, learners are automatically returned to their original activity.

4. **Eradicate Database and Backend Package Infrastructure**:
   - Drop the `Packages` table and the `Orders.PackageId` column in `docs/database-schema.sql`.
   - Remove package inserts from `docs/seed-demo.sql`.
   - Remove the `Package` domain entity, EF Core mapping, `PackageDto`, and `GET /api/packages` endpoint.
   - Refactor `PaymentService` and `CheckoutEndpoints` to accept exclusively `credits` and calculate `Amount = credits * 20000m`.

---

## User Stories

1. As a learner, I want `/wallet` to focus purely on my credit balance and transaction history, so that I can audit my spending and purchases without seeing conflicting package advertisements.
2. As a learner, I want the "Buy credits" button on my wallet page to take me to the interactive PayOS checkout slider, so that I can buy the exact number of credits I need.
3. As a learner, I want to inspect my append-only credit ledger on `/wallet` under "Credit history", so that I know exactly when and why each credit was added or deducted.
4. As a learner, I want to inspect my past payment orders and gateway receipts under "Orders & receipts", so that I have clear documentation of my financial transactions.
5. As a learner visiting `/pricing`, I want to see a clear, uniform exchange rate of 20,000 VND per credit with an interactive calculator, so that pricing is transparent and simple.
6. As a learner on `/pricing`, I want to click quick preset chips (5, 10, 20 credits) or adjust a slider, so that I can see the exact cost before proceeding to checkout.
7. As a learner clicking the credit balance pill in the top navigation header, I want quick access to view my wallet ledger or top up credits, so that I am always in control of my balance.
8. As a learner attempting to start a mock exam without enough credits, I want the preflight modal to direct me to checkout pre-filled with the needed credits and returning to the exam, so that I don't get lost on a general packages page.
9. As a learner booking a mentor without enough credits, I want the booking modal to route me to `/checkout?credits={mentorPrice}&returnUrl={mentorPage}`, so that I can immediately resume booking after payment.
10. As a learner taking a placement retake without enough credits, I want to be redirected to checkout with the retake cost pre-selected and auto-return to placement, so that my retake sitting is seamless.
11. As a learner submitting a speaking or writing free-response practice without enough credits, I want a direct link to checkout pre-filled with 1 credit and returning to my draft, so that I can submit without losing my work.
12. As a developer, I want the `Packages` table completely eradicated from SQL Server, so that the database schema has no obsolete tables or orphaned constraints.
13. As a developer, I want `Orders` to no longer reference a `PackageId`, so that order creation logic is simple and strictly driven by `Credits` and `Amount`.
14. As a developer, I want `GET /api/packages` removed from the API surface, so that API clients cannot request non-existent package configurations.
15. As an administrator, I want orders in the admin console to display the credit amount and total VND without attempting to look up deleted package names, so that order management remains robust.
16. As a security reviewer, I want all return URLs to continue passing internal path validation, so that context-aware redirects cannot be exploited for open redirects.
17. As an automated test engineer, I want `docs/schema-tests/run-all.sh` to pass 100% on a schema without `Packages`, so that schema integrity is preserved.

---

## Implementation Decisions

### Decision 1: Database Schema Cleanup
- Remove `CREATE TABLE Packages (...)` and its index `UX_Packages_OneFeatured` from `docs/database-schema.sql`.
- In `Orders` table:
  - Remove `PackageId INT NULL REFERENCES Packages(PackageId)`.
  - Retain `Credits INT NOT NULL CONSTRAINT DF_Orders_Credits DEFAULT 1 CONSTRAINT CK_Orders_Credits CHECK (Credits > 0)`.
  - Retain `Amount DECIMAL(12,2) NOT NULL CONSTRAINT CK_Orders_Amount CHECK (Amount >= 0)`.
- In `docs/seed-demo.sql`:
  - Eradicate lines 57–77 (`-- ---- Packages (3 rows)`).
  - Update `[Orders]` demo inserts to omit `[PackageId]`.
- In `docs/schema-tests/`:
  - Update any domain test files that inserted into `Packages` to insert directly into `Orders (UserId, Credits, Status, Amount, OrderCode, PaymentNo, Gateway)` without `@pk`.

### Decision 2: Backend Domain & API Eradication
- Eradicate `src/BusinessObjects/Entities/Package.cs`.
- In `CompanionDbContext.cs` and `CompanionDbContext.Finance.cs`:
  - Remove `public virtual DbSet<Package> Packages { get; set; }`.
  - Remove `modelBuilder.Entity<Package>(...)` and the `entity.HasOne(d => d.Package)` relationship on `Order`.
- In `Contracts`:
  - Remove `PackageDto.cs`.
  - In `CreateOrderRequest.cs`: simplify to `public sealed record CreateOrderRequest(int Credits);`.
  - In `OrderDto.cs`, `AdminOrderDto.cs`, `CheckoutPreviewDto.cs`: drop `int? PackageId` and `string? PackageName`.
- In `Services`:
  - In `PaymentService.cs`:
    - Drop `packageId` parameter from `GetCheckoutPreviewAsync` and `CreateOrderAsync`.
    - Both methods strictly accept `int credits`.
    - `Amount = credits * PricePerCreditVnd`.
    - In `ToOrderDto`, remove all references to `o.Package`.
  - In `AdminOrderService.cs`: remove `o.Package` projection.
  - In `CreditLedger.cs`: assert `order.Credits == amount`.
- In `WebAPI`:
  - Delete `PackageEndpoints.cs` and unmap `GET /api/packages`.
  - In `CheckoutEndpoints.Orders.cs`: accept `int credits` from query / body.

### Decision 3: Frontend `/wallet` Simplification
- In `src/web/src/features/wallet/WalletPage.tsx`:
  - Remove `packagesQuery` and the `packages` tab from the `Tabs` list.
  - State `activeTab` defaults to `'ledger'` (`'ledger' | 'orders'`).
  - Retain the 4 metric cards:
    - Credit balance
    - Total purchased credits
    - Total spent (VND)
    - Paid orders count
  - Provide two tabs:
    1. **Credit history**: `CreditLedgerTable` showing all transactions (`Delta`, `Reason`, `CreatedAt`).
    2. **Orders & receipts**: `OrdersTable` showing order code, date, gateway, status, amount, and receipt inspection.
  - Top header action:
    - Button **"Buy credits"** (`variant="primary"`) links directly to `/checkout` using TanStack Router `<Link to="/checkout">`.
    - Remove or update "Pricing plans" button to link to `/pricing`.

### Decision 4: Frontend `/pricing` Redesign
- In `src/web/src/features/public/PricingPage.tsx`:
  - Eradicate the 3 legacy package cards (`Starter Pack`, `Intensive Pack`, `Master Pack`).
  - Introduce a transparent Credit Calculator Card:
    - Explains the flat conversion rate: **1 Credit = 20,000 VND**.
    - Includes interactive credit slider (1 to 100) synchronized with preset chips (5, 10, 20 credits).
    - Displays dynamic cost calculation (`credits * 20,000 VND`).
    - Primary CTA: **"Proceed to Top-up"** linking to `/checkout?credits={selectedCredits}`.
  - Retain the FAQ accordion (Do credits expire?, What stays free?, etc.).

### Decision 5: Universal Context-Aware Preflights
- Update all hardcoded `/wallet` redirection paths across `src/web`:
  - `src/components/shell/AppShell.tsx`: Header credit pill links to `/wallet` with a "+ Top up" button linking to `/checkout`.
  - `src/features/exams/MockExamCatalogPage.tsx` & `MockExamConfirmPage.tsx`: redirect to `/checkout?credits=1&returnUrl=/exams`.
  - `src/features/placement/PlacementRetakePage.tsx`: redirect to `/checkout?credits={retakeCredits}&returnUrl=/placement/retake`.
  - `src/features/productive/speaking/SpeakingStudioPage.tsx`: redirect to `/checkout?credits=1&returnUrl=/practice/speaking`.
  - `src/features/productive/writing/WritingStudioPage.tsx`: redirect to `/checkout?credits=1&returnUrl=/practice/writing`.
  - `src/features/mentors/MentorDetailPage.tsx`: redirect to `/checkout?credits={mentorPrice}&returnUrl=/mentors/${mentorId}`.

---

## Testing Decisions

### What Makes a Good Test
- Tests must assert external system contracts, financial ledger accuracy, and user observable behaviors without testing private database queries or CSS classes.
- Tests must verify:
  1. Creating an order with custom credits persists `Credits` and `Amount = Credits * 20000`, with no `PackageId` column existing.
  2. The `/wallet` page renders balance, ledger, and order history without making any requests to `/api/packages`.
  3. Clicking "Buy credits" on `/wallet` navigates to `/checkout`.
  4. The `/pricing` page calculates 20k/credit dynamically and routes to `/checkout?credits=N`.
  5. Insufficient credit preflight blocks across mock exams, placement, and productive studios correctly generate context-aware checkout URLs with valid return destinations.

### Tested Modules
- **Database Schema**: `docs/schema-tests/run-all.sh` (47 tests validating zero dependency on `Packages`).
- **Payment & Order Services**: `tests/Services.Tests/PaymentServiceTests.cs`, `AdminOrderServiceTests.cs`.
- **Frontend Components**:
  - `src/web/src/features/wallet/__tests__/wallet-checkout.test.tsx` (verifying `/wallet` ledger tabs and `/checkout` slider).
  - `src/web/src/features/placement/__tests__/PlacementCatalogAndRetake.test.tsx`.
  - `src/web/src/features/productive/speaking/__tests__/speaking-studio.test.tsx`.
  - `src/web/src/features/productive/writing/__tests__/writing-studio.test.tsx`.

---

## Out of Scope

- Introducing subscription recurring billing or automated credit auto-refills.
- Volume-based discount schemes (the rate is permanently flat at 20,000 VND / Credit).
- Altering the append-only ledger transaction calculation (`CreditTransactions`).
- Modifying mobile client code (`extra_efficient_mobile`).

---

## Further Notes

- All changes adhere strictly to HeroUI v3 compound components (`Card.Content`, `Tabs.List`, `Tabs.Tab`, `Tabs.Panel`, `NumberField`, `Slider`) and Signage Brand Hues (`--accent: #0b4ea2`, `--success: #0a6b3c`, `--warning: #f2b705`, `--danger: #b31217`) per `DESIGN.md`.
- All file links must use GitHub-style markdown links with forward slashes and the `file://` scheme.
