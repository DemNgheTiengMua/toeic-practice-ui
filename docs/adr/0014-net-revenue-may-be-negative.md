# Revenue is net of refunds, and may be negative

## Status

Accepted. Implements #279, #284.

## Context

Two defects met in the same figure.

The dashboard computed "revenue this month" as `SUM(Orders.Amount)` over paid orders. Every amount in the
schema is `CHECK (Amount >= 0)`, so that sum cannot be negative — it is gross receipts wearing the name
revenue. A month in which a learner was refunded for an order paid earlier in the same month reports money
the business did not keep.

The same figure was computed **twice, differently**. `AdminDashboardService` counted a paid order whose
`PaidAt` is null by falling back to `CreatedAt`; `AdminOrderService` counted only `PaidAt` inside the
month. One question, two answers, on two admin screens. Both also cut the month at UTC midnight, seven
hours early for the accountant who reads it.

## Decision

Revenue is **paid orders minus money refunds settled in the same window**, and a negative result is a
valid answer, not a clamped one. Credit refunds are excluded from revenue — no money moved — and counted
in their own figure.

One shared definition of "a paid order in a reporting period" is used by every admin reporting query, and
reporting windows are cut at a fixed UTC+7 offset held in one place. The offset is a constant, never the
container's timezone, so a deployment cannot silently change a report.

A money refund is attributed to its **settlement** date, not its request date. Settlement is the only
moment money is treated as having left.

## Consequences

Negative revenue is rendered in Sign Red with a true minus sign (`U+2212`), per the design system's rule
that red marks a refused condition rather than decorating one. A reader cannot mistake it for a broken
number.

Month totals visibly change for existing data, because the boundary moved from UTC to UTC+7 and the figure
stopped being gross. This is the intended correction, not a regression.

The figure now depends on `Refunds`, so a screen that reports money without consulting refunds would be
wrong by construction. Any future money report must use the same shared definition rather than writing its
own predicate — the defect above is exactly what a second predicate costs.

`PaidAt` being nullable while `Status = 'paid'` is a data-shape defect left in place. The shared predicate
counts such a row exactly once using its creation date, so the defect does not become a reporting defect.
