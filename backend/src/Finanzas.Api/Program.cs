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
// Invertir Online: reemplazar por la implementación real de IInvestmentProvider cuando se conecte.
builder.Services.AddSingleton<IInvestmentProvider, SinProveedorDeInversiones>();
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
app.MapImport();

app.Run();

public partial class Program;
