using System.Collections.Concurrent;
using System.Text.Json;

namespace Services;

public interface IMockPaymentGateway : IPaymentGateway
{
    void SetOutcome(long paymentNo, string status, string? errorCode = null);
    bool IsCancelled(long paymentNo);
    IReadOnlyCollection<long> CancelledPaymentNos { get; }
}

public sealed class MockPaymentGateway : IMockPaymentGateway
{
    public const string Name = "mock";
    public string GatewayName => Name;

    private readonly ConcurrentDictionary<long, (string Status, string? ErrorCode)> _byPaymentNo = new();
    private readonly ConcurrentBag<long> _cancelledPaymentNos = new();

    public bool IsCancelled(long paymentNo) => _cancelledPaymentNos.Contains(paymentNo);
    public IReadOnlyCollection<long> CancelledPaymentNos => _cancelledPaymentNos.ToArray();

    public void SetOutcome(long paymentNo, string status, string? errorCode = null)
    {
        _byPaymentNo[paymentNo] = (status, errorCode);
    }

    public Task<bool> CancelPaymentLinkAsync(
        long paymentNo,
        string? gatewayTxnRef,
        string? cancellationReason = null,
        CancellationToken cancellationToken = default)
    {
        _cancelledPaymentNos.Add(paymentNo);
        _byPaymentNo[paymentNo] = ("expired", null);
        return Task.FromResult(true);
    }

    public Task<PaymentGatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayOrderInfo orderInfo,
        CancellationToken cancellationToken = default)
    {
        var checkoutUrl = $"/checkout/mock-gateway?orderCode={Uri.EscapeDataString(orderInfo.OrderCode)}";
        var gatewayTxnRef = $"MOCK-{orderInfo.PaymentNo}";
        return Task.FromResult(new PaymentGatewayLinkResult(checkoutUrl, gatewayTxnRef));
    }

    public Task<GatewayPaymentStatus> QueryPaymentStatusAsync(
        long paymentNo,
        string? gatewayTxnRef,
        CancellationToken cancellationToken = default)
    {
        if (_byPaymentNo.TryGetValue(paymentNo, out var outcome))
        {
            return Task.FromResult(new GatewayPaymentStatus(outcome.Status, gatewayTxnRef, outcome.ErrorCode, $"{{ \"status\": \"{outcome.Status}\", \"paymentNo\": {paymentNo} }}"));
        }

        // Default state when nothing simulated yet: pending
        return Task.FromResult(new GatewayPaymentStatus("pending", gatewayTxnRef, null, $"{{ \"status\": \"pending\", \"paymentNo\": {paymentNo} }}"));
    }

    public Task<WebhookVerificationResult> VerifyWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        if (signature == "invalid" || signature == "bad")
        {
            return Task.FromResult(new WebhookVerificationResult(
                IsValid: false,
                PaymentNo: 0,
                GatewayTxnRef: null,
                Status: "failed",
                ErrorCode: "INVALID_SIGNATURE",
                RawPayload: payload));
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            long paymentNo = 0;
            if (root.TryGetProperty("paymentNo", out var pNo) && pNo.TryGetInt64(out var pn))
            {
                paymentNo = pn;
            }
            else if (root.TryGetProperty("orderCode", out var oCode) && oCode.TryGetInt64(out var ocLong))
            {
                paymentNo = ocLong;
            }

            var status = root.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "paid" : "paid";
            var errorCode = root.TryGetProperty("errorCode", out var errProp) ? errProp.GetString() : null;
            var txnRef = root.TryGetProperty("gatewayTxnRef", out var txnProp) ? txnProp.GetString() : $"MOCK-{paymentNo}";

            return Task.FromResult(new WebhookVerificationResult(
                IsValid: true,
                PaymentNo: paymentNo,
                GatewayTxnRef: txnRef,
                Status: status,
                ErrorCode: errorCode,
                RawPayload: payload));
        }
        catch
        {
            // If raw text or invalid JSON, treat as valid mock payload if signature is not bad
            return Task.FromResult(new WebhookVerificationResult(
                IsValid: true,
                PaymentNo: 0,
                GatewayTxnRef: null,
                Status: "paid",
                ErrorCode: null,
                RawPayload: payload));
        }
    }
}
