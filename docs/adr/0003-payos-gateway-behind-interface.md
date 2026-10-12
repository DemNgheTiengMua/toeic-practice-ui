# payOS is the real gateway; gateways are pluggable

Credit purchases go through payOS (VietQR bank transfer), replacing the VNPay/MoMo design. payOS has no
fake-money sandbox: testing means a real small transfer into the merchant's own linked account. So
every gateway sits behind `IPaymentGateway`, with `payos` (real) and `mock` (no money moves; used by
automated tests and offline demos) as the two implementations today. Adding VNPay or MoMo later means
a new implementation and a new `Gateway` value, not a schema redesign.

## Consequences

- Orders keep the human-readable `OrderCode` and gain a gateway-agnostic numeric `PaymentNo` from a
  SQL `SEQUENCE`, because payOS (and most gateways) require a numeric merchant order number.
- Every callback, return redirect, and status query is logged as a payment event, so reconciliation is
  auditable and repeated callbacks are harmless.
- Credit is granted only on a signature-verified webhook or a server-side status query reporting paid —
  never on the browser's return redirect, which anyone can type.
- The WebAPI webhook endpoint must be publicly reachable; local development needs a tunnel.
