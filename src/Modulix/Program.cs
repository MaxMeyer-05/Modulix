using Serilog;

using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;

using Modulix.Services;
using Modulix.Services.Interfaces;

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

builder.Services.AddDbContext<ServerContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ServerDatabase")));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Modulix",
        Version = "v1"
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
        Description = "Hier den JWT-Access-Token aus Keycloak einfügen (ohne 'Bearer ' davor):"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;
        options.Audience = builder.Configuration["Keycloak:Audience"];
        options.MetadataAddress = builder.Configuration["Keycloak:MetadataAddress"]!;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Keycloak:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Keycloak:Audience"],
            ValidateLifetime = true,
            RoleClaimType = "roles"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServerContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.MapSwagger();
    app.MapSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
