using MachineStatusUpdate.Models;
using MachineStatusUpdate.Services;
using Microsoft.EntityFrameworkCore;
using MachineStatusUpdate.Hubs;

var builder = WebApplication.CreateBuilder(args);

// builder.WebHost.ConfigureKestrel(options =>
// {
//     options.ListenAnyIP(8109);
// });

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();

// ── Session (role-based auth) ──
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout        = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly    = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite    = SameSiteMode.Lax;
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// app.UsePathBase("/downtime");

// // Force PathBase cho tất cả request - giúp tag helper generate link đúng
// app.Use((context, next) =>
// {
//     context.Request.PathBase = "/downtime";
//     return next();
// });

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseSession();       // ← Phải đặt TRƯỚC UseRouting

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<DowntimeHub>("/downtimeHub");

app.Run();
