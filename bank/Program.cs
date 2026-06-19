using Microsoft.EntityFrameworkCore;
using bank.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using bank.Components;
using bank.Services;
using bank.Providers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Wpisz token JWT. Przykład: 'eyJhbGciOiJIUzI1NiIs...'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddDbContext<BankDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/Keys"))
    .SetApplicationName("AmericanBank");

var jwtKey = builder.Configuration["JWT_KEY"] ?? "TajnyKluczBanku1234567890123456789";
var key = Encoding.UTF8.GetBytes(jwtKey);

System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services.AddAuthentication(x =>
    {
        x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(x =>
    {
        x.RequireHttpsMetadata = false;
        x.SaveToken = true;
        x.MapInboundClaims = false;
        x.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

// Konfiguracja HttpClient dla AuthService z dynamicznym BaseAddress
builder.Services.AddHttpClient();
builder.Services.AddScoped(sp => 
{
    var navManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navManager.BaseUri) };
});
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<bank.Services.ExternalPayments.AchService>();
builder.Services.AddScoped<bank.Services.ExternalPayments.RtpRegistrationService>();
builder.Services.AddScoped<bank.Services.ExternalPayments.RtpService>();
builder.Services.AddScoped<bank.Services.ExternalPayments.SwiftService>();
builder.Services.AddScoped<bank.Services.ExternalPayments.FedNowService>();
builder.Services.AddHostedService<bank.Services.ExternalPayments.RtpRegistrationService>();
builder.Services.AddHostedService<bank.Services.ExternalPayments.PaymentPollingBackgroundService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.UseSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BankDbContext>();
    dbContext.Database.Migrate();
    DbSeeder.Seed(dbContext);
}

app.Run();