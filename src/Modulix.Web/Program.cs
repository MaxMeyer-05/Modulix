using System.Security.Claims;

using Serilog;

using Modulix.Web;
using Modulix.Web.Services;
using Modulix.Client;


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Duende.AccessTokenManagement;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IUserActivityService, UserActivityService>();

var authSettings = builder.Configuration
    .GetRequiredSection(AuthorizationSettings.SectionName)
    .Get<AuthorizationSettings>()!;

var serviceClient = ClientCredentialsClientName.Parse("modulix_Service");
builder.Services.AddClientCredentialsTokenManagement()
    .AddClient(serviceClient, client =>
    {
        client.TokenEndpoint = new Uri($"{authSettings.Authority.TrimEnd('/')}/protocol/openid-connect/token");
        client.ClientId = ClientId.Parse(authSettings.Service.ClientId);
        client.ClientSecret = ClientSecret.Parse(authSettings.Service.ClientSecret);
    });

builder.Services.AddModulixApi(
    builder.Configuration["ModulixApi:BaseUrl"]
        ?? throw new InvalidOperationException("ModulixApi:BaseUrl is missing."))
        .AddClientCredentialsTokenHandler(serviceClient);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Events = new CookieAuthenticationEvents
        {
            OnSigningIn = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity
                    && !identity.HasClaim(c => c.Type == UserActivityService.SessionClaimType))
                {
                    var oidcSid = identity.FindFirst("sid")?.Value
                        ?? identity.FindFirst(ClaimTypes.Sid)?.Value
                        ?? identity.FindFirst("session_state")?.Value
                        ?? Guid.NewGuid().ToString("N");

                    identity.AddClaim(new Claim(UserActivityService.SessionClaimType, oidcSid));
                }

                return Task.CompletedTask;
            },
            OnSigningOut = context =>
            {
                var activityService = context.HttpContext.RequestServices.GetService<IUserActivityService>();
                activityService?.Clear(context.HttpContext.User);
                return Task.CompletedTask;
            }
        };
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = authSettings.Authority;
        options.ClientId = authSettings.Client.ClientId;
        options.ClientSecret = authSettings.Client.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        
        options.RequireHttpsMetadata = authSettings.Authority.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets()
    .AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
