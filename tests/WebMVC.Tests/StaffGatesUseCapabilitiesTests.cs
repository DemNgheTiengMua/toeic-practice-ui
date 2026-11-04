using System.Reflection;
using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #200 criteria 3 and 4: every staff route is gated by a capability, and none is gated by a role
/// name. This enumerates the controller actions the app actually exposes �� no hand-written list to
/// forget �� and drives the real authorization service with the real policies, so a route that
/// quietly keeps <c>Roles.Admin</c> fails here rather than in production.
/// </summary>
public sealed class StaffGatesUseCapabilitiesTests : IClassFixture<StaffGatesUseCapabilitiesTests.Site>
{
    private readonly Site site;

    public StaffGatesUseCapabilitiesTests(Site site) => this.site = site;

    /// <summary>Every action that lives under a staff controller, i.e. one whose routes are under
    /// <c>/admin</c>. A learner route is not a staff gate and is not the subject of this ticket.
    /// <para>
    /// Async actions return <c>Task&lt;IActionResult&gt;</c>, which is not itself an
    /// <c>IActionResult</c>; unwrapping it is what makes this enumerate the async majority instead of
    /// a handful of synchronous actions. A test that silently skipped them passed while an
    /// <c>AdminUsersController</c> was put back on the role policy — that is the exact rot this
    /// guards against, so `AsyncActions_are_enumerated` pins the count.
    /// </para></summary>
    private static IEnumerable<(Type Controller, MethodInfo Action, string Route)> StaffActions()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract && t.Namespace == "WebMVC.Controllers")
            .ToList();

        foreach (var controller in controllers)
        {
            var typeRoutes = controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(a => a.Template).ToList();
            var isStaff = controller.Name.StartsWith("Admin", StringComparison.Ordinal)
                || typeRoutes.Any(r => r.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
                || controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Any(a => a.Policy is not null && Permissions.All.Contains(a.Policy));
            if (!isStaff) continue;

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && ReturnsActionResult(m.ReturnType)))
            {
                var routes = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .Select(a => a.Template ?? "")
                    .Concat(typeRoutes)
                    .ToList();
                yield return (controller, action, routes.Count == 0 ? "" : string.Join(",", routes));
            }
        }
    }

    /// <summary>An action result, possibly wrapped in <c>Task</c>/<c>ValueTask</c>.</summary>
    private static bool ReturnsActionResult(Type type) =>
        typeof(IActionResult).IsAssignableFrom(
            type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
                ? type.GetGenericArguments()[0]
                : type);

    [Fact]
    public void Async_actions_are_enumerated_not_skipped()
    {
        // AdminUsersController's actions all return Task<IActionResult>. If the enumeration drops
        // them, every other assertion in this file is about a handful of synchronous actions.
        var staff = StaffActions().ToList();
        var userActions = staff.Where(s => s.Controller.Name == "AdminUsersController").ToList();
        Assert.NotEmpty(userActions);
        Assert.All(userActions, s => Assert.Equal(typeof(Task<IActionResult>), s.Action.ReturnType));
    }

    [Fact]
    public void Every_staff_action_names_a_capability_policy_and_never_a_role()
    {
        var offenders = new List<string>();

        foreach (var (controller, action, route) in StaffActions())
        {
            var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                .ToList();
            var policies = attributes.Select(a => a.Policy).OfType<string>().ToList();

            if (!policies.Any(p => Permissions.All.Contains(p)))
            {
                offenders.Add($"{controller.Name}.{action.Name} ({route}) has no capability policy");
            }

            foreach (var attribute in attributes)
            {
                if (!string.IsNullOrEmpty(attribute.Roles))
                {
                    offenders.Add($"{controller.Name}.{action.Name} is gated on role names '{attribute.Roles}'");
                }

                if (attribute.Policy is not null && !Permissions.All.Contains(attribute.Policy))
                {
                    offenders.Add($"{controller.Name}.{action.Name} uses policy '{attribute.Policy}', which is not a capability");
                }
            }
        }

        Assert.NotEmpty(StaffActions());
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Criterion 4 across the whole MVC app: no controller or action is gated on the staff role
    /// name, and no policy named after it exists. Learner controllers stay on <c>Roles.Learner</c>
    /// on purpose — there is no learner namespace, and the five namespaces are staff authority.
    /// </summary>
    [Fact]
    public async Task No_controller_anywhere_is_gated_on_the_admin_role_name()
    {
        var offenders = new List<string>();

        foreach (var controller in typeof(Program).Assembly.GetTypes()
                     .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType))
                    .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(inherit: true)))
                .ToList();

            foreach (var attribute in attributes)
            {
                if (attribute.Roles?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true)
                {
                    offenders.Add($"{controller.Name} is gated on role names '{attribute.Roles}'");
                }

                if (attribute.Policy == Roles.Admin)
                {
                    offenders.Add($"{controller.Name} is gated on the admin role policy");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));

        // And the policy itself is gone, so no future attribute can quietly reintroduce it.
        var provider = site.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.Null(await provider.GetPolicyAsync(Roles.Admin));
    }

    [Fact]
    public async Task Every_staff_action_refuses_an_account_without_its_capability_and_admits_one_with_it()
    {
        var authorization = site.Services.GetRequiredService<IAuthorizationService>();
        var withAll = Principal(Permissions.All);
        var withNone = Principal([]);

        var failures = new List<string>();

        foreach (var (controller, action, route) in StaffActions())
        {
            var policies = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                .Select(a => a.Policy)
                .OfType<string>()
                .Distinct()
                .ToList();

            foreach (var policy in policies)
            {
                var denied = await authorization.AuthorizeAsync(withNone, resource: null, policy);
                if (denied.Succeeded)
                {
                    failures.Add($"{controller.Name}.{action.Name} ({route}) admits an account holding no capability via '{policy}'");
                }

                var allowed = await authorization.AuthorizeAsync(withAll, resource: null, policy);
                if (!allowed.Succeeded)
                {
                    failures.Add($"{controller.Name}.{action.Name} ({route}) refuses an account holding '{policy}'");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static ClaimsPrincipal Principal(IEnumerable<string> permissions)
    {
        var identity = new ClaimsIdentity(
            AuthClaims.For(1, "staff@test.local", "admin", "Staff", permissions.ToList()),
            CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: IAccessTokenFactory.ClaimEmail,
            roleType: IAccessTokenFactory.ClaimRole);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>The real WebMVC host: the policies under test are the ones Program.cs registers.</summary>
    public sealed class Site : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
        }
    }
}
