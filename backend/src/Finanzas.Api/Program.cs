using Finanzas.Api;
using Finanzas.Domain;
using Finanzas.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FinanzasDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Finanzas") ?? "Data Source=finanzas.db"));
// CORS abierto solo para desarrollo (Expo web en :8081 llama a la API en :5080). En la nube la app web se sirve
// desde la misma API, así que Fly pone Cors:AllowAny=false y no se habilita.
var corsAllowAny = builder.Configuration.GetValue("Cors:AllowAny", true);
if (corsAllowAny)
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
var auth = builder.AddFinanzasAuth();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BackupService>();
if (builder.Configuration.GetValue("Backup:Enabled", true))
    builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupService>());
builder.Services.AddOpenApi();
// Invertir Online (solo lectura). Las credenciales llegan por variables de entorno Iol__Username e Iol__Password, que
// docker compose toma del .env de la raíz (nunca en el repo). Sin ellas la app funciona igual, sin datos de IOL.
var iolUser = builder.Configuration["Iol:Username"];
var iolPassword = builder.Configuration["Iol:Password"];
if (!string.IsNullOrWhiteSpace(iolUser) && !string.IsNullOrWhiteSpace(iolPassword))
    builder.Services.AddSingleton<IInvestmentProvider>(_ => new IolInvestmentProvider(
        new HttpClient { BaseAddress = new Uri(builder.Configuration["Iol:BaseUrl"] ?? "https://api.invertironline.com"), Timeout = TimeSpan.FromSeconds(20) },
        new IolCredentials(iolUser, iolPassword)));
else
    builder.Services.AddSingleton<IInvestmentProvider, SinProveedorDeInversiones>();
// Ripio no tiene API para cuentas personales: las tenencias se cargan a mano y los precios salen de una API pública sin clave.
builder.Services.AddSingleton<ICryptoPrices>(_ => new BinanceCryptoPrices(
    new HttpClient { BaseAddress = new Uri(builder.Configuration["Crypto:BaseUrl"] ?? "https://api.binance.com"), Timeout = TimeSpan.FromSeconds(15) }));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<InvestmentService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinanzasDbContext>();
    db.Database.Migrate();
    AuthSetup.SyncUsers(db, auth, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<AppUser>>(),
        app.Logger);
}

// La app web (PWA) exportada con Expo vive en wwwroot: se sirve sin login, porque la pantalla de login es parte de ella.
// index.html y sw.js sin caché (así cada deploy llega enseguida); los bundles de _expo llevan hash y se cachean.
var webFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
        ctx.Context.Request.Path.StartsWithSegments("/_expo") ? "public, max-age=31536000, immutable" : "no-cache",
};
app.UseDefaultFiles();
app.UseStaticFiles(webFiles);
if (corsAllowAny) app.UseCors();
app.UseFinanzasAuth(auth);
app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuth();
app.MapBackup();
app.MapDashboard();
app.MapCashFlow();
app.MapTransactions();
app.MapCards();
app.MapRecurringRules();
app.MapBudgets();
app.MapSettings();
app.MapOperationTypes();
app.MapCategories();
app.MapPaymentMethods();
app.MapSavings();
app.MapInvestments();
app.MapImport();
// Cualquier otra ruta que no sea de la API abre la app web (si está incluida en la imagen).
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html", webFiles).AllowAnonymous();

app.Run();

public partial class Program;
