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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

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
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
    };
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
builder.Services.AddScoped<ICompanyContext, WebCompanyContext>();
builder.Services.AddSingleton<EncryptionService>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireUserName("admin"));
});

Console.WriteLine("3. BUILDING THE APP");
var app = builder.Build();
Console.WriteLine("4. BUILDING FINISHED !");

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
    app.UseHttpsRedirection();

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

    using (var scope = app.Services.CreateScope())
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var adminUser = userManager.FindByNameAsync("admin").Result;

        if (adminUser == null)
        {
            var defaultAdmin = new IdentityUser { UserName = "admin" };
            userManager.CreateAsync(defaultAdmin, "Admin2026!").Wait();
        }
    }

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