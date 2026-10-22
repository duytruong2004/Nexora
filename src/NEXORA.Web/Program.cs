using Microsoft.AspNetCore.Identity;
using System.Globalization;
using NEXORA.Application.Catalog;
using NEXORA.Application.Email;
using NEXORA.Application.Consultation;
using NEXORA.Application.Commerce;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Email;
using NEXORA.Web.Images;
using NEXORA.Web.Identity;

var commandArguments = ProvisioningCommandArguments.Parse(args);
var builder = WebApplication.CreateBuilder(commandArguments.HostArguments);

var connectionString = DatabaseConnectionPolicy.Resolve(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    builder.Environment.EnvironmentName);

builder.Services.AddNexoraInfrastructure(connectionString, builder.Environment.EnvironmentName);
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

// The QA staff provisioning command first verifies the existing QA role set read-only and never seeds roles.
// Normal startup and the pre-existing Development commands retain the idempotent role initializer.
if (!commandArguments.IsQaStaffProvisioningRequested)
{
    await using var scope = app.Services.CreateAsyncScope();
    var roleSeeder = scope.ServiceProvider.GetRequiredService<RoleSeeder>();
    await roleSeeder.EnsureRolesAsync();
}

if (app.Environment.IsDevelopment() && !commandArguments.IsRequested)
{
    if (DevelopmentInventoryProvisioningCommand.TargetsDevelopmentDatabase(connectionString))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var demoSeeder = scope.ServiceProvider.GetRequiredService<ICatalogDemoSeeder>();
        await demoSeeder.SeedAsync(app.Lifetime.ApplicationStopping);
        var consultationSeeder = scope.ServiceProvider.GetRequiredService<IConsultationDemoSeeder>();
        await consultationSeeder.SeedAsync(app.Lifetime.ApplicationStopping);
        var inventorySeeder = scope.ServiceProvider.GetRequiredService<IDevelopmentInventorySeeder>();
        await inventorySeeder.SeedMissingAsync(app.Lifetime.ApplicationStopping);
    }
    else
    {
        app.Logger.LogWarning("Development demo seeders skipped because the configured target is not PC01\\SQLEXPRESS / NEXORA. This prevents QA databases from receiving unintended demo changes.");
    }
}

if (commandArguments.IsRequested)
{
    var environmentAllowed = commandArguments.IsQaStaffProvisioningRequested
        ? app.Environment.IsDevelopment() || app.Environment.IsEnvironment("QA")
        : app.Environment.IsDevelopment();
    if (!environmentAllowed)
    {
        app.Logger.LogCritical("This provisioning command is not available in the current environment.");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = commandArguments.IsQaStaffProvisioningRequested
        ? await QaStaffProvisioningCommand.ExecuteAsync(
            app.Services,
            connectionString,
            app.Environment,
            app.Logger,
            app.Lifetime.ApplicationStopping)
        : commandArguments.IsDevelopmentInventorySeedRequested
        ? await DevelopmentInventoryProvisioningCommand.ExecuteAsync(
            app.Services,
            connectionString,
            app.Logger,
            app.Lifetime.ApplicationStopping)
        : await DevelopmentAdminProvisioningCommand.ExecuteAsync(
            app.Services,
            app.Logger,
            app.Lifetime.ApplicationStopping);
    return;
}

app.Run();

public partial class Program { }
