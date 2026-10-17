using Serilog;
using Docker.DotNet;
using System.Diagnostics;

using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;

using Modulix.Services;
using Modulix.Services.Interfaces;

using Modulix;
using Modulix.Extensions;
using Modulix.Infrastructure;
using Modulix.Database.DbContexts;
using Modulix.Models.Options;

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

builder.Services.AddScoped<IModuleService, ModuleService>();
builder.Services.AddScoped<DirectoryExtension>();
builder.Services.AddScoped<ModuleEndpointExtension>();

builder.Services.AddSingleton<IDockerService, DockerService>();
builder.Services.AddSingleton<IModuleEndpointScanner, ModuleEndpointScanner>();
builder.Services.AddSingleton<DockerClient>(_ =>
{
    var dockerUri = OperatingSystem.IsWindows()
        ? new Uri("npipe://./pipe/docker_engine")
        : new Uri("unix:///var/run/docker.sock");

    return new DockerClientConfiguration(dockerUri).CreateClient();
});

builder.Services.AddDbContext<ServerContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ServerDatabase")));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RL1Labs API",
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
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServerContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapOpenApi();
    
    app.MapSwagger();
    app.MapSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
