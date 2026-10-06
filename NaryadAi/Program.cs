using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using NaryadAi.Components;
using NaryadAi.Data;
using NaryadAi.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    var dataProtection = builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
    if (OperatingSystem.IsWindows() && builder.Configuration.GetValue<bool>("DataProtection:ProtectKeysWithDpapi"))
        dataProtection.ProtectKeysWithDpapi();
}
builder.Services.AddScoped<AiReviewService>();
builder.Services.AddScoped<AiAnalyticsService>();
builder.Services.AddScoped<AiChatbotService>();
builder.Services.AddScoped<WorkOrderService>();
builder.Services.AddScoped<WorkOrderPhotoService>();
builder.Services.AddScoped<EmployeeRatingService>();
builder.Services.AddScoped<AppLanguageService>();
builder.Services.AddScoped<ApiAccessTokenService>();
builder.Services.AddSingleton<WorkOrderChangeNotifier>();
builder.Services.AddHostedService<WorkOrderDeadlineMonitor>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Transient);

var app = builder.Build();

var isDesignTime = app.Environment.IsEnvironment("DesignTime");
if (!isDesignTime)
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        DataSeeder.Initialize(db);
        await DemoDataSeeder.InitializeAsync(db);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<WorkOrdersHub>("/hubs/work-orders");

if (!isDesignTime)
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<NaryadAi.Data.AppDbContext>();
            NaryadAi.Data.DataSeeder.Initialize(context);
            if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:EnableDemoData"))
                await NaryadAi.Data.DemoDataSeeder.InitializeAsync(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"БД: {ex.Message}");
        }
    }
}

app.Run();

