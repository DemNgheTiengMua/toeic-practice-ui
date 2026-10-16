using System.Text.Json;
using BusinessObjects;
using PayOS.Models.Webhooks;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class PayOsPaymentGatewayTests
{
    private const string TestClientId = "13ba1e85-4e59-42a9-9697-1ca174e6e3d1";
    private const string TestApiKey = "13081d50-a72a-417d-93bd-5cf82c46f876";
    private const string TestChecksumKey = "618cc06cdcdcc939b290d7f8730b5c65bae420ef310fa049f735f1609efae110";

    private readonly PayOsOptions _options = new()
    {
        ClientId = TestClientId,
        ApiKey = TestApiKey,
        ChecksumKey = TestChecksumKey,
        WebhookUrl = "https://extra-efficient.local/api/payments/webhook"
    };

    [Theory]
    [InlineData("PAID", "paid")]
    [InlineData("paid", "paid")]
    [InlineData("CANCELLED", "failed")]
    [InlineData("canceled", "failed")]
    [InlineData("EXPIRED", "expired")]
    [InlineData("PENDING", "pending")]
    [InlineData("PROCESSING", "pending")]
    [InlineData("unknown_status", "pending")]
    [InlineData(null, "pending")]
    public void MapPayOsStatus_correctly_maps_to_order_status(string? input, string expected)
    {
        var result = PayOsPaymentGateway.MapPayOsStatus(input);
        Assert.Equal(expected, result.ToCode());
    }

    [Theory]
    [InlineData("00", "paid")]
    [InlineData("01", "failed")]
    [InlineData("99", "failed")]
    [InlineData(null, "failed")]
    public void MapWebhookCodeToStatus_correctly_maps_code(string? code, string expected)
    {
        var result = PayOsPaymentGateway.MapWebhookCodeToStatus(code);
        Assert.Equal(expected, result.ToCode());
    }

    [Fact]
    public void GenerateHmacSha256Signature_computes_expected_hash()
    {
        var data = "amount=100000&description=Credits 5&orderCode=123";
        var hash = PayOsPaymentGateway.GenerateHmacSha256Signature(data, TestChecksumKey);

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    [Fact]
    public async Task VerifyWebhookAsync_rejects_empty_or_whitespace_payload()
    {
        var gateway = new PayOsPaymentGateway(_options);

        var resEmpty = await gateway.VerifyWebhookAsync("", null);
        var resWhitespace = await gateway.VerifyWebhookAsync("   ", null);

        Assert.False(resEmpty.IsValid);
        Assert.Equal(GatewayStatus.Failed, resEmpty.Status);
        Assert.False(resWhitespace.IsValid);
    }

    [Fact]
    public async Task VerifyWebhookAsync_rejects_malformed_json()
    {
        var gateway = new PayOsPaymentGateway(_options);

        var res = await gateway.VerifyWebhookAsync("{ invalid json ", "some-sig");

        Assert.False(res.IsValid);
        Assert.Equal(GatewayStatus.Failed, res.Status);
    }

    [Fact]
    public async Task VerifyWebhookAsync_rejects_missing_signature()
    {
        var gateway = new PayOsPaymentGateway(_options);
        var payload = """
            {
              "code": "00",
              "desc": "success",
              "data": {
                "orderCode": 1001,
                "amount": 50000
              }
            }
            """;

        var res = await gateway.VerifyWebhookAsync(payload, null);

        Assert.False(res.IsValid);
        Assert.Equal(GatewayStatus.Failed, res.Status);
        Assert.Equal("MISSING_SIGNATURE", res.ErrorCode);
    }

    [Fact]
    public async Task VerifyWebhookAsync_rejects_tampered_signature()
    {
        var gateway = new PayOsPaymentGateway(_options);
        var payload = """
            {
              "code": "00",
              "desc": "success",
              "data": {
                "orderCode": 1001,
                "amount": 50000,
                "description": "Test",
                "accountNumber": "123",
                "reference": "REF1",
                "transactionDateTime": "2026-10-14 12:00:00",
                "currency": "VND",
                "paymentLinkId": "link1",
                "code": "00"
              },
              "signature": "0000000000000000000000000000000000000000000000000000000000000000"
            }
            """;

        var res = await gateway.VerifyWebhookAsync(payload, null);

        Assert.False(res.IsValid);
        Assert.Equal(GatewayStatus.Failed, res.Status);
    }

    [Fact]
    public async Task VerifyWebhookAsync_verifies_authentic_signature_and_detects_tampered_payload()
    {
        var gateway = new PayOsPaymentGateway(_options);

        var dataObj = new WebhookData
        {
            OrderCode = 2001,
            Amount = 50000,
            Description = "Credits 5",
            AccountNumber = "12345678",
            Reference = "REF-2001",
            TransactionDateTime = "2026-10-14 20:00:00",
            Currency = "VND",
            PaymentLinkId = "plink-2001",
            Code = "00"
        };

        var signature = PayOsPaymentGateway.CreateWebhookSignature(dataObj, TestChecksumKey);

        var authenticPayload = $$"""
            {
              "code": "00",
              "desc": "success",
              "success": true,
              "data": {
                "orderCode": 2001,
                "amount": 50000,
                "description": "Credits 5",
                "accountNumber": "12345678",
                "reference": "REF-2001",
                "transactionDateTime": "2026-10-14 20:00:00",
                "currency": "VND",
                "paymentLinkId": "plink-2001",
                "code": "00"
              },
              "signature": "{{signature}}"
            }
            """;

        var validResult = await gateway.VerifyWebhookAsync(authenticPayload, null);
        Assert.True(validResult.IsValid, $"Verification failed with error: {validResult.ErrorCode}");
        Assert.Equal(2001, validResult.PaymentNo);
        Assert.Equal("plink-2001", validResult.GatewayTxnRef);
        Assert.Equal(GatewayStatus.Paid, validResult.Status);
        Assert.Equal("00", validResult.ErrorCode);

        // Tamper 1: change amount from 50000 to 10000 while keeping original signature
        var tamperedAmountPayload = authenticPayload.Replace("50000", "10000");
        var tamperedAmountResult = await gateway.VerifyWebhookAsync(tamperedAmountPayload, null);
        Assert.False(tamperedAmountResult.IsValid);

        // Tamper 2: change orderCode from 2001 to 9999 while keeping original signature
        var tamperedOrderPayload = authenticPayload.Replace("2001", "9999");
        var tamperedOrderResult = await gateway.VerifyWebhookAsync(tamperedOrderPayload, null);
        Assert.False(tamperedOrderResult.IsValid);

        // Header signature support: signature passed via header parameter instead of body
        var payloadWithoutSignature = $$"""
            {
              "code": "00",
              "desc": "success",
              "success": true,
              "data": {
                "orderCode": 2001,
                "amount": 50000,
                "description": "Credits 5",
                "accountNumber": "12345678",
                "reference": "REF-2001",
                "transactionDateTime": "2026-10-14 20:00:00",
                "currency": "VND",
                "paymentLinkId": "plink-2001",
                "code": "00"
              }
            }
            """;
        var headerResult = await gateway.VerifyWebhookAsync(payloadWithoutSignature, signature);
        Assert.True(headerResult.IsValid);
        Assert.Equal(2001, headerResult.PaymentNo);
    }

    [Fact]
    public async Task VerifyWebhookAsync_with_non_00_code_returns_failed_status()
    {
        var gateway = new PayOsPaymentGateway(_options);

        var dataObj = new WebhookData
        {
            OrderCode = 3001,
            Amount = 50000,
            Description = "Credits 5",
            AccountNumber = "12345678",
            Reference = "REF-3001",
            TransactionDateTime = "2026-10-14 20:00:00",
            Currency = "VND",
            PaymentLinkId = "plink-3001",
            Code = "01"
        };

        var signature = PayOsPaymentGateway.CreateWebhookSignature(dataObj, TestChecksumKey);

        var failedPayload = $$"""
            {
              "code": "00",
              "desc": "success",
              "success": true,
              "data": {
                "orderCode": 3001,
                "amount": 50000,
                "description": "Credits 5",
                "accountNumber": "12345678",
                "reference": "REF-3001",
                "transactionDateTime": "2026-10-14 20:00:00",
                "currency": "VND",
                "paymentLinkId": "plink-3001",
                "code": "01"
              },
              "signature": "{{signature}}"
            }
            """;

        var result = await gateway.VerifyWebhookAsync(failedPayload, null);
        Assert.True(result.IsValid);
        Assert.Equal(GatewayStatus.Failed, result.Status);
        Assert.Equal("01", result.ErrorCode);
    }

    [Fact]
    public async Task CancelPaymentLinkAsync_handles_nonexistent_or_network_error_gracefully()
    {
        var gateway = new PayOsPaymentGateway(_options);

        // When cancelling a non-existent payment link, it should catch error, log, and return false
        var result = await gateway.CancelPaymentLinkAsync(999999, "non-existent-link", "Order expired");
        Assert.False(result);
    }

    [Fact]
    public async Task QueryPaymentStatusAsync_handles_nonexistent_or_network_error_gracefully()
    {
        var gateway = new PayOsPaymentGateway(_options);

        // When querying non-existent order, it returns pending status with error details without throwing
        var result = await gateway.QueryPaymentStatusAsync(999999, "non-existent-link");
        Assert.Equal(GatewayStatus.Pending, result.Status);
        Assert.NotNull(result.ErrorCode);
    }

    [Fact]
    public async Task Post_live_simulated_webhook_for_order_3()
    {
        var gateway = new PayOsPaymentGateway(_options);
        var dataObj = new WebhookData
        {
            OrderCode = 3,
            Amount = 190000,
            Description = "Credits 10",
            AccountNumber = "12345678",
            Reference = "REF-3",
            TransactionDateTime = "2026-10-16 15:40:50",
            Currency = "VND",
            PaymentLinkId = "plink-3",
            Code = "00"
        };
        var signature = PayOsPaymentGateway.CreateWebhookSignature(dataObj, _options.ChecksumKey);
        var payload = $$"""
            {
              "code": "00",
              "desc": "success",
              "success": true,
              "data": {
                "orderCode": 3,
                "amount": 190000,
                "description": "Credits 10",
                "accountNumber": "12345678",
                "reference": "REF-3",
                "transactionDateTime": "2026-10-16 15:40:50",
                "currency": "VND",
                "paymentLinkId": "plink-3",
                "code": "00"
              },
              "signature": "{{signature}}"
            }
            """;

        using var client = new HttpClient();
        var content = new StringContent(payload, System.Text.Encoding.UTF8, System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json"));
        content.Headers.Add("x-signature", signature);
        var resp = await client.PostAsync("http://localhost:5001/api/payments/webhook", content);
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
    }
}
