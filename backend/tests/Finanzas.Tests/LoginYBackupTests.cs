using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Finanzas.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Finanzas.Tests;

/// <summary>Login con un usuario por persona, rutas protegidas y copias de la base (spec despliegue-celulares).</summary>
public class LoginYBackupTests
{
    public class Api(string pabloPassword = "clave-pablo", int tokenDays = 90, string? dbPath = null) : WebApplicationFactory<Program>
    {
        public readonly string DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"finanzas-auth-{Guid.NewGuid():N}.db");
        public readonly string BackupDir = Path.Combine(Path.GetTempPath(), $"finanzas-bk-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Finanzas", $"Data Source={DbPath}");
            builder.UseSetting("Auth:Required", "true");
            builder.UseSetting("Auth:SigningKey", "clave-de-firma-solo-para-tests-0123456789abcdef");
            builder.UseSetting("Auth:TokenDays", tokenDays.ToString());
            builder.UseSetting("Auth:Users:0:Username", "Pablo");
            builder.UseSetting("Auth:Users:0:Password", pabloPassword);
            builder.UseSetting("Auth:Users:0:DisplayName", "Pablo");
            builder.UseSetting("Auth:Users:1:Username", "esposa");
            builder.UseSetting("Auth:Users:1:Password", "clave-esposa");
            builder.UseSetting("Backup:Enabled", "false");
            builder.UseSetting("Backup:Directory", BackupDir);
            builder.UseSetting("Backup:Keep", "3");
        }

        public void DeleteFiles()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(DbPath);
            if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true);
        }
    }

    static async Task<HttpResponseMessage> Login(HttpClient c, string user, string password) =>
        await c.PostAsJsonAsync("/auth/login", new { username = user, password });

    static async Task<string> Token(HttpClient c, string user = "pablo", string password = "clave-pablo")
    {
        var res = await Login(c, user, password);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    static HttpRequestMessage Get(string path, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Routes_need_a_token_except_health_and_login()
    {
        using var api = new Api();
        try
        {
            var c = api.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/dashboard?scope=familia")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/transactions")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/backup/download")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Get("/transactions", "no-es-un-token"))).StatusCode);

            var token = await Token(c);
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/transactions", token))).StatusCode);
            var me = await (await c.SendAsync(Get("/auth/me", token))).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("pablo", me.GetProperty("username").GetString());
        }
        finally { api.DeleteFiles(); }
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_user_and_rejects_wrong_passwords()
    {
        using var api = new Api();
        try
        {
            var c = api.CreateClient();
            var ok = await (await Login(c, " PABLO ", "clave-pablo")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Pablo", ok.GetProperty("displayName").GetString());
            Assert.True(ok.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow.AddDays(89));

            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "pablo", "otra")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "nadie", "clave-pablo")).StatusCode);
            // El segundo usuario, sin nombre para mostrar, usa el de usuario.
            var esposa = await (await Login(c, "esposa", "clave-esposa")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("esposa", esposa.GetProperty("displayName").GetString());
        }
        finally { api.DeleteFiles(); }
    }

    [Fact]
    public async Task Login_is_rate_limited_after_five_attempts_per_minute()
    {
        using var api = new Api();
        try
        {
            var c = api.CreateClient();
            for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "pablo", "mal")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(c, "pablo", "clave-pablo")).StatusCode);
        }
        finally { api.DeleteFiles(); }
    }

    [Fact]
    public async Task Expired_tokens_are_rejected()
    {
        using var api = new Api(tokenDays: -1);
        try
        {
            var c = api.CreateClient();
            var token = await Token(c);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Get("/transactions", token))).StatusCode);
        }
        finally { api.DeleteFiles(); }
    }

    [Fact]
    public async Task Changing_a_password_closes_only_that_users_sessions()
    {
        var db = Path.Combine(Path.GetTempPath(), $"finanzas-auth-{Guid.NewGuid():N}.db");
        string pabloToken, esposaToken;
        using (var first = new Api(dbPath: db))
        {
            var c = first.CreateClient();
            pabloToken = await Token(c);
            esposaToken = await Token(c, "esposa", "clave-esposa");
        }

        // Se cambia el secret de Pablo y la API arranca de nuevo sobre la misma base.
        using var second = new Api(pabloPassword: "clave-nueva", dbPath: db);
        try
        {
            var c = second.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Get("/transactions", pabloToken))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/transactions", esposaToken))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "pablo", "clave-pablo")).StatusCode);
            await Token(c, "pablo", "clave-nueva");
        }
        finally { second.DeleteFiles(); }
    }

    [Fact]
    public async Task Daily_backup_is_made_once_per_day_and_keeps_the_last_ones()
    {
        using var api = new Api();
        try
        {
            var c = api.CreateClient();
            await Token(c); // arranca la API y crea la base
            Directory.CreateDirectory(api.BackupDir);
            foreach (var d in new[] { "2026-01-01", "2026-01-02", "2026-01-03", "2026-01-04" })
                await File.WriteAllTextAsync(Path.Combine(api.BackupDir, $"finanzas-{d}.db"), "vieja");

            var backups = api.Services.GetRequiredService<BackupService>();
            var path = await backups.RunDailyAsync();
            var written = File.GetLastWriteTimeUtc(path);
            Assert.True(new FileInfo(path).Length > 0);
            Assert.Equal(await backups.RunDailyAsync(), path);
            Assert.Equal(written, File.GetLastWriteTimeUtc(path)); // la segunda vez no la rehace

            var left = Directory.GetFiles(api.BackupDir).Select(f => Path.GetFileName(f)).Order().ToArray();
            Assert.Equal(3, left.Length); // Keep = 3: la de hoy y las dos más nuevas
            Assert.Equal(["finanzas-2026-01-03.db", "finanzas-2026-01-04.db", Path.GetFileName(path)], left);
        }
        finally { api.DeleteFiles(); }
    }

    [Fact]
    public async Task Backup_download_returns_a_sqlite_file()
    {
        using var api = new Api();
        try
        {
            var c = api.CreateClient();
            var res = await c.SendAsync(Get("/backup/download", await Token(c)));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.StartsWith("finanzas-", res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            var bytes = await res.Content.ReadAsByteArrayAsync();
            Assert.Equal("SQLite format 3", System.Text.Encoding.ASCII.GetString(bytes, 0, 15));
        }
        finally { api.DeleteFiles(); }
    }
}
