using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using PersonaOS.Application;
using PersonaOS.Infrastructure;
using PersonaOS.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options =>
{
    options.Filters.Add<PersonaOS.Api.Infrastructure.DomainValidationExceptionFilter>();
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Clean architecture composition root: use cases + port adapters.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Delivers due reminders to registered devices without the app being open.
builder.Services.AddHostedService<PersonaOS.Api.Infrastructure.ReminderDispatchService>();
// Sends the morning brief / evening rollup at the user's configured local times.
builder.Services.AddHostedService<PersonaOS.Api.Infrastructure.ProactiveScheduleService>();

// Data Protection encrypts the Anthropic API key at rest. Persist keys to a
// configured path (the mounted data volume in Docker) so the ciphertext stays
// decryptable across restarts/upgrades. In dev, the OS default store is used.
var dpProtection = builder.Services.AddDataProtection().SetApplicationName("PersonaOS");
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    Directory.CreateDirectory(dpKeysPath);
    dpProtection.PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
}

// JWT bearer validation (web-host concern).
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwt.SigningKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Apply pending EF Core migrations on startup so a `docker compose pull`
// upgrade migrates the schema automatically for self-hosters.
await app.Services.MigrateDatabaseAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
