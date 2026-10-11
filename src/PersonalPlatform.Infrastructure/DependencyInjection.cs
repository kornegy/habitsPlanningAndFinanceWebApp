using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PersonalPlatform.Application.Auth;
using PersonalPlatform.Application.Common;
using PersonalPlatform.Application.Profile;
using PersonalPlatform.Contracts.Auth;
using PersonalPlatform.Infrastructure.Email;
using PersonalPlatform.Infrastructure.Identity;
using PersonalPlatform.Infrastructure.Persistence;

namespace PersonalPlatform.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(TimeProvider.System);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema);
                npgsql.CommandTimeout(30);
            })
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<ITextSearch, PostgresTextSearch>();

        AddIdentity(services, configuration);
        AddTokens(services, configuration, environment);
        AddEmail(services, configuration, environment);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<AdminStatsService>();
        services.AddScoped<SessionValidator>();
        services.AddHostedService<SessionCleanupService>();
        services.AddHostedService<AdminRoleBootstrapper>();
        return services;
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIdentityCore<AppUser>(options =>
            {
                // Length-based policy (NIST SP 800-63B): long passphrases instead of composition rules.
                options.Password.RequiredLength = PasswordPolicy.MinLength;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 8;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(3));
        // Data Protection keys sign email-confirmation and password-reset tokens. They are stored in the
        // database so all instances share them; in production they should additionally be encrypted with a
        // certificate (DataProtection:CertificatePath), so that read access to the database alone is not
        // enough to forge password-reset tokens.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("PersonalPlatform")
            .PersistKeysToDbContext<AppDbContext>();
        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrEmpty(certificatePath))
        {
            dataProtection.ProtectKeysWithCertificate(
                X509CertificateLoader.LoadPkcs12FromFile(certificatePath, configuration["DataProtection:CertificatePassword"]));
        }
    }

    private static void AddTokens(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SessionOptions>().Bind(configuration.GetSection(SessionOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AccountEmailOptions>().Bind(configuration.GetSection(AccountEmailOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            // Convenience for local runs only: without configured keys, generate an ephemeral key per process.
            // Access tokens then expire on restart (clients refresh transparently). Never used in Production,
            // where startup fails if no key is configured.
            services.PostConfigure<JwtOptions>(o =>
            {
                if (o.SigningKeys.Count == 0)
                {
                    o.SigningKeys.Add(new JwtSigningKeyOptions { KeyId = "dev-ephemeral", Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
                    o.ActiveKeyId = "dev-ephemeral";
                }
            });
        }

        services.AddSingleton<JwtKeyRing>();
        services.AddSingleton<AccessTokenIssuer>();
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.Mode == EmailDeliveryMode.Smtp || environment.IsDevelopment() || environment.IsEnvironment("Testing"),
                "Email:Mode=PickupDirectory is only allowed in Development.")
            .ValidateOnStart();
        services.AddSingleton<EmailQueue>();
        services.AddSingleton<AccountEmails>();
        services.AddSingleton<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>();
            return options.Value.Mode == EmailDeliveryMode.PickupDirectory
                ? new PickupDirectoryEmailSender(options)
                : new SmtpEmailSender(options);
        });
        services.AddHostedService<EmailDispatcher>();
    }
}
