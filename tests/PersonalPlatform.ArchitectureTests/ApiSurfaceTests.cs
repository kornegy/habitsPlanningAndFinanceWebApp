using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PersonalPlatform.Api.Security;
using PersonalPlatform.Application.Common;

namespace PersonalPlatform.ArchitectureTests;

/// <summary>Rules for HTTP endpoints: deny-by-default, thin controllers, abuse limits and request shapes.</summary>
public sealed class ApiSurfaceTests
{
    /// <summary>The complete list of controller actions reachable without an access token.</summary>
    private static readonly HashSet<string> AnonymousActions =
    [
        "AuthController.Register",
        "AuthController.ConfirmEmail",
        "AuthController.ResendConfirmation",
        "AuthController.Login",
        "AuthController.Refresh",
        "AuthController.Logout",
        "AuthController.ForgotPassword",
        "AuthController.ResetPassword",
        "ProfileController.ReferenceData",
    ];

    /// <summary>Actions authenticated by the refresh cookie instead of a bearer token, which therefore need CSRF checks.</summary>
    private static readonly HashSet<string> CookieAuthenticatedActions = ["AuthController.Refresh", "AuthController.Logout"];

    private static readonly List<Type> Controllers = [.. LayeringTests.Api.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)];

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    [Fact]
    public void Controllers_are_sealed_api_controllers_under_versioned_routes()
    {
        Assert.NotEmpty(Controllers);
        Assert.All(Controllers, c =>
        {
            Assert.True(c.IsSealed, $"{c.Name} must be sealed");
            Assert.NotNull(c.GetCustomAttribute<ApiControllerAttribute>());
            var route = c.GetCustomAttribute<RouteAttribute>()?.Template;
            Assert.True(route?.StartsWith("api/v1/", StringComparison.Ordinal) == true, $"{c.Name} route '{route}' must start with api/v1/");
        });
    }

    [Fact]
    public void Only_the_allow_listed_actions_are_anonymous()
    {
        var anonymous = Controllers
            .SelectMany(c => Actions(c).Where(a => c.GetCustomAttribute<AllowAnonymousAttribute>() is not null || a.GetCustomAttribute<AllowAnonymousAttribute>() is not null))
            .Select(Name)
            .ToHashSet();

        Assert.Equal(AnonymousActions.Order(), anonymous.Order());
    }

    [Fact]
    public void Anonymous_actions_have_a_dedicated_rate_limit()
    {
        var unlimited = Controllers
            .SelectMany(Actions)
            .Where(a => AnonymousActions.Contains(Name(a)) && Name(a) != "ProfileController.ReferenceData")
            .Where(a => a.GetCustomAttribute<EnableRateLimitingAttribute>() is null && a.DeclaringType!.GetCustomAttribute<EnableRateLimitingAttribute>() is null)
            .Select(Name)
            .ToList();

        Assert.Empty(unlimited);
    }

    [Fact]
    public void Cookie_authenticated_actions_require_csrf_protection()
    {
        var actions = Controllers.SelectMany(Actions).Where(a => CookieAuthenticatedActions.Contains(Name(a))).ToList();

        Assert.Equal(CookieAuthenticatedActions.Count, actions.Count);
        Assert.All(actions, a => Assert.NotNull(a.GetCustomAttribute<RequireCsrfHeaderAttribute>()));
    }

    [Fact]
    public void Admin_endpoints_require_the_admin_role()
    {
        var admin = Controllers.Single(c => c.Name == "AdminController");

        Assert.Equal(Contracts.Admin.Roles.Admin, admin.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        Assert.DoesNotContain(Actions(admin), a => a.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }

    [Fact]
    public void Controllers_never_touch_the_database_directly()
    {
        var violations = Controllers
            .SelectMany(c => c.GetConstructors().SelectMany(ctor => ctor.GetParameters()).Select(p => (Controller: c.Name, p.ParameterType)))
            .Where(x => typeof(DbContext).IsAssignableFrom(x.ParameterType) || x.ParameterType == typeof(IAppDbContext))
            .Select(x => $"{x.Controller}: {x.ParameterType.Name}")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Owner_identity_never_comes_from_the_request()
    {
        // The owner is always the authenticated subject. Only email links carry a user id, and they are
        // bound to a single-use token issued for that user.
        var tokenBoundRequests = new[] { "ConfirmEmailRequest", "ResetPasswordRequest" };

        var actionParameters = Controllers.SelectMany(Actions)
            .SelectMany(a => a.GetParameters().Select(p => (Action: Name(a), p.Name, p.ParameterType)))
            .ToList();
        var ownerParameters = actionParameters
            .Where(p => p.Name is not null && (p.Name.Equals("ownerId", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("userId", StringComparison.OrdinalIgnoreCase)))
            .Select(p => $"{p.Action}({p.Name})");
        var ownerProperties = actionParameters
            .Select(p => p.ParameterType)
            .Where(t => t.Namespace?.StartsWith("PersonalPlatform.Contracts", StringComparison.Ordinal) == true && !tokenBoundRequests.Contains(t.Name))
            .SelectMany(t => t.GetProperties().Where(p => p.Name is "OwnerId" or "UserId").Select(p => $"{t.Name}.{p.Name}"));

        Assert.Empty(ownerParameters.Concat(ownerProperties).Distinct());
    }

    [Fact]
    public void Clients_cannot_submit_computed_balances_or_totals()
    {
        var forbidden = new[] { "Balance", "CurrentBalance", "Spent", "Remaining", "UsedPercent", "CompletedAt" };
        var violations = LayeringTests.Contracts.GetTypes()
            .Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties().Where(p => forbidden.Contains(p.Name)).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Every_action_accepts_a_cancellation_token_or_is_synchronous()
    {
        var missing = Controllers.SelectMany(Actions)
            .Where(a => typeof(Task).IsAssignableFrom(a.ReturnType) && a.GetParameters().All(p => p.ParameterType != typeof(CancellationToken)))
            .Select(Name)
            .ToList();

        Assert.Empty(missing);
    }
}
