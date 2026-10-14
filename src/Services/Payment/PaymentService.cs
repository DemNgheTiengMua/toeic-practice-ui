using System.Data;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class PaymentService(CompanionDbContext db, IPaymentGateway gateway) : IPaymentService
{
    public async Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(
        int userId,
        int packageId,
        CancellationToken cancellationToken = default)
    {
        await AssertUserEmailVerifiedAsync(userId, cancellationToken);
        await ExpireStaleOrdersAsync(olderThanMinutes: 15, cancellationToken);

        var package = await db.Packages
            .Where(p => p.PackageId == packageId && p.IsActive)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PackageNotFoundException(packageId);

        var currentBalance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        await ExpireStaleOrdersAsync(olderThanMinutes: 15, cancellationToken);

        var openOrder = await db.Orders
            .Include(o => o.Package)
            .Where(o => o.UserId == userId && (o.Status == "pending" || o.Status == "reconciling"))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        OrderDto? openOrderDto = openOrder is null ? null : ToOrderDto(openOrder);

        return new CheckoutPreviewDto(
            PackageId: package.PackageId,
            PackageName: package.Name,
            Credits: package.Credits,
            Price: package.Price,
            CurrentBalance: currentBalance,
            BalanceAfter: currentBalance + package.Credits,
            OpenOrder: openOrderDto);
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(
        int userId,
        int packageId,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        await AssertUserEmailVerifiedAsync(userId, cancellationToken);
        await ExpireStaleOrdersAsync(olderThanMinutes: 15, cancellationToken);

        var openOrder = await db.Orders
            .Include(o => o.Package)
            .Where(o => o.UserId == userId && (o.Status == "pending" || o.Status == "reconciling"))
            .FirstOrDefaultAsync(cancellationToken);

        if (openOrder is not null)
        {
            throw new OrderAlreadyOpenException(
                openOrder.OrderCode,
                openOrder.Package?.Name ?? "Credit Package",
                openOrder.Amount,
                openOrder.CreatedAt,
                openOrder.Status);
        }

        var package = await db.Packages
            .Where(p => p.PackageId == packageId && p.IsActive)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PackageNotFoundException(packageId);

        // Get next unique merchant payment number from SQL sequence without subquery wrapping
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR SEQ_PaymentNo;";
        if (db.Database.CurrentTransaction is not null)
        {
            command.Transaction = Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(db.Database.CurrentTransaction);
        }
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var paymentNo = Convert.ToInt64(scalar);

        var now = DateTimeOffset.UtcNow;
        var orderCode = $"DH-{now:yyyyMMdd}-{paymentNo:D4}";

        var returnUrl = $"{baseUrl.TrimEnd('/')}/payment/result?orderCode={Uri.EscapeDataString(orderCode)}";
        var cancelUrl = $"{baseUrl.TrimEnd('/')}/checkout?packageId={package.PackageId}";

        var linkResult = await gateway.CreatePaymentLinkAsync(new PaymentGatewayOrderInfo(
            PaymentNo: paymentNo,
            OrderCode: orderCode,
            Amount: package.Price,
            Description: $"Credits {package.Credits}",
            ReturnUrl: returnUrl,
            CancelUrl: cancelUrl), cancellationToken);

        var order = new Order
        {
            UserId = userId,
            PackageId = package.PackageId,
            Amount = package.Price,
            Status = "pending",
            OrderCode = orderCode,
            PaymentNo = paymentNo,
            Gateway = gateway.GatewayName,
            GatewayTxnRef = linkResult.GatewayTxnRef,
            CreatedAt = now
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponse(
            OrderId: order.OrderId,
            OrderCode: order.OrderCode,
            PaymentNo: order.PaymentNo,
            Amount: order.Amount,
            Gateway: order.Gateway,
            CheckoutUrl: linkResult.CheckoutUrl,
            Status: order.Status);
    }

    public async Task<PaymentResultDto> HandleReturnRedirectAsync(
        int userId,
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        long.TryParse(orderCode, out var parsedPaymentNo);
        var order = await db.Orders
            .Include(o => o.Package)
            .Where(o => (o.OrderCode == orderCode || (parsedPaymentNo > 0 && o.PaymentNo == parsedPaymentNo)) && o.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new OrderNotFoundException(orderCode);

        // ADR-0003: Every return redirect is logged as a payment event, but NEVER grants credit.
        var reportedStatus = order.Status switch
        {
            "reconciling" => "pending",
            _ => order.Status
        };

        var returnEvent = new PaymentEvent
        {
            OrderId = order.OrderId,
            Source = "return_redirect",
            ReportedStatus = reportedStatus,
            SignatureValid = false,
            WasApplied = false,
            Note = "Browser return redirect logged",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PaymentEvents.Add(returnEvent);
        await db.SaveChangesAsync(cancellationToken);

        if (order.Status == "pending")
        {
            // Perform trusted server-side status query
            var gatewayStatus = await gateway.QueryPaymentStatusAsync(order.PaymentNo, order.GatewayTxnRef, cancellationToken);
            if (gatewayStatus.Status == "paid")
            {
                await SettleOrderAsync(
                    order.PaymentNo,
                    source: "status_query",
                    signatureValid: false,
                    rawPayload: gatewayStatus.RawPayload,
                    note: "Settled upon return status query",
                    cancellationToken: cancellationToken);
            }
            else if (gatewayStatus.Status == "failed" || gatewayStatus.Status == "cancelled")
            {
                order.Status = "failed";
                order.GatewayErrorCode = gatewayStatus.ErrorCode ?? "24";
                db.PaymentEvents.Add(new PaymentEvent
                {
                    OrderId = order.OrderId,
                    Source = "status_query",
                    ReportedStatus = "failed",
                    SignatureValid = false,
                    WasApplied = true,
                    Payload = gatewayStatus.RawPayload,
                    Note = "Status query returned failed/cancelled",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (gatewayStatus.Status == "expired" || order.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-15))
            {
                order.Status = "expired";
                await gateway.CancelPaymentLinkAsync(order.PaymentNo, order.GatewayTxnRef, "Order expired", cancellationToken);
                db.PaymentEvents.Add(new PaymentEvent
                {
                    OrderId = order.OrderId,
                    Source = "cancel",
                    ReportedStatus = "expired",
                    SignatureValid = false,
                    WasApplied = true,
                    Payload = gatewayStatus.RawPayload,
                    Note = "Order expired upon return and cancelled at gateway",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Unconfirmed payment becomes reconciling ("Reconciling transaction")
                order.Status = "reconciling";
                db.PaymentEvents.Add(new PaymentEvent
                {
                    OrderId = order.OrderId,
                    Source = "status_query",
                    ReportedStatus = "pending",
                    SignatureValid = false,
                    WasApplied = true,
                    Payload = gatewayStatus.RawPayload,
                    Note = "Unconfirmed payment marked as reconciling",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return await GetPaymentResultAsync(userId, orderCode, cancellationToken);
    }

    public async Task<PaymentResultDto> GetPaymentResultAsync(
        int userId,
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        long.TryParse(orderCode, out var parsedPaymentNo);
        var order = await db.Orders
            .Include(o => o.Package)
            .Where(o => (o.OrderCode == orderCode || (parsedPaymentNo > 0 && o.PaymentNo == parsedPaymentNo)) && o.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new OrderNotFoundException(orderCode);

        // Check if reconciling or pending order should be checked again or has expired
        if (order.Status == "reconciling" || order.Status == "pending")
        {
            if (order.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-15))
            {
                order.Status = "expired";
                await gateway.CancelPaymentLinkAsync(order.PaymentNo, order.GatewayTxnRef, "Order expired", cancellationToken);
                db.PaymentEvents.Add(new PaymentEvent
                {
                    OrderId = order.OrderId,
                    Source = "cancel",
                    ReportedStatus = "expired",
                    SignatureValid = false,
                    WasApplied = true,
                    Note = "Reconciling order timed out and cancelled at gateway",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var gatewayStatus = await gateway.QueryPaymentStatusAsync(order.PaymentNo, order.GatewayTxnRef, cancellationToken);
                if (gatewayStatus.Status == "paid")
                {
                    await SettleOrderAsync(
                        order.PaymentNo,
                        source: "status_query",
                        signatureValid: false,
                        rawPayload: gatewayStatus.RawPayload,
                        note: "Settled during status refresh",
                        cancellationToken: cancellationToken);
                    // Refresh order state
                    order = await db.Orders
                        .Include(o => o.Package)
                        .Where(o => o.OrderCode == orderCode && o.UserId == userId)
                        .SingleAsync(cancellationToken);
                }
                else
                {
                    // ADR-0003: Every status query is logged as a payment event
                    var reportedStatus = gatewayStatus.Status switch
                    {
                        "reconciling" => "pending",
                        _ => gatewayStatus.Status
                    };
                    db.PaymentEvents.Add(new PaymentEvent
                    {
                        OrderId = order.OrderId,
                        Source = "status_query",
                        ReportedStatus = reportedStatus,
                        SignatureValid = false,
                        WasApplied = false,
                        Payload = gatewayStatus.RawPayload,
                        Note = "Status query checked during reconcile refresh",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        var balance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == userId)
            .Select(b => b.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        return new PaymentResultDto(
            OrderId: order.OrderId,
            OrderCode: order.OrderCode,
            Status: order.Status,
            PackageName: order.Package?.Name ?? "Credit Package",
            Credits: order.Package?.Credits ?? 0,
            Amount: order.Amount,
            Gateway: order.Gateway,
            CreatedAt: order.CreatedAt,
            PaidAt: order.PaidAt,
            CurrentBalance: balance,
            GatewayErrorCode: order.GatewayErrorCode);
    }

    public async Task<bool> SettleOrderAsync(
        long paymentNo,
        string source,
        bool signatureValid,
        string? rawPayload = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        // Credit is granted ONLY on a trusted source (status_query or verified webhook)
        if (source != "status_query" && !(source == "webhook" && signatureValid))
        {
            throw new InvalidOperationException("Credit can only be granted by a trusted source (status_query or verified webhook).");
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var order = await db.Orders
            .Include(o => o.Package)
            .Where(o => o.PaymentNo == paymentNo)
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return false;
        }

        // Idempotency: check if already credited
        var existingCreditTxn = await db.CreditTransactions
            .Where(t => t.OrderId == order.OrderId && t.Reason == "purchase")
            .FirstOrDefaultAsync(cancellationToken);

        if (existingCreditTxn is not null || order.Status == "paid")
        {
            // Log repeat event without applying
            db.PaymentEvents.Add(new PaymentEvent
            {
                OrderId = order.OrderId,
                Source = source,
                ReportedStatus = "paid",
                SignatureValid = signatureValid,
                WasApplied = false,
                Payload = rawPayload,
                Note = note ?? "Idempotent repeat confirmation (ignored)",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        order.Status = "paid";
        order.PaidAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var creditTxn = new CreditTransaction
        {
            UserId = order.UserId,
            Reason = "purchase",
            Delta = order.Package.Credits,
            OrderId = order.OrderId,
            CreatedAt = now
        };
        db.CreditTransactions.Add(creditTxn);

        var paymentEvent = new PaymentEvent
        {
            OrderId = order.OrderId,
            Source = source,
            ReportedStatus = "paid",
            SignatureValid = signatureValid,
            WasApplied = true,
            Payload = rawPayload,
            Note = note ?? "Order settled and credits granted",
            CreatedAt = now
        };
        db.PaymentEvents.Add(paymentEvent);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<WebhookVerificationResult> HandleWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        var result = await gateway.VerifyWebhookAsync(payload, signature, cancellationToken);
        if (!result.IsValid)
        {
            return result;
        }

        if (result.PaymentNo > 0)
        {
            if (result.Status == "paid")
            {
                await SettleOrderAsync(
                    result.PaymentNo,
                    source: "webhook",
                    signatureValid: true,
                    rawPayload: result.RawPayload,
                    note: "Settled via signature-verified webhook",
                    cancellationToken: cancellationToken);
            }
            else
            {
                var order = await db.Orders.FirstOrDefaultAsync(o => o.PaymentNo == result.PaymentNo, cancellationToken);
                if (order is not null && order.Status != "paid")
                {
                    order.Status = result.Status == "cancelled" || result.Status == "failed" ? "failed" : result.Status;
                    order.GatewayErrorCode = result.ErrorCode;
                    db.PaymentEvents.Add(new PaymentEvent
                    {
                        OrderId = order.OrderId,
                        Source = "webhook",
                        ReportedStatus = result.Status,
                        SignatureValid = true,
                        WasApplied = true,
                        Payload = result.RawPayload,
                        Note = "Order status updated via webhook",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        return result;
    }

    public async Task<PaymentResultDto> ApplyMockGatewayActionAsync(
        int userId,
        string orderCode,
        string action,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .Include(o => o.Package)
            .Where(o => o.OrderCode == orderCode && o.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new OrderNotFoundException(orderCode);

        if (gateway is IMockPaymentGateway mock)
        {
            switch (action.ToLowerInvariant())
            {
                case "success":
                    mock.SetOutcome(order.PaymentNo, "paid");
                    await SettleOrderAsync(
                        order.PaymentNo,
                        source: "status_query",
                        signatureValid: false,
                        rawPayload: "{\"status\":\"paid\"}",
                        note: "Settled via mock gateway success action",
                        cancellationToken: cancellationToken);
                    break;
                case "failed":
                    mock.SetOutcome(order.PaymentNo, "failed", errorCode ?? "24");
                    order.Status = "failed";
                    order.GatewayErrorCode = errorCode ?? "24";
                    db.PaymentEvents.Add(new PaymentEvent
                    {
                        OrderId = order.OrderId,
                        Source = "status_query",
                        ReportedStatus = "failed",
                        SignatureValid = false,
                        WasApplied = true,
                        Payload = "{\"status\":\"failed\"}",
                        Note = "Mock gateway failed action",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                case "expired":
                    mock.SetOutcome(order.PaymentNo, "expired");
                    order.Status = "expired";
                    db.PaymentEvents.Add(new PaymentEvent
                    {
                        OrderId = order.OrderId,
                        Source = "status_query",
                        ReportedStatus = "expired",
                        SignatureValid = false,
                        WasApplied = true,
                        Payload = "{\"status\":\"expired\"}",
                        Note = "Mock gateway expired action",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                case "pending":
                default:
                    mock.SetOutcome(order.PaymentNo, "pending");
                    order.Status = "reconciling";
                    db.PaymentEvents.Add(new PaymentEvent
                    {
                        OrderId = order.OrderId,
                        Source = "status_query",
                        ReportedStatus = "pending",
                        SignatureValid = false,
                        WasApplied = true,
                        Payload = "{\"status\":\"pending\"}",
                        Note = "Mock gateway slow pending action",
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    break;
            }
        }

        return await GetPaymentResultAsync(userId, orderCode, cancellationToken);
    }

    public async Task<WalletDto> GetWalletAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await ExpireStaleOrdersAsync(olderThanMinutes: 15, cancellationToken);

        // Read credit aggregates from views
        var summary = await db.vw_UserCreditSummaries
            .Where(s => s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);

        var totals = await db.vw_UserPurchaseTotals
            .Where(t => t.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);

        var orders = await db.Orders
            .Include(o => o.Package)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var ledger = await db.CreditTransactions
            .Include(t => t.Order)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var balance = summary?.Balance ?? 0;
        var creditsPurchased = summary?.CreditsPurchased ?? 0;
        var creditsUsed = summary?.CreditsUsed ?? 0;
        var amountPurchased = totals?.AmountPurchased ?? 0m;
        var paidOrderCount = totals?.PaidOrderCount ?? 0;

        return new WalletDto(
            Balance: balance,
            CreditsPurchased: creditsPurchased,
            CreditsUsed: creditsUsed,
            AmountPurchased: amountPurchased,
            PaidOrderCount: paidOrderCount,
            Orders: orders.Select(ToOrderDto).ToList(),
            Ledger: ledger.Select(t => new CreditLedgerEntryDto(
                TxnId: t.TxnId,
                Reason: t.Reason,
                Delta: t.Delta,
                Note: t.Note,
                CreatedAt: t.CreatedAt,
                OrderCode: t.Order?.OrderCode)).ToList());
    }

    public async Task ExpireStaleOrdersAsync(
        int olderThanMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-olderThanMinutes);
        var staleOrders = await db.Orders
            .Where(o => (o.Status == "pending" || o.Status == "reconciling") && o.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (staleOrders.Count > 0)
        {
            foreach (var order in staleOrders)
            {
                order.Status = "expired";
                await gateway.CancelPaymentLinkAsync(order.PaymentNo, order.GatewayTxnRef, "Order expired", cancellationToken);
                db.PaymentEvents.Add(new PaymentEvent
                {
                    OrderId = order.OrderId,
                    Source = "cancel",
                    ReportedStatus = "expired",
                    SignatureValid = false,
                    WasApplied = true,
                    Note = "Stale order expired and cancelled at gateway",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task AssertUserEmailVerifiedAsync(int userId, CancellationToken cancellationToken)
    {
        var emailVerifiedAt = await db.Users
            .Where(u => u.UserId == userId)
            .Select(u => u.EmailVerifiedAt)
            .SingleOrDefaultAsync(cancellationToken);

        if (emailVerifiedAt is null)
        {
            throw new EmailNotVerifiedException();
        }
    }

    private static OrderDto ToOrderDto(Order o) => new(
        OrderId: o.OrderId,
        OrderCode: o.OrderCode,
        PaymentNo: o.PaymentNo,
        PackageId: o.PackageId,
        PackageName: o.Package?.Name ?? "Credit Package",
        Credits: o.Package?.Credits ?? 0,
        Amount: o.Amount,
        Status: o.Status,
        Gateway: o.Gateway,
        GatewayTxnRef: o.GatewayTxnRef,
        GatewayErrorCode: o.GatewayErrorCode,
        CreatedAt: o.CreatedAt,
        PaidAt: o.PaidAt);
}
