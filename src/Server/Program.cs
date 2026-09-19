using Serilog;

using Microsoft.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;

using Server.Services;
using Server.Models.Dtos;
using Server.Infrastructure;
using Server.Database.DbContexts;

using Server.Security.Tokens;
using Server.Security.Password;
using Server.Security.Authorization;
using Server.Security.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

builder.Services.AddScoped<UserSessionDataDto>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasherService>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddSingleton<IJwtTokenProvider, JwtTokenProvider>();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateOnStart();

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
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        options.TokenValidationParameters = jwtOptions.Value.CreateTokenValidationParameters();

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.HasValidSessionClaims() != true)
                {
                    context.Fail("The token does not contain a valid subject and role.");
                    return Task.CompletedTask;
                }

                var sessionDto = context.HttpContext.RequestServices.GetRequiredService<UserSessionDataDto>();
                context.Principal.PopulateSessionData(sessionDto);

                return Task.CompletedTask;
            }
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
