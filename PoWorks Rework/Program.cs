using PoWorks_Rework.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<PoWorks_Rework.Repositories.MeterRepository>();

// Register the DatabaseService as a singleton
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<SqlServerService>();

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

// Add specific routes for HDS Import
app.MapControllerRoute(
    name: "hdsImport",
    pattern: "HdsImport/{action=GetTables}/{id?}");

app.Run();