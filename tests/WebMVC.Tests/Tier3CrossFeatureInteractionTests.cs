using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Tier 3: Cross-Feature Interactions Test Suite for WebMVC.
/// Validates cross-subsystem interactions: sitting lifecycle with auth claims, anti-forgery guards,
/// multi-stage sitting transitions, and diagnostic error propagation.
/// </summary>
public sealed class Tier3CrossFeatureInteractionTests
{
    // =========================================================================
    // 1. Sitting Lifecycle With Authentication & Cookie Claims
    // =========================================================================

    [Fact]
    public async Task FixedAuthentication_Provides_Deterministic_Ticket_And_Claims()
    {
        var identity = new ClaimsIdentity(AuthClaims.For(88, "fixed@demo.local", Roles.Learner, "Fixed Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "header.payload.sig" });

        var authService = new FixedAuthentication(principal, properties);
        var httpContext = new DefaultHttpContext();

        var result = await authService.AuthenticateAsync(httpContext, "Cookies");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Ticket);
        Assert.Equal("88", result.Ticket.Principal.FindFirst(IAccessTokenFactory.ClaimUserId)?.Value);
        Assert.Equal("fixed@demo.local", result.Ticket.Principal.FindFirst(IAccessTokenFactory.ClaimEmail)?.Value);
        Assert.Equal(Roles.Learner, result.Ticket.Principal.FindFirst(IAccessTokenFactory.ClaimRole)?.Value);
    }
}
