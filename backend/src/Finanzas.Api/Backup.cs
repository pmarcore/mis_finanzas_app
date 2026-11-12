using Finanzas.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Finanzas.Api;

/// <summary>
/// Copias de la base SQLite: una por día en Backup:Directory (por defecto la carpeta "backups" al lado de la base),
/// se guardan las últimas Backup:Keep (30), y una descarga a pedido desde Configuración.
/// Se usa VACUUM INTO, que copia la base entera de forma consistente aunque la API la esté usando.
/// </summary>
public class BackupService(IServiceScopeFactory scopes, IConfiguration config, ILogger<BackupService> log, TimeProvider clock) : BackgroundService
{
    public const string Prefix = "finanzas-";

    public string Directory => config["Backup:Directory"] is { Length: > 0 } dir ? dir : DefaultDirectory();
    int Keep => int.TryParse(config["Backup:Keep"], out var k) && k > 0 ? k : 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Revisa cada hora; si la máquina se apaga de noche (Fly la apaga cuando nadie la usa) la copia sale al prenderse.
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDailyAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "No se pudo hacer la copia diaria de la base."); }
            try { await Task.Delay(TimeSpan.FromHours(1), clock, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Hace la copia del día si todavía no existe y borra las que sobran. Devuelve la ruta de la copia de hoy.</summary>
    public async Task<string> RunDailyAsync(CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var path = Path.Combine(Directory, $"{Prefix}{today:yyyy-MM-dd}.db");
        if (!File.Exists(path))
        {
            await VacuumIntoAsync(path, ct);
            log.LogInformation("Copia diaria de la base: {Path}", path);
        }
        foreach (var old in System.IO.Directory.GetFiles(Directory, $"{Prefix}*.db").OrderDescending().Skip(Keep))
            File.Delete(old);
        return path;
    }

    /// <summary>Copia consistente de la base al archivo indicado (no debe existir).</summary>
    public async Task VacuumIntoAsync(string path, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanzasDbContext>();
        await using var conn = new SqliteConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path";
        cmd.Parameters.AddWithValue("$path", path);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    string DefaultDirectory()
    {
        var source = new SqliteConnectionStringBuilder(config.GetConnectionString("Finanzas") ?? "Data Source=finanzas.db").DataSource;
        var folder = Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".";
        return Path.Combine(folder, "backups");
    }
}

public static class BackupEndpoints
{
    public static void MapBackup(this WebApplication app)
    {
        // Baja una copia de la base al momento (pide sesión como todo lo demás).
        app.MapGet("/backup/download", async (BackupService backups, TimeProvider clock, HttpContext http) =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"finanzas-descarga-{Guid.NewGuid():N}.db");
            await backups.VacuumIntoAsync(tmp, http.RequestAborted);
            var bytes = await File.ReadAllBytesAsync(tmp, http.RequestAborted);
            File.Delete(tmp);
            var name = $"{BackupService.Prefix}{clock.GetLocalNow():yyyy-MM-dd-HHmm}.db";
            return Results.File(bytes, "application/vnd.sqlite3", name);
        });
    }
}
