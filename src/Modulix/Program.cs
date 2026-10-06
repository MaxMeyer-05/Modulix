using Serilog;

using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;

using Modulix.Services;
using Modulix.Services.Interfaces;

using Modulix.Infrastructure;
using Modulix.Database.DbContexts;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

builder.Services.AddScoped<IModuleService, ModuleService>();

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
            ValidIssuer = builder.Configuration["Keycloak:Issuer"],
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
