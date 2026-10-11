using System.Reflection;
using PersonalPlatform.Api.Security;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Client.Services;
using PersonalPlatform.Contracts;
using PersonalPlatform.Domain.Common;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.ArchitectureTests;

/// <summary>Dependency rules between the modular monolith's layers, checked on the compiled assemblies.</summary>
public sealed class LayeringTests
{
    internal static readonly Assembly Domain = typeof(IOwnedEntity).Assembly;
    internal static readonly Assembly Contracts = typeof(PagedResult<>).Assembly;
    internal static readonly Assembly Application = typeof(IAppDbContext).Assembly;
    internal static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;
    internal static readonly Assembly Api = typeof(SecurityOptions).Assembly;
    internal static readonly Assembly Client = typeof(Loc).Assembly;

    [Fact]
    public void Domain_depends_only_on_the_base_class_library() =>
        Assert.DoesNotContain(References(Domain), r => !IsBaseClassLibrary(r));

    [Fact]
    public void Contracts_depend_only_on_the_base_class_library() =>
        Assert.DoesNotContain(References(Contracts), r => !IsBaseClassLibrary(r));

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_database_drivers() =>
        Assert.DoesNotContain(References(Application), r =>
            r is "PersonalPlatform.Infrastructure" or "PersonalPlatform.Api" or "PersonalPlatform.Client"
            || r.StartsWith("Npgsql", StringComparison.Ordinal)
            || r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || r.StartsWith("MailKit", StringComparison.Ordinal));

    [Fact]
    public void Infrastructure_does_not_depend_on_the_web_layer() =>
        Assert.DoesNotContain(References(Infrastructure), r => r is "PersonalPlatform.Api" or "PersonalPlatform.Client" || r.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal));

    [Fact]
    public void Client_talks_to_the_server_only_through_contracts() =>
        Assert.DoesNotContain(References(Client), r =>
            r is "PersonalPlatform.Domain" or "PersonalPlatform.Application" or "PersonalPlatform.Infrastructure" or "PersonalPlatform.Api"
            || r.StartsWith("Npgsql", StringComparison.Ordinal)
            || r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || r.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal));

    [Fact]
    public void Domain_entities_cannot_be_mass_assigned()
    {
        // Every persisted aggregate changes state only through methods that enforce its rules.
        var publicSetters = Domain.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IOwnedEntity).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(publicSetters);
    }

    [Fact]
    public void Owned_entities_expose_an_owner_id()
    {
        var owned = Domain.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IOwnedEntity).IsAssignableFrom(t)).ToList();

        Assert.NotEmpty(owned);
        Assert.All(owned, t => Assert.Equal(typeof(Guid), t.GetProperty(nameof(IOwnedEntity.OwnerId))?.PropertyType));
    }

    [Fact]
    public void Application_services_live_in_feature_namespaces()
    {
        var misplaced = Application.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && t.Name.EndsWith("Service", StringComparison.Ordinal))
            .Where(t => t.Namespace is null || t.Namespace.EndsWith(".Common", StringComparison.Ordinal) || t.Namespace == "PersonalPlatform.Application")
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(misplaced);
    }

    private static List<string> References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!).Order(StringComparer.Ordinal)];

    private static bool IsBaseClassLibrary(string name) =>
        name is "netstandard" or "mscorlib" || name.StartsWith("System", StringComparison.Ordinal);
}
