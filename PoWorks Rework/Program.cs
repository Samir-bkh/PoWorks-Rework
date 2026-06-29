using PoWorks_Rework.Services;
using System;
using System.Security.Authentication;

// 1er Radar : Tout début du programme
Console.WriteLine("🚀 1. DÉMARRAGE DU PROGRAMME...");

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine("⏳ 2. AJOUT DES SERVICES...");

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

// Register HttpClient and PCVueWebService
builder.Services.AddSingleton<PCVueWebService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<PCVueWebService>>();
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        CheckCertificateRevocationList = false,
        SslProtocols = SslProtocols.None
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
Console.WriteLine("⏳ 3. CONSTRUCTION DE L'APPLICATION...");
var app = builder.Build();
Console.WriteLine("✅ 4. CONSTRUCTION TERMINÉE !");

Console.WriteLine("✅ 4. CONSTRUCTION TERMINÉE !");

try
{
    Console.WriteLine("⏳ 4b. Configuration du pipeline HTTP...");

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    Console.WriteLine("⏳ 4c. UseHttpsRedirection...");
    app.UseHttpsRedirection();

    Console.WriteLine("⏳ 4d. UseStaticFiles...");
    app.UseStaticFiles();

    Console.WriteLine("⏳ 4e. UseRouting...");
    app.UseRouting();

    Console.WriteLine("⏳ 4f. UseAuthorization...");
    app.UseAuthorization();

    Console.WriteLine("⏳ 4g. MapControllerRoutes...");
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

    Console.WriteLine("🏁 5. PRÊT À LANCER LE SITE WEB — http://localhost:5101");
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ ERREUR : {ex.GetType().Name}");
    Console.WriteLine($"   Message : {ex.Message}");
    Console.WriteLine($"   Cause   : {ex.InnerException?.Message}");
    Console.WriteLine("\nAppuie sur une touche...");
    Console.ReadKey();
}