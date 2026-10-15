using Microsoft.AspNetCore.Identity;
using System.Globalization;
using NEXORA.Application.Catalog;
using NEXORA.Application.Email;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure;
using NEXORA.Infrastructure.Identity;
using NEXORA.Infrastructure.Persistence;
using NEXORA.Web.Email;
using NEXORA.Web.Images;
using NEXORA.Web.Identity;

var commandArguments = ProvisioningCommandArguments.Parse(args);
var builder = WebApplication.CreateBuilder(commandArguments.HostArguments);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required. Configure it through appsettings.Development.json or environment variables.");
}

builder.Services.AddNexoraInfrastructure(connectionString);
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
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/accessdenied";
    options.Cookie.Name = "NEXORA.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});

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

// Role initialization requires a database with migrations already applied. This does not apply migrations.
await using (var scope = app.Services.CreateAsyncScope())
{
    var roleSeeder = scope.ServiceProvider.GetRequiredService<RoleSeeder>();
    await roleSeeder.EnsureRolesAsync();
}

if (app.Environment.IsDevelopment() && !commandArguments.IsRequested)
{
    await using var scope = app.Services.CreateAsyncScope();
    var demoSeeder = scope.ServiceProvider.GetRequiredService<ICatalogDemoSeeder>();
    await demoSeeder.SeedAsync(app.Lifetime.ApplicationStopping);
}

if (commandArguments.IsRequested)
{
    if (!app.Environment.IsDevelopment())
    {
        app.Logger.LogCritical("Admin provisioning is available only in the Development environment.");
        Environment.ExitCode = 1;
        return;
    }

    Environment.ExitCode = await DevelopmentAdminProvisioningCommand.ExecuteAsync(
        app.Services,
        app.Logger,
        app.Lifetime.ApplicationStopping);
    return;
}

app.Run();

public partial class Program { }
