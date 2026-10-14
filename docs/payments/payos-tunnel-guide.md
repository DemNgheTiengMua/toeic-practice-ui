# Manual End-to-End Payment Testing with payOS and Webhook Tunnel

This runbook documents the exact steps for conducting an end-to-end payment test using the real payOS VietQR gateway with a publicly reachable webhook tunnel.

---

## 1. Prerequisites & Credentials

payOS requires real HTTP delivery of its HMAC-SHA256 signed webhooks to verify transactions and grant learner credits. Testing uses the registered merchant application:

- **Client ID**: `13ba1e85-4e59-42a9-9697-1ca174e6e3d1`
- **API Key**: `13081d50-a72a-417d-93bd-5cf82c46f876`
- **Checksum Key**: `618cc06cdcdcc939b290d7f8730b5c65bae420ef310fa049f735f1609efae110`

---

## 2. Setting Up a Local Webhook Tunnel

The WebAPI runs locally on port `5001`. Because payOS servers cannot reach `localhost`, run a reverse proxy/tunnel to provide a secure public URL.

### Option A: Cloudflare Tunnel (`cloudflared`) — Recommended

```bash
cloudflared tunnel --url http://localhost:5001
```

Note the output URL, e.g. `https://random-subdomain.trycloudflare.com`.

### Option B: ngrok

```bash
ngrok http 5001
```

Note the output URL, e.g. `https://xxxx-xx-xx.ngrok-free.app`.

### Option C: VS Code Dev Tunnels

```bash
devtunnel host -p 5001 --allow-anonymous
```

---

## 3. Configuration

In `src/WebAPI/appsettings.json` (or via environment variables):

```json
{
  "Payment": {
    "Gateway": "payos",
    "PayOS": {
      "ClientId": "13ba1e85-4e59-42a9-9697-1ca174e6e3d1",
      "ApiKey": "13081d50-a72a-417d-93bd-5cf82c46f876",
      "ChecksumKey": "618cc06cdcdcc939b290d7f8730b5c65bae420ef310fa049f735f1609efae110",
      "WebhookUrl": "https://<your-tunnel-subdomain>/api/payments/webhook"
    }
  }
}
```

> [!IMPORTANT]
> The Webhook URL must point to `/api/payments/webhook` on your tunnel domain and use HTTPS.

---

## 4. Webhook Registration with payOS

You can confirm the webhook URL using either:

