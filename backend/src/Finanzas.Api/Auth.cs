using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Finanzas.Domain;
using Finanzas.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Finanzas.Api;

/// <summary>
/// Login simple: un usuario por persona, definidos en la configuración (Auth:Users), y un token firmado que dura
/// Auth:TokenDays días. Con Auth:Required en false (tests viejos) la API queda abierta como antes.
/// </summary>
public class AuthOptions
{
    public bool Required { get; set; } = true;
    /// <summary>Clave para firmar los tokens (al menos 32 caracteres). En la nube va como secret, nunca en el repo.</summary>
    public string? SigningKey { get; set; }
    public int TokenDays { get; set; } = 90;
    public List<ConfiguredUser> Users { get; set; } = [];
}

public class ConfiguredUser
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? DisplayName { get; set; }
}

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username, string DisplayName, DateTime ExpiresAt);
public record MeResponse(string Username, string DisplayName);

public static class AuthSetup
{
    public const string LoginRateLimit = "login";
    const string Issuer = "finanzas";
    const string VersionClaim = "ver";

    public static AuthOptions AddFinanzasAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        if (!options.Required) return options;

        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Falta Auth:SigningKey (al menos 32 caracteres). En Fly: fly secrets set AUTH__SIGNINGKEY=... (ver docs/despliegue.md).");

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Issuer,
                IssuerSigningKey = Key(options),
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            // Un token de un usuario borrado o emitido antes del último cambio de contraseña ya no sirve.
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<FinanzasDbContext>();
                    var sub = ctx.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                    var ver = ctx.Principal?.FindFirstValue(VersionClaim);
                    var ok = int.TryParse(sub, out var id)
                        && await db.Users.AnyAsync(u => u.Id == id && u.TokenVersion.ToString() == ver);
                    if (!ok) ctx.Fail("Sesión vencida.");
                },
            };
        });
        builder.Services.AddAuthorization(o =>
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // Frena fuerza bruta: 5 intentos por minuto por IP (detrás del proxy de Fly la IP real viene en Fly-Client-IP).
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Request.Headers["Fly-Client-IP"].FirstOrDefault() ?? http.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return options;
    }

    public static void UseFinanzasAuth(this WebApplication app, AuthOptions options)
    {
        if (!options.Required) return;
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
    }

    /// <summary>Crea los usuarios configurados que no existen y actualiza nombre y contraseña de los que sí.</summary>
    public static void SyncUsers(FinanzasDbContext db, AuthOptions options, IPasswordHasher<AppUser> hasher, ILogger log)
    {
        foreach (var cu in options.Users)
        {
            if (string.IsNullOrWhiteSpace(cu.Username)) continue;
            if (string.IsNullOrEmpty(cu.Password))
            {
                log.LogWarning("El usuario {User} no tiene contraseña configurada: se ignora.", cu.Username);
                continue;
            }
            var username = Normalize(cu.Username);
            var display = string.IsNullOrWhiteSpace(cu.DisplayName) ? cu.Username.Trim() : cu.DisplayName.Trim();
            var user = db.Users.FirstOrDefault(u => u.Username == username);
            if (user is null)
            {
                user = new AppUser { Username = username, DisplayName = display, PasswordHash = "" };
                user.PasswordHash = hasher.HashPassword(user, cu.Password);
                db.Users.Add(user);
                log.LogInformation("Usuario {User} creado.", username);
                continue;
            }
            user.DisplayName = display;
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, cu.Password) == PasswordVerificationResult.Failed)
            {
                user.PasswordHash = hasher.HashPassword(user, cu.Password);
                user.TokenVersion++;
                log.LogInformation("Contraseña de {User} actualizada: sus sesiones abiertas se cierran.", username);
            }
        }
        db.SaveChanges();
    }

    public static void MapAuth(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<AuthOptions>();

        var login = app.MapPost("/auth/login", async (LoginRequest req, FinanzasDbContext db, IPasswordHasher<AppUser> hasher) =>
        {
            const string bad = "Usuario o contraseña incorrectos.";
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrEmpty(req.Password))
                return Results.Problem(bad, statusCode: 401);
            var username = Normalize(req.Username);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
                return Results.Problem(bad, statusCode: 401);
            var expires = DateTime.UtcNow.AddDays(options.TokenDays);
            return Results.Ok(new LoginResponse(CreateToken(options, user, expires), user.Username, user.DisplayName, expires));
        }).AllowAnonymous();
        if (options.Required) login.RequireRateLimiting(LoginRateLimit);

        // Sin login obligatorio (ambiente viejo) no hay usuario: la app lo toma como "sin sesión necesaria".
        app.MapGet("/auth/me", async (ClaimsPrincipal principal, FinanzasDbContext db) =>
        {
            if (!options.Required) return Results.Ok(new { required = false });
            var id = int.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
            var user = await db.Users.FirstAsync(u => u.Id == id);
            return Results.Ok(new MeResponse(user.Username, user.DisplayName));
        }).AllowAnonymous().AddEndpointFilter(async (ctx, next) =>
            options.Required && ctx.HttpContext.User.Identity?.IsAuthenticated != true ? Results.Unauthorized() : await next(ctx));
    }

    static string CreateToken(AuthOptions options, AppUser user, DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Username),
                new Claim(VersionClaim, user.TokenVersion.ToString()),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(Key(options), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    static SymmetricSecurityKey Key(AuthOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey ?? ""));

    static string Normalize(string username) => username.Trim().ToLowerInvariant();
}
