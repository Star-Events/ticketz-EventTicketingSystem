using EventTicketingSystem.Data;
using EventTicketingSystem.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;
using EventTicketingSystem.Security;


QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<DbHelper>();

builder.Services.AddSingleton<EventTicketingSystem.Services.ImageService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddSingleton<EventReadService>();


builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DbHelper>();
    using var conn = db.GetConnection();
    conn.Open();

    using var check = new NpgsqlCommand("SELECT 1 FROM users WHERE role='Admin' LIMIT 1;", conn);
    var exists = check.ExecuteScalar() != null;

    if (!exists)
    {
        var (hash, salt) = PasswordHasher.HashPassword("Admin@123");
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO users (user_id, full_name, email, password_hash, password_salt, role)
            VALUES (gen_random_uuid(), 'System Admin', 'admin@system.local', @hash, @salt, 'Admin');", conn);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("salt", salt);
        cmd.ExecuteNonQuery();
        Console.WriteLine("✅ Seeded default admin: admin@system.local / Admin@123");
    }
}


app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
