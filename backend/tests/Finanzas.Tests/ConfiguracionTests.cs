using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finanzas.Tests;

/// <summary>Tipos de operación, categorías, medios de pago y ahorro/inversión sobre la foto de octubre (base propia).</summary>
public class ConfiguracionTests : IClassFixture<PlanillaOctubreTests.Api>
{
    readonly PlanillaOctubreTests.Api api;
    public ConfiguracionTests(PlanillaOctubreTests.Api api) => this.api = api;

    async Task<HttpClient> Client()
    {
        await api.EnsureImportedAsync();
        return api.CreateClient();
    }

    static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    static decimal Num(JsonElement e, string prop) => e.GetProperty(prop).GetDecimal();

    [Fact]
    public async Task Operation_types_are_offered_per_scope()
    {
        var c = await Client();
        var memey = (await Json(await c.GetAsync("/operation-types?scope=memey"))).EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        var familia = (await Json(await c.GetAsync("/operation-types?scope=familia"))).EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("Retiro de Memey", memey);
        Assert.DoesNotContain("Aporte a Memey", memey);
        Assert.Contains("Aporte a Memey", familia);
        Assert.Contains("Inversión", familia);
    }

    [Fact]
    public async Task A_new_type_that_adds_to_cash_moves_the_balance_and_flipping_it_rebuilds_the_movements()
    {
        var c = await Client();
        var type = await Json(await c.PostAsJsonAsync("/operation-types", new
        {
            name = "Venta Memey", kind = "movimiento", effect = "suma", scopes = new[] { "memey" }, active = true,
        }));
        var id = type.GetProperty("id").GetInt32();
        var before = Num(await Json(await c.GetAsync("/dashboard?scope=memey&date=2026-10-08")), "cashNow");

        await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "memey", personId = 2, operationTypeId = id, date = "2026-10-05", description = "Venta", amount = 10_000, paymentMethodId = 3,
        }));
        Assert.Equal(before + 10_000, Num(await Json(await c.GetAsync("/dashboard?scope=memey&date=2026-10-08")), "cashNow"));

        await Json(await c.PutAsJsonAsync($"/operation-types/{id}", new
        {
            name = "Venta Memey", kind = "movimiento", effect = "resta", scopes = new[] { "memey" }, active = true,
        }));
        Assert.Equal(before - 10_000, Num(await Json(await c.GetAsync("/dashboard?scope=memey&date=2026-10-08")), "cashNow"));

        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/operation-types/{id}")).StatusCode);
    }

    [Fact]
    public async Task Savings_and_investments_are_shown_apart_from_cash()
    {
        var c = await Client();
        var cashBefore = Num(await Json(await c.GetAsync("/dashboard?scope=familia&date=2026-10-08")), "cashNow");
        await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 8, date = "2026-10-07", description = "FCI", amount = 200_000, paymentMethodId = 1,
        }));

        var s = await Json(await c.GetAsync("/savings?scope=familia&date=2026-10-08"));
        var inv = s.GetProperty("inversion");
        Assert.Equal(200_000m, Num(inv, "balance"));
        Assert.False(inv.GetProperty("providerConnected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, inv.GetProperty("marketValue").ValueKind);
        Assert.Equal(cashBefore - 200_000, Num(await Json(await c.GetAsync("/dashboard?scope=familia&date=2026-10-08")), "cashNow"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.PostAsync("/investments/sync?scope=familia", null)).StatusCode);

        var card = await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 8, date = "2026-10-07", description = "FCI", amount = 1, paymentMethodId = 4,
        });
        Assert.Equal(HttpStatusCode.BadRequest, card.StatusCode);
    }

    [Fact]
    public async Task Categories_and_payment_methods_can_be_added_and_used_ones_are_only_deactivated()
    {
        var c = await Client();
        var cat = await Json(await c.PostAsJsonAsync("/categories", new { name = "Mascotas", scope = "familia", kind = "gasto", budgetType = "variable", active = true }));
        var catId = cat.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/categories/{catId}")).StatusCode);

        var alimentos = (await Json(await c.GetAsync("/categories?scope=familia"))).EnumerateArray().First(x => x.GetProperty("name").GetString() == "Alimentos");
        var alimentosId = alimentos.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/categories/{alimentosId}")).StatusCode);
        var off = await Json(await c.PutAsJsonAsync($"/categories/{alimentosId}", new { name = "Alimentos", scope = "familia", kind = "gasto", budgetType = "variable", active = false }));
        Assert.False(off.GetProperty("active").GetBoolean());

        var amex = await Json(await c.PostAsJsonAsync("/payment-methods", new { name = "Amex", type = "tarjeta", cashBoxId = 1, isCard = true, active = true }));
        Assert.True(amex.GetProperty("isCard").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/payment-methods/{amex.GetProperty("id").GetInt32()}")).StatusCode);

        // Visa Santander tiene compras: no deja de ser tarjeta ni se borra.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/payment-methods/4", new { name = "Visa Santander", type = "debito", cashBoxId = 1, isCard = false, active = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync("/payment-methods/4")).StatusCode);
    }
}
