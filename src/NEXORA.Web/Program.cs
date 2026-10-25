using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Threading.RateLimiting;
using NEXORA.Application.Catalog;
using NEXORA.Application.Email;
using NEXORA.Application.Consultation;
using NEXORA.Application.Commerce;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Consultation;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Email;
using NEXORA.Web.Images;
using NEXORA.Web.Identity;

var commandArguments = ProvisioningCommandArguments.Parse(args);
var builder = WebApplication.CreateBuilder(commandArguments.HostArguments);

var databaseProfile = builder.Configuration["Database:Profile"];
var connectionString = DatabaseConnectionPolicy.Resolve(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    builder.Environment.EnvironmentName,
    databaseProfile);

builder.Services.AddNexoraInfrastructure(connectionString, builder.Environment.EnvironmentName, databaseProfile);
builder.Services.Configure<QwenConsultationOptions>(builder.Configuration.GetSection("ConsultationAI:Qwen"));
builder.Services.AddHttpClient<IConsultationAiAssistant, QwenConsultationAiAssistant>(client =>
{
    client.BaseAddress = new Uri("https://api.xkiro.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IProductImageStorage, LocalProductImageStorage>();
builder.Services.AddIdentity<AppUser, AppRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 4;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<NexoraUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders()
    .AddTokenProvider<StaffInvitationTokenProvider>(StaffInvitationPurpose.ProviderName);

builder.Services.Configure<StaffInvitationTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(24));
builder.Services.Configure<StaffInvitationOptions>(options =>
{
    builder.Configuration.GetSection("StaffInvitation").Bind(options);
    if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.PublicBaseUrl))
    {
        options.PublicBaseUrl = "http://127.0.0.1:5059";
    }
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/accessdenied";
    options.Cookie.Name = "NEXORA.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});
builder.Services.AddAuthoritativeAccountCookieValidation();

builder.Services.AddScoped<RoleSeeder>();
builder.Services.AddSingleton<InMemoryEmailMailbox>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IEmailSender, DevelopmentEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, UnconfiguredEmailSender>();
}

builder.Services.AddControllersWithViews();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("consultation-chat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
var vietnameseCulture = new CultureInfo("vi-VN");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(vietnameseCulture),
    SupportedCultures = [vietnameseCulture],
    SupportedUICultures = [vietnameseCulture]
});
// Uploads are created at runtime and are not included in the static asset manifest.
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

if (commandArguments.IsRequested)
{
    if (commandArguments.IsQaStaffProvisioningRequested)
    {
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("QA"))
        {
            app.Logger.LogCritical("QA staff provisioning is not available in the current environment.");
            Environment.ExitCode = 1;
        }
        else
        {
            Environment.ExitCode = await QaStaffProvisioningCommand.ExecuteAsync(
                app.Services,
                connectionString,
                app.Environment,
                app.Logger,
                app.Lifetime.ApplicationStopping);
        }

        return;
    }

    if (!DevelopmentSeedPolicy.IsAllowed(app.Environment.EnvironmentName, databaseProfile, connectionString))
    {
        app.Logger.LogCritical("The requested Development command requires an approved personal local database profile.");
        Console.WriteLine("Lệnh Development chỉ được phép với Database:Profile=PersonalDevelopment và database NEXORA_Dev_<Member> cục bộ.");
        Environment.ExitCode = 2;
        return;
    }

    Environment.ExitCode = commandArguments.IsDevelopmentDatabaseCheckRequested
        ? PrintDevelopmentDatabaseCheck()
        : commandArguments.IsDevelopmentDataSeedRequested
        ? await DevelopmentDataSeedingCommand.ExecuteAsync(
            app.Services,
            connectionString,
            databaseProfile,
            app.Environment.EnvironmentName,
            app.Logger,
            app.Lifetime.ApplicationStopping)
        : commandArguments.IsDevelopmentAccountsSeedRequested
        ? await DevelopmentAccountProvisioningCommand.ExecuteAsync(
            app.Services,
            app.Configuration,
            connectionString,
            databaseProfile,
            app.Environment.EnvironmentName,
            app.Logger,
            app.Lifetime.ApplicationStopping)
        : commandArguments.IsDevelopmentInventorySeedRequested
        ? await DevelopmentInventoryProvisioningCommand.ExecuteAsync(
            app.Services,
            connectionString,
            databaseProfile,
            app.Logger,
            app.Lifetime.ApplicationStopping)
        : await DevelopmentAdminProvisioningCommand.ExecuteAsync(
            app.Services,
            app.Logger,
            app.Lifetime.ApplicationStopping);
    return;
}

app.Run();

static int PrintDevelopmentDatabaseCheck()
{
    Console.WriteLine("Development connection policy accepted the PersonalDevelopment target. No database connection was opened.");
    return 0;
}

public partial class Program { }
