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
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<FinanzasDbContext>().Database.Migrate();

app.UseCors();
app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapDashboard();
app.MapTransactions();
app.MapCards();
app.MapRecurringRules();
app.MapBudgets();
app.MapSettings();
app.MapImport();

app.Run();

public partial class Program;
