using MachineStatusUpdate.Models;
using MachineStatusUpdate.Services;
using Microsoft.AspNetCore.HttpOverrides;
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

// ── Trust nginx reverse proxy ──────────────────────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    // Trust tất cả proxy nội bộ (nginx cùng máy)
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ── QUAN TRỌNG: Thứ tự middleware không được đảo ──────────────────────

// 1) Đọc X-Forwarded-For / X-Forwarded-Proto từ nginx TRƯỚC TIÊN
app.UseForwardedHeaders();

// 2) Set PathBase để tag helpers sinh URL đúng với /downtime prefix
app.UsePathBase("/downtime");

// 3) Middleware phụ đảm bảo PathBase luôn được set (giữ nguyên như cũ)
app.Use((context, next) =>
{
    context.Request.PathBase = "/downtime";
    return next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseSession();       // ← Phải đặt TRƯỚC UseRouting

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<DowntimeHub>("/downtimeHub");

app.Run();
