using PoWorks_Rework.Services;
using System;
using System.Security.Authentication;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using PoWorks_Rework.Data;

Console.WriteLine("1. PROGRAM START");

var builder = WebApplication.CreateBuilder(args);

var tempEncryptionService = new PoWorks_Rework.Services.EncryptionService(builder.Configuration);

var host = builder.Configuration["DatabaseSettings:Host"];
var port = builder.Configuration["DatabaseSettings:Port"];
var database = builder.Configuration["DatabaseSettings:Database"];
var username = builder.Configuration["DatabaseSettings:Username"];
var encryptedPassword = builder.Configuration["DatabaseSettings:Password"];

var plainPassword = tempEncryptionService.Decrypt(encryptedPassword);

var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={plainPassword};Command Timeout=120;";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

QuestPDF.Settings.License = LicenseType.Community;

Console.WriteLine("2. ADDING SERVICES");
builder.Services.AddControllersWithViews();
builder.Services.AddControllers();
builder.Services.AddScoped<PoWorks_Rework.Repositories.MeterRepository>();
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<SqlServerService>();
builder.Services.AddScoped<DashboardDataService>();
builder.Services.AddScoped<VarexpParserService>();
builder.Services.AddScoped<VariableBrowseParsingService>();

builder.Services.AddScoped<BillingService>();
builder.Services.AddSingleton<PCVueWebService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<PCVueWebService>>();
    var handler = new HttpClientHandler();
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
    var httpClient = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromMinutes(2)
    };
    return new PCVueWebService(httpClient, logger);
});
builder.Services.AddScoped<TrendsService>();
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
System.Net.ServicePointManager.SecurityProtocol =
    System.Net.SecurityProtocolType.Tls |
    System.Net.SecurityProtocolType.Tls11 |
    System.Net.SecurityProtocolType.Tls12 |
    System.Net.SecurityProtocolType.Tls13;

builder.Services.AddHostedService<AutoImportWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SetupCheckService>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser()); 
});

builder.Services.AddScoped<CredentialMigrationService>();

Console.WriteLine("3. BUILDING THE APP");

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();

    var adminUser = userManager.FindByNameAsync("Admin").GetAwaiter().GetResult();
    if (adminUser == null)
    {
        var defaultAdmin = new Microsoft.AspNetCore.Identity.IdentityUser
        {
            UserName = "Admin",
            NormalizedUserName = "ADMIN",
            Email = "admin@poworks.local",
            NormalizedEmail = "ADMIN@POWORKS.LOCAL",
            EmailConfirmed = true
        };

       
        var createResult = userManager.CreateAsync(defaultAdmin, "Admin2026!").GetAwaiter().GetResult();

        if (createResult.Succeeded)
        {
            userManager.AddClaimAsync(defaultAdmin, new System.Security.Claims.Claim("CompanyId", "1")).GetAwaiter().GetResult();
            userManager.AddClaimAsync(defaultAdmin, new System.Security.Claims.Claim("CompanyName", "Default Company")).GetAwaiter().GetResult();
        }
    }
}

Console.WriteLine("4. BUILDING FINISHED !");

var mainDbService = app.Services.GetRequiredService<DatabaseService>();
var mainEncService = app.Services.GetRequiredService<EncryptionService>();

mainDbService.Initialize(new PoWorks_Rework.Models.DatabaseSettings
{
    Host = app.Configuration["DatabaseSettings:Host"] ?? "localhost",
    Port = app.Configuration["DatabaseSettings:Port"] ?? "5433",
    Database = app.Configuration["DatabaseSettings:Database"] ?? "",
    Username = app.Configuration["DatabaseSettings:Username"] ?? "postgres",
    Password = mainEncService.Decrypt(app.Configuration["DatabaseSettings:Password"] ?? ""),
    SSLMode = app.Configuration["DatabaseSettings:SSLMode"] ?? "Prefer"
});

using (var scope = app.Services.CreateScope())
{
    var migrationService = scope.ServiceProvider.GetRequiredService<CredentialMigrationService>();
    await migrationService.MigrateAllCredentialsAsync();
}

try
{
    Console.WriteLine("4b. HTTP pipeline setup");

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    Console.WriteLine("4c. UseHttpsRedirection");
    //app.UseHttpsRedirection();

    Console.WriteLine("4d. UseStaticFiles");
    app.UseStaticFiles();

    Console.WriteLine("4e. UseRouting");
    app.UseRouting();

    Console.WriteLine("4f. UseAuthentication & UseAuthorization");
    app.UseAuthentication();
    app.UseAuthorization();

    Console.WriteLine("4g. MapControllerRoutes");
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    app.MapControllerRoute(
        name: "importControllers",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
    app.MapControllerRoute(
        name: "varexpImport",
        pattern: "VarexpImport/{action}/{id?}",
        defaults: new { controller = "VarexpImport" });
    app.MapControllerRoute(
        name: "webServicesImport",
        pattern: "WebServicesImport/{action}/{id?}",
        defaults: new { controller = "WebServicesImport" });

    Console.WriteLine("5. READY TO START THE WEB SITE — http://localhost:5101");

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"\nERROR : {ex.GetType().Name}");
    Console.WriteLine($"   Message : {ex.Message}");
    Console.WriteLine($"   Cause   : {ex.InnerException?.Message}");
    Console.WriteLine("\n Press a key ");
    Console.ReadKey();
}