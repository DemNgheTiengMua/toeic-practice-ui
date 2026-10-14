using System.Text.Json;
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using PayOS.Models.Webhooks;

namespace Services;

public sealed class PayOsOptions
{
    public const string SectionName = "Payment:PayOS";
    public string ClientId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChecksumKey { get; set; } = "";
}

public sealed class PayOsPaymentGateway : IPaymentGateway
{
    public const string Name = "payos";
    public string GatewayName => Name;

    private readonly PayOSClient _client;

    public PayOsPaymentGateway(PayOsOptions options)
    {
        _client = new PayOSClient(new PayOS.PayOSOptions
        {
            ClientId = options.ClientId,
            ApiKey = options.ApiKey,
            ChecksumKey = options.ChecksumKey
        });
    }

    public async Task<PaymentGatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayOrderInfo orderInfo,
        CancellationToken cancellationToken = default)
    {
        var desc = string.IsNullOrWhiteSpace(orderInfo.Description) ? "ExtraEfficiency" : orderInfo.Description;
        if (desc.Length > 25)
        {
            desc = desc[..25];
        }

        var request = new CreatePaymentLinkRequest
        {
            OrderCode = orderInfo.PaymentNo,
            Amount = (long)orderInfo.Amount,
            Description = desc,
            ReturnUrl = orderInfo.ReturnUrl,
            CancelUrl = orderInfo.CancelUrl
        };

        var response = await _client.PaymentRequests.CreateAsync(request);
        return new PaymentGatewayLinkResult(response.CheckoutUrl, response.PaymentLinkId);
    }

    public async Task<GatewayPaymentStatus> QueryPaymentStatusAsync(
        long paymentNo,
        string? gatewayTxnRef,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var idToQuery = !string.IsNullOrEmpty(gatewayTxnRef) ? gatewayTxnRef : paymentNo.ToString();
            var info = await _client.PaymentRequests.GetAsync(idToQuery);
            var statusStr = info.Status.ToString().ToLowerInvariant();
            var status = statusStr switch
            {
                "paid" => "paid",
                "cancelled" or "canceled" => "failed",
                "expired" => "expired",
                _ => "pending"
            };

            return new GatewayPaymentStatus(
                Status: status,
                GatewayTxnRef: info.Id,
                ErrorCode: null,
                RawPayload: JsonSerializer.Serialize(info));
        }
        catch (Exception ex)
        {
            return new GatewayPaymentStatus(
                Status: "pending",
                GatewayTxnRef: gatewayTxnRef,
                ErrorCode: ex.Message,
                RawPayload: ex.ToString());
        }
    }

    public async Task<WebhookVerificationResult> VerifyWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var webhook = JsonSerializer.Deserialize<Webhook>(payload);
            if (webhook is null)
            {
                return new WebhookVerificationResult(false, 0, null, "failed", "DESERIALIZATION_FAILED", payload);
            }

            if (!string.IsNullOrEmpty(signature))
            {
                webhook.Signature = signature;
            }

            var verifiedData = await _client.Webhooks.VerifyAsync(webhook);
            var status = (verifiedData.Code ?? "").ToLowerInvariant() switch
            {
                "00" => "paid",
                _ => "failed"
            };

            return new WebhookVerificationResult(
                IsValid: true,
                PaymentNo: verifiedData.OrderCode,
                GatewayTxnRef: verifiedData.PaymentLinkId,
                Status: status,
                ErrorCode: verifiedData.Code,
                RawPayload: payload);
        }
        catch (Exception ex)
        {
            return new WebhookVerificationResult(
                IsValid: false,
                PaymentNo: 0,
                GatewayTxnRef: null,
                Status: "failed",
                ErrorCode: ex.Message,
                RawPayload: payload);
        }
    }
}
