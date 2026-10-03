using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using NaryadAi.Components;
using NaryadAi.Data;
using NaryadAi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiReviewService>();
builder.Services.AddScoped<WorkOrderService>();
builder.Services.AddScoped<WorkOrderPhotoService>();
builder.Services.AddScoped<EmployeeRatingService>();
builder.Services.AddHostedService<WorkOrderDeadlineMonitor>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
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

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<NaryadAi.Data.AppDbContext>(); 
        NaryadAi.Data.DataSeeder.Initialize(context);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка при инициализации БД: {ex.Message}");
    }
}

app.Run();
