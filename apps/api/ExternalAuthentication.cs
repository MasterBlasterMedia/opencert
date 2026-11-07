using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
namespace OpenCert.Api;

public static class ExternalAuthentication
{
    public static IServiceCollection AddExternalAuthentication(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var auth = services.AddAuthentication();
        bool Enabled(string name) => !string.IsNullOrWhiteSpace(config[$"Authentication:{name}:ClientId"]) && (!string.IsNullOrWhiteSpace(config[$"Authentication:{name}:ClientSecret"]) || name == "Apple" && !string.IsNullOrWhiteSpace(config["Authentication:Apple:PrivateKeyFile"]));
        void OAuth(string name, OAuthOptions o)
        {
            o.ClientId = config[$"Authentication:{name}:ClientId"]!; o.ClientSecret = config[$"Authentication:{name}:ClientSecret"]!;
            o.SignInScheme = IdentityConstants.ExternalScheme; o.CallbackPath = "/signin-" + name.ToLowerInvariant();
            o.SaveTokens = false; o.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5); o.BackchannelTimeout = TimeSpan.FromSeconds(15);
            o.Events.OnRemoteFailure = context => { context.HandleResponse(); context.Response.Redirect("/login?external=failed"); return Task.CompletedTask; };
        }
        if (Enabled("Google")) auth.AddGoogle("Google", o => { OAuth("Google", o); o.UsePkce = true; o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("profile"); });
        if (Enabled("Microsoft")) auth.AddMicrosoftAccount("Microsoft", o => { OAuth("Microsoft", o); o.UsePkce = true; o.Scope.Clear(); o.Scope.Add("User.Read"); });
        if (Enabled("GitHub")) auth.AddGitHub("GitHub", o => { OAuth("GitHub", o); o.UsePkce = true; o.Scope.Clear(); });
        if (Enabled("Discord")) auth.AddDiscord("Discord", o => { OAuth("Discord", o); o.UsePkce = true; o.Scope.Clear(); o.Scope.Add("identify"); });
        if (Enabled("Apple")) auth.AddOpenIdConnect("Apple", "Apple", o =>
        {
            o.Authority = "https://appleid.apple.com"; o.ClientId = config["Authentication:Apple:ClientId"]!;
            o.ClientSecret = config["Authentication:Apple:ClientSecret"];
            o.SignInScheme = IdentityConstants.ExternalScheme; o.CallbackPath = "/signin-apple";
            o.ResponseType = "code"; o.ResponseMode = "form_post"; o.UsePkce = false;
            o.Scope.Clear(); o.Scope.Add("openid"); o.SaveTokens = false;
            o.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5); o.BackchannelTimeout = TimeSpan.FromSeconds(15);
            o.ProtocolValidator.RequireNonce = true; o.TokenValidationParameters.ValidateIssuer = true; o.TokenValidationParameters.ValidateAudience = true;
            o.Events.OnAuthorizationCodeReceived = ctx =>
            {
                if (config["Authentication:Apple:PrivateKeyFile"] is { Length: > 0 } file)
                {
                    using var key = ECDsa.Create(); key.ImportFromPem(File.ReadAllText(file));
                    var now = DateTime.UtcNow;
                    ctx.TokenEndpointRequest!.ClientSecret = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                    {
                        Issuer = config["Authentication:Apple:TeamId"],
                        Audience = "https://appleid.apple.com",
                        IssuedAt = now,
                        NotBefore = now.AddSeconds(-30),
                        Expires = now.AddMinutes(5),
                        Claims = new Dictionary<string, object> { ["sub"] = o.ClientId },
                        SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = config["Authentication:Apple:KeyId"] }, SecurityAlgorithms.EcdsaSha256)
                    });
                }
                return Task.CompletedTask;
            };
            o.Events.OnRemoteFailure = context => { context.HandleResponse(); context.Response.Redirect("/login?external=failed"); return Task.CompletedTask; };
        });
        services.ConfigureExternalCookie(o => { o.Cookie.Name = "oc.external-ticket"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.ExpireTimeSpan = TimeSpan.FromMinutes(5); });
        return services;
    }
}
