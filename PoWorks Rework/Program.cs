using PoWorks_Rework.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<PoWorks_Rework.Repositories.MeterRepository>();

// Register the DatabaseService as a singleton
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<SqlServerService>();
builder.Services.AddScoped<VarexpParserService>();

builder.Services.AddScoped<VariableBrowseParsingService>();

// Register HttpClient and PCVueWebService
builder.Services.AddHttpClient<PCVueWebService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();

    // BYPASS SSL CERTIFICATE VALIDATION (DEVELOPMENT ONLY!)
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

    return handler;
});

// NEW: Register TrendsService (depends on PCVueWebService)
builder.Services.AddScoped<TrendsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();