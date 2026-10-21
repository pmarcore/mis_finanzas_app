using Finanzas.Api;
using Finanzas.Domain;
using Finanzas.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FinanzasDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Finanzas") ?? "Data Source=finanzas.db"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
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
    scope.ServiceProvider.GetRequiredService<FinanzasDbContext>().Database.Migrate();

app.UseCors();
app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
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

app.Run();

public partial class Program;
