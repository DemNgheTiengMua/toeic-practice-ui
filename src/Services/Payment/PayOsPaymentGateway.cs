using BusinessObjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    public string? WebhookUrl { get; set; }
}

public sealed class PayOsPaymentGateway : IPaymentGateway
{

    public PaymentGateway GatewayName => PaymentGateway.Payos;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PayOSClient _client;
    private readonly PayOsOptions _options;
    private readonly ILogger<PayOsPaymentGateway>? _logger;

    public PayOsPaymentGateway(PayOsOptions options, ILogger<PayOsPaymentGateway>? logger = null)
    {
        _options = options;
        _logger = logger;
        _client = new PayOSClient(new PayOS.PayOSOptions
        {
            ClientId = options.ClientId,
            ApiKey = options.ApiKey,
            ChecksumKey = options.ChecksumKey
        });
    }

    public async Task<string?> ConfirmWebhookUrlAsync(string? webhookUrl = null, CancellationToken cancellationToken = default)
    {
        var url = webhookUrl ?? _options.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        var res = await _client.Webhooks.ConfirmAsync(url);
        return res?.WebhookUrl;
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
            var status = MapPayOsStatus(info.Status.ToString());

            return new GatewayPaymentStatus(
                Status: status,
                GatewayTxnRef: info.Id,
                ErrorCode: null,
                RawPayload: JsonSerializer.Serialize(info));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "QueryPaymentStatusAsync failed for paymentNo {PaymentNo}", paymentNo);
            return new GatewayPaymentStatus(
                Status: GatewayStatus.Pending,
                GatewayTxnRef: gatewayTxnRef,
                ErrorCode: ex.Message,
                RawPayload: ex.ToString());
        }
    }

    public async Task<bool> CancelPaymentLinkAsync(
        long paymentNo,
        string? gatewayTxnRef,
        string? cancellationReason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var idToQuery = !string.IsNullOrEmpty(gatewayTxnRef) ? gatewayTxnRef : paymentNo.ToString();
            var reason = string.IsNullOrWhiteSpace(cancellationReason) ? "Order expired" : cancellationReason;
            await _client.PaymentRequests.CancelAsync(idToQuery, reason);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "CancelPaymentLinkAsync failed for paymentNo {PaymentNo}", paymentNo);
            return false;
        }
    }

    public async Task<WebhookVerificationResult> VerifyWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new WebhookVerificationResult(false, 0, null, GatewayStatus.Failed, "EMPTY_PAYLOAD", payload ?? "");
            }

            var webhook = JsonSerializer.Deserialize<Webhook>(payload, JsonOptions);
            if (webhook is null)
            {
                return new WebhookVerificationResult(false, 0, null, GatewayStatus.Failed, "DESERIALIZATION_FAILED", payload);
            }

            if (!string.IsNullOrEmpty(signature))
            {
                webhook.Signature = signature;
            }

            if (string.IsNullOrWhiteSpace(webhook.Signature))
            {
                return new WebhookVerificationResult(false, 0, null, GatewayStatus.Failed, "MISSING_SIGNATURE", payload);
            }

            var verifiedData = await _client.Webhooks.VerifyAsync(webhook);
            var status = MapWebhookCodeToStatus(verifiedData.Code);

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
            _logger?.LogWarning(ex, "Webhook signature verification rejected: {Message}", ex.Message);
            return new WebhookVerificationResult(
                IsValid: false,
                PaymentNo: 0,
                GatewayTxnRef: null,
                Status: GatewayStatus.Failed,
                ErrorCode: ex.Message,
                RawPayload: payload);
        }
    }

    public static GatewayStatus MapPayOsStatus(string? payOsStatus)
    {
        return (payOsStatus ?? "").Trim().ToLowerInvariant() switch
        {
            "paid" => GatewayStatus.Paid,
            "cancelled" or "canceled" => GatewayStatus.Failed,
            "expired" => GatewayStatus.Expired,
            "pending" or "processing" => GatewayStatus.Pending,
            _ => GatewayStatus.Pending
        };
    }

    public static GatewayStatus MapWebhookCodeToStatus(string? code)
    {
        return (code ?? "").Trim().ToLowerInvariant() switch
        {
            "00" => GatewayStatus.Paid,
            _ => GatewayStatus.Failed
        };
    }

    public static string CreateWebhookSignature(WebhookData data, string checksumKey)
    {
        var method = typeof(PayOS.Crypto.CryptoProvider).GetMethod(
            "ConvertToSortedQueryString",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var query = method?.Invoke(null, [data, false]) as string ?? "";
        return GenerateHmacSha256Signature(query, checksumKey);
    }

    public static string GenerateHmacSha256Signature(string dataString, string checksumKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataString));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
