using EasyStock.Server.Configuration;
using EasyStock.Server.DB.Entities;
using EasyStock.Server.DB;
using EasyStock.Server.Interfaces;
using EasyStock.Server.Handlers.Admin;
using EasyStock.Server.Handlers.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EasyStock.Server.Helpers;

public static class AuthSetupHelper
{
    /// <summary>
    /// Identity (users only, no roles tables), JWT bearer authentication, the account services and, unless
    /// <paramref name="startupSteps"/> is false (OpenAPI generator), the startup step that loads the signing key
    /// and prints the setup token. Register it after the database migrator.
    /// </summary>
    public static IServiceCollection AddEasyStockAuth(this IServiceCollection services, bool startupSteps)
    {
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = AuthLimitsHelper.PasswordMinLength;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 1;
                // Lockout is handled by LoginHandler itself, so Identity's o.Lockout.* options are not set.
                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<EasyStockDbContext>()
            .AddPasswordValidator<MaxLengthPasswordValidatorHandler>();
        // The default validator rejects the null username of Guests.
        services.RemoveAll<IUserValidator<AppUser>>();
        services.AddScoped<IUserValidator<AppUser>, UsernameValidatorHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Configured on first use, so building the app (OpenAPI generator) needs no key or data directory.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ServerOptions>, SigningKeyHandler, TimeProvider>((o, server, keys, time) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = AccessTokenHandler.ValidationParameters(server.Value.PublicUrl, keys, time);
                o.Events = SessionValidationHelper.Events();
            });
        services.AddAuthorization();

        services.AddScoped<CurrentUserHandler>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserHandler>());
        services.AddSingleton<SigningKeyHandler>();
        services.AddSingleton<GuestSignupCounterHandler>();
        services.AddSingleton<QrContentHandler>();
        services.AddSingleton<AccessTokenHandler>();
        services.AddSingleton<RefreshTokenHandler>();
        services.AddScoped<SessionHandler>();
        services.AddHttpContextAccessor();
        services.AddSingleton<PasswordAttemptLimiter>();
        services.AddScoped<PasswordAttemptHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<SetupHandler>();
        services.AddScoped<InviteHandler>();
        services.AddScoped<PublicInviteHandler>();
        services.AddScoped<PasswordResetHandler>();
        services.AddScoped<PromotionHandler>();
        services.AddScoped<AccountHandler>();
        services.AddScoped<AdminUsersHandler>();

        if (startupSteps)
            services.AddHostedService<AuthStartupHandler>();
        return services;
    }
}
