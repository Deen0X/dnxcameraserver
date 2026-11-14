using System.Net;
using System.Threading.RateLimiting;
using DNXCameraServer.Data;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Audit;
using DNXCameraServer.Web.Auth;
using DNXCameraServer.Web.Cameras;
using DNXCameraServer.Web.Configuration;
using DNXCameraServer.Web.Devices;
using DNXCameraServer.Web.Go2Rtc;
using DNXCameraServer.Web.Rtsp;
using DNXCameraServer.Web.Security;
using DNXCameraServer.Web.Setup;
using DNXCameraServer.Web.Startup;
using DNXCameraServer.Web.Streaming;
using DNXCameraServer.Web.AppSystem;
using DNXCameraServer.Web.Users;
using DNXCameraServer.Web.Workers;
using DNXCameraServer.Web.Xiaomi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var options = DnxOptions.FromEnvironment();
options.EnsureDirectories();
options.Validate();

// Modo de mantenimiento: `--reset-admin-password` borra los administradores y sale.
if (args.Contains("--reset-admin-password") ||
    Environment.GetEnvironmentVariable("RESET_ADMIN_PASSWORD") is "1" or "true")
{
    using var resetLoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
    var resetLogger = resetLoggerFactory.CreateLogger("Reset");
    return await DbInitializer.ResetAdminsAsync(options.DataDir, resetLogger);
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(options);
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<CameraDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(options.DataDir, "camera.db")}"));

// Claves de protección de datos persistentes: las cookies sobreviven al reinicio.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.DataDir, "dpkeys")))
    .SetApplicationName("DNXCameraServer");

builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<AdminSetupService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CameraAccess>();
builder.Services.AddScoped<StreamManager>();
builder.Services.AddSingleton<CapabilityService>();
builder.Services.AddSingleton<XiaomiLoginGate>();
builder.Services.AddHttpClient<Go2RtcClient>();
builder.Services.AddSingleton<Go2RtcSupervisor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Go2RtcSupervisor>());
builder.Services.AddHostedService<RtspGatewayService>();
builder.Services.AddHostedService<StatusSyncWorker>();

// Límite de intentos de login por IP (F1-04).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "dnx_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("Admin", p => p.RequireRole(Roles.Admin)));

if (options.TrustedProxies.Count > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var proxy in options.TrustedProxies)
            if (IPAddress.TryParse(proxy, out var ip))
                o.KnownProxies.Add(ip);
    });
}

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

var app = builder.Build();

// Errores no controlados: se registran y se devuelve JSON (con detalle si se pide).
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
    loggerFactory.CreateLogger("Errors").LogError(
        feature?.Error, "Error no controlado en {Path}", context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";

    var showDetail =
        string.Equals(Environment.GetEnvironmentVariable("DNX_DEBUG_ERRORS"), "true",
            StringComparison.OrdinalIgnoreCase);

    await context.Response.WriteAsJsonAsync(new
    {
        error = "internal_error",
        detail = showDetail ? feature?.Error.Message : null,
        path = context.Request.Path.Value
    });
}));

app.UseForwardedHeaders();

// Cabeceras de seguridad (LAN, sin proxy obligatorio).
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(self)";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; " +
        "connect-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'";
    await next();
});

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

// Inicialización: migraciones, admin inicial y reconciliación de streams.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CameraDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    await DbInitializer.InitializeAsync(db, logger);

    try
    {
        var streams = scope.ServiceProvider.GetRequiredService<StreamManager>();
        await streams.ReconcileAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "No se pudo reconciliar streams al arrancar (go2rtc puede no estar listo).");
    }
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
// El SSO va después del middleware de autenticación para no re-firmar la cookie
// en cada petición cuando ya hay sesión.
app.UseMiddleware<SsoMiddleware>();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSetupEndpoints();
app.MapXiaomiEndpoints();
app.MapCameraEndpoints();
app.MapStreamingEndpoints();
app.MapUserEndpoints();
app.MapDeviceTokenEndpoints();
app.MapSystemEndpoints();

app.MapMethods("/api/{**rest}",
    ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    () => Results.NotFound(new { error = "not_found" }));

app.MapFallbackToFile("index.html");

app.Run();

return 0;

public partial class Program;