1. **payOS Merchant Portal**:
   - Log in to [https://my.payos.vn](https://my.payos.vn).
   - Go to **Kênh thanh toán** > **Cài đặt Webhook**.
   - Paste `https://<your-tunnel-subdomain>/api/payments/webhook` and click **Xác nhận**.
   - payOS will immediately send a verification request. The endpoint verifies and returns HTTP 200.

2. **Automated API confirmation**:
   - Call the WebAPI endpoint (requires Admin token):
     ```bash
     curl -X POST http://localhost:5001/api/payments/payos/confirm-webhook \
          -H "Authorization: Bearer <ADMIN_JWT>"
     ```

---

## 5. End-to-End Walkthrough

### Step 1: Start Solution Services

```bash
# Terminal 1: WebAPI (port 5001)
dotnet run --project src/WebAPI

# Terminal 2: WebMVC (port 5000)
dotnet run --project src/WebMVC
```

### Step 2: Sign In as a Verified Learner

1. Open `http://localhost:5000/sign-in` in the browser.
2. Sign in with a verified learner account (e.g., `learner@demo.local` / `Pass@123`).
3. If using a new account, verify email first (diagnostic practice remains free, but checkout strictly blocks unverified accounts).

### Step 3: Select Package & Proceed to Checkout

1. Navigate to `http://localhost:5000/pricing`.
2. Choose a credit package (e.g. **Prep Pack** — 5 credits, 199,000 VND).
3. The browser lands on `http://localhost:5000/checkout?packageId=...`.
4. Review order summary: current balance, credits added, total amount, payment method (payOS).
5. Click **Pay [Amount]**.

### Step 4: Pay via payOS Embedded Form or Hosted Page

1. The payOS checkout interface initializes:
   - In modern browsers with JavaScript enabled, the embedded payOS form opens inline in the checkout sign frame.
   - Alternatively, if JS is disabled or full redirect is invoked, the browser redirects to `https://pay.payos.vn/web/{paymentLinkId}`.
2. The payOS interface renders:
   - Dynamic VietQR code.
   - Bank transfer information (Account Number, Bank Name, Account Name).
   - Transfer amount and exact transfer description (e.g. `DH-20261014-0001`).
   - Countdown timer (15 minutes).

### Step 5: Transfer Funds & Observe Webhook

1. Using your bank app or payOS test transfer simulator, transfer the exact amount with the transfer description.
2. **payOS Webhook Execution**:
   - payOS sends `POST /api/payments/webhook` with the JSON payload and `signature`.
   - WebAPI validates the signature using `HMAC-SHA256(data, ChecksumKey)`.
   - `PaymentService.SettleOrderAsync`:
     - Updates `Orders` row: `Status = 'paid'`, `PaidAt = SYSDATETIMEOFFSET()`.
     - Appends `CreditTransactions` row: `Delta = +5`, `Reason = 'purchase'`, linked to `OrderId`.
     - Appends `PaymentEvents` row: `Source = 'webhook'`, `SignatureValid = 1`, `WasApplied = 1`.

### Step 6: Verify Returning Learner UX

1. Once payment is confirmed, the payOS embedded form triggers `onSuccess` and navigates to:
   `http://localhost:5000/payment/result?orderCode=DH-20261014-0001`
2. The page renders:
   - Sign Green header: **"Payment successful"** (`DONE`).
   - Added credit amount and newly computed balance.
   - Order receipt table with Order Code, Package, Amount, Gateway (`PAYOS`), and Time.
   - Action links: **Start practice** and **View credit wallet**.
3. Click **View credit wallet** (`/wallet`):
   - Balance reflects the newly added credits.
   - Transaction ledger displays the `purchase` row with order code and timestamp.

---

## 6. Verification Queries in Database

Inspect LocalDB to verify complete auditability and constraint compliance:

```sql
-- 1. Check order status
SELECT OrderId, OrderCode, PaymentNo, Status, Amount, Gateway, GatewayTxnRef, CreatedAt, PaidAt
FROM Orders
WHERE OrderCode LIKE 'DH-%'
ORDER BY OrderId DESC;

-- 2. Check append-only credit ledger
SELECT TxnId, UserId, Reason, Delta, OrderId, CreatedAt
FROM CreditTransactions
ORDER BY TxnId DESC;

-- 3. Check payment event audit log
SELECT EventId, OrderId, Source, ReportedStatus, SignatureValid, WasApplied, Note, CreatedAt
FROM PaymentEvents
ORDER BY EventId DESC;
```

---

## 7. Failure & Expiry Scenarios

1. **Order Expiration**:
   - If an order is not completed within 15 minutes, `ExpireStaleOrdersAsync` voids the link at payOS via `PaymentRequests.CancelAsync` and marks the order `expired`.
   - `PaymentEvents` logs a row with `Source = 'cancel'`, `ReportedStatus = 'expired'`, `WasApplied = 1`.
2. **Tampered Webhook**:
   - If a webhook payload has its amount, orderCode, or signature modified in transit, `VerifyWebhookAsync` rejects it with `IsValid = false`.
   - WebAPI responds with `HTTP 400 Bad Request: {"error": "Invalid signature"}`.
   - No ledger rows are written and the order remains unmodified.
3. **Repeated Webhooks**:
   - Subsequent duplicate webhook deliveries for an already-paid order are handled idempotently.
   - The event is logged in `PaymentEvents` with `WasApplied = 0` (`"Idempotent repeat confirmation (ignored)"`) and no duplicate credits are granted.
