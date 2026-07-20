using PoWorks_Rework.Services;
using System;
using System.Security.Authentication;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;

// Start of program
Console.WriteLine("1. PROGRAM START");

var builder = WebApplication.CreateBuilder(args);

// --- CONFIGURATION QUESTPDF ---
// Déclaration de la licence communautaire (Gratuite pour les PME/Indépendants)
QuestPDF.Settings.License = LicenseType.Community;

Console.WriteLine("2. ADDING SERVICES");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddControllers();
builder.Services.AddScoped<PoWorks_Rework.Repositories.MeterRepository>();

// Register the DatabaseService as a singleton
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<SqlServerService>();

// PHASE 1: Register the new DashboardDataService
builder.Services.AddScoped<DashboardDataService>();

// Existing services
builder.Services.AddScoped<VarexpParserService>();
builder.Services.AddScoped<VariableBrowseParsingService>();

builder.Services.AddScoped<BillingService>();

// Dans Program.cs, remplace ton bloc de registre PCVueWebService par ceci :
builder.Services.AddSingleton<PCVueWebService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<PCVueWebService>>();
    var handler = new HttpClientHandler
    {
        // Autorise tout, ne vérifie rien (parfait pour le test)
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
    };
    var httpClient = new HttpClient(handler)
    {
        Timeout = TimeSpan.FromMinutes(2)
    };
    return new PCVueWebService(httpClient, logger);
});

// Register TrendsService (depends on PCVueWebService)
builder.Services.AddScoped<TrendsService>();
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
System.Net.ServicePointManager.SecurityProtocol =
    System.Net.SecurityProtocolType.Tls |
    System.Net.SecurityProtocolType.Tls11 |
    System.Net.SecurityProtocolType.Tls12 |
    System.Net.SecurityProtocolType.Tls13;

builder.Services.AddHostedService<AutoImportWorker>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8); // Durée de la session : 8 heures
    });

// Permet de lire les requêtes HTTP (et donc les cookies) n'importe où dans le code
builder.Services.AddHttpContextAccessor();

// Déclare notre cerveau Multi-Clients
builder.Services.AddScoped<ICompanyContext, WebCompanyContext>();

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

   
    app.UseAuthorization();

    Console.WriteLine("4f. UseAuthorization");
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