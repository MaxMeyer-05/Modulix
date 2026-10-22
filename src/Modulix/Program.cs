using Serilog;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;

using Modulix.Scanner;
using Modulix.Scanner.Interfaces;

using Modulix.Services;
using Modulix.Services.Interfaces;

using Modulix.Docker;
using Modulix.Docker.Options;
using Modulix.Docker.Services;

using Modulix;
using Modulix.Mappers;
using Modulix.Extensions;
using Modulix.Infrastructure;

using Modulix.Models.Options;
using Modulix.Database.DbContexts;

using Modulix.ReverseProxy;
using Modulix.ReverseProxy.Services;

using Modulix.Client.Models.Enums;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

builder.Services.AddOptions<ModulixOptions>()
    .Bind(builder.Configuration.GetSection(ModulixOptions.SectionName))
    .ValidateDataAnnotations()
    .PostConfigure<IHostEnvironment>((options, env) =>
    {
        if (!string.IsNullOrWhiteSpace(options.StorageBasePath))
            options.StorageBasePath = Path.GetFullPath(options.StorageBasePath, env.ContentRootPath);
    })
    .ValidateOnStart();

builder.Services.AddOptions<DockerOptions>()
    .Bind(builder.Configuration.GetSection(DockerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddScoped<DirectoryExtension>();
builder.Services.AddScoped<ModuleEndpointExtension>();
builder.Services.AddScoped<IModuleService, ModuleService>();
builder.Services.AddScoped<IModuleVersionService, ModuleVersionService>();
builder.Services.AddScoped<ISubEndpointService, SubEndpointService>();
builder.Services.AddScoped<IScannerService, ScannerService>();

builder.Services.AddSingleton<IModuleEndpointScanner, ModuleEndpointScanner>();
builder.Services.AddDockerServices();
builder.Services.AddReverseProxyServices();

builder.Services.AddDbContext<ServerContext>(options =>
    options.UseSqlite
    (
        builder.Configuration.GetConnectionString("ServerDatabase"),
        x => x.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
    ));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Modulix API",
        Version = FileVersionInfo
            .GetVersionInfo(
                typeof(Program).Assembly.Location)
            .ProductVersion
    });

    options.IncludeXmlComments(
        Path.Combine(
            AppContext.BaseDirectory,
            $"{typeof(Program).Assembly.GetName().Name}.xml")
    );

    // Ermöglicht die Token-Eingabe in Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Copy the JWT-Access-Token from Keycloak here:"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

builder.Services
    .AddAuthorization()
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authOptions = builder.Configuration
            .GetSection(AuthorizationSettings.SectionName)
            .Get<AuthorizationSettings>()
            ?? AuthorizationSettings.Default;

        options.Authority = authOptions.Authority;
        options.RequireHttpsMetadata = authOptions.Authority.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = authOptions.Authority,
            ValidateAudience = !string.IsNullOrEmpty(authOptions.Audience),
            ValidAudience = authOptions.Audience?.Trim()
        };
    });


builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
});

var httpsSettings = builder.Configuration
    .GetSection(HttpsSettings.SectionName)
    .Get<HttpsSettings>()
    ?? HttpsSettings.Default;

if (httpsSettings.Enabled)
{
    if (httpsSettings.Port is int httpsPort)
    {
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = httpsPort);
    }

    if (!string.IsNullOrWhiteSpace(httpsSettings.CertificatePath)
        && !httpsSettings.CertificatePath.StartsWith('<'))
    {
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ConfigureHttpsDefaults(httpsOptions =>
            {
                httpsOptions.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    httpsSettings.CertificatePath,
                    httpsSettings.CertificatePassword);
            });
        });
    }
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServerContext>();
    await dbContext.Database.MigrateAsync();

    var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
    var reverseProxyService = scope.ServiceProvider.GetRequiredService<IReverseProxyService>();
    var candidateModules = await dbContext.Modules
        .Include(m => m.SubEndpoints)
        .Where(m => m.Status == ModuleStatus.Running)
        .ToListAsync();

    var activeProxyModules = new List<Modulix.ReverseProxy.Models.ProxyModuleDefinition>();
    var statusChanged = false;

    foreach (var module in candidateModules)
    {
        var isRunning = !string.IsNullOrWhiteSpace(module.ContainerId)
            && await dockerService.IsContainerRunningAsync(module.ContainerId);

        if (isRunning)
        {
            activeProxyModules.Add(module.ToProxyModuleDefinition());
        }
        else
        {
            module.Status = ModuleStatus.Failed;
            statusChanged = true;
        }
    }

    if (statusChanged)
    {
        await dbContext.SaveChangesAsync();
    }

    reverseProxyService.UpdateModules(activeProxyModules);
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
    
    app.MapSwagger();
    app.MapSwaggerUI();
}

if (httpsSettings.Enabled)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapModulixReverseProxy();
app.Run();
