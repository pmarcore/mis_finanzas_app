using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finanzas.Tests;

/// <summary>
/// Presupuesto separado por ámbito (Familia y Memey no se mezclan), detalle de movimientos de una fila del
/// presupuesto vs. real y filtros del listado de movimientos (foto de octubre, base propia).
/// </summary>
public class PresupuestoPorEntidadTests : IClassFixture<PlanillaOctubreTests.Api>
{
    readonly PlanillaOctubreTests.Api api;
    public PresupuestoPorEntidadTests(PlanillaOctubreTests.Api api) => this.api = api;

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

    static async Task<int> CategoryId(HttpClient c, string scope, string name) =>
        (await Json(await c.GetAsync($"/categories?scope={scope}"))).EnumerateArray()
            .First(x => x.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();

    static async Task<decimal> TotalBudget(HttpClient c, string scope, string month, string cutoff) =>
        Num(await Json(await c.GetAsync($"/reports/budget-vs-actual?month={month}&cutoff={cutoff}&scope={scope}")), "totalBudget");

    [Fact]
    public async Task Memey_budget_is_saved_and_reported_apart_from_familia()
    {
        var c = await Client();
        var insumos = await CategoryId(c, "memey", "Insumos");

        var memey = await Json(await c.PutAsJsonAsync("/budgets/2026-10?scope=memey", new[] { new { month = "2026-10", categoryId = insumos, amount = 50_000 } }));
        Assert.Single(memey.EnumerateArray());

        // Guardar el de Memey no borra ni suma al de Familia.
        var familia = await Json(await c.GetAsync("/budgets/2026-10?scope=familia"));
        Assert.Equal(21, familia.GetArrayLength());
        Assert.Equal(6_680_000m, familia.EnumerateArray().Sum(b => Num(b, "amount")));
        Assert.Equal(6_680_000m, await TotalBudget(c, "familia", "2026-10", "2026-10-09"));
        Assert.Equal(50_000m, await TotalBudget(c, "memey", "2026-10", "2026-10-09"));

        // Y guardar el de Familia no borra el de Memey.
        await Json(await c.PutAsJsonAsync("/budgets/2026-10?scope=familia", familia));
        Assert.Single((await Json(await c.GetAsync("/budgets/2026-10?scope=memey"))).EnumerateArray());
    }

    [Fact]
    public async Task A_category_of_the_other_scope_is_rejected()
    {
        var c = await Client();
        var alimentos = await CategoryId(c, "familia", "Alimentos");
        var res = await c.PutAsJsonAsync("/budgets/2026-10?scope=memey", new[] { new { month = "2026-10", categoryId = alimentos, amount = 1_000 } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Loading_a_memey_month_does_not_hide_the_familia_budget_carried_forward()
    {
        var c = await Client();
        var publicidad = await CategoryId(c, "memey", "Publicidad");
        await Json(await c.PutAsJsonAsync("/budgets/2026-12?scope=memey", new[] { new { month = "2026-12", categoryId = publicidad, amount = 30_000 } }));

        // Familia no cargó diciembre: sigue valiendo el último mes que cargó (octubre).
        Assert.Equal(6_680_000m, await TotalBudget(c, "familia", "2026-12", "2026-12-01"));
        Assert.Equal(30_000m, await TotalBudget(c, "memey", "2026-12", "2026-12-01"));
    }

    [Fact]
    public async Task Category_detail_lists_the_movements_that_add_up_to_the_actual()
    {
        var c = await Client();
        var alimentos = await CategoryId(c, "familia", "Alimentos");
        var report = await Json(await c.GetAsync("/reports/budget-vs-actual?month=2026-10&cutoff=2026-10-09&scope=familia"));
        var actual = Num(report.GetProperty("rows").EnumerateArray().First(r => r.GetProperty("categoryId").GetInt32() == alimentos), "actual");

        var list = await Json(await c.GetAsync($"/reports/budget-vs-actual/{alimentos}/transactions?month=2026-10&cutoff=2026-10-09"));
        Assert.Equal(14, list.GetArrayLength());
        Assert.All(list.EnumerateArray(), t => Assert.Equal(alimentos, t.GetProperty("categoryId").GetInt32()));
        Assert.Equal(actual, list.EnumerateArray().Sum(t => Num(t, "amount")));

        // Con corte antes de fin de mes solo entra lo realizado hasta el corte.
        var alCorte = await Json(await c.GetAsync($"/reports/budget-vs-actual/{alimentos}/transactions?month=2026-10&cutoff=2026-10-05"));
        Assert.All(alCorte.EnumerateArray(), t => Assert.True(string.CompareOrdinal(t.GetProperty("date").GetString(), "2026-10-05") <= 0));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/reports/budget-vs-actual/9999/transactions?month=2026-10")).StatusCode);
    }

    [Fact]
    public async Task Transactions_can_be_filtered_by_category_and_payment_method()
    {
        var c = await Client();
        var alimentos = await CategoryId(c, "familia", "Alimentos");
        const string mes = "scope=familia&from=2026-10-01&to=2026-10-31";

        var porCategoria = await Json(await c.GetAsync($"/transactions?{mes}&categoryId={alimentos}"));
        Assert.True(porCategoria.GetArrayLength() > 0);
        Assert.All(porCategoria.EnumerateArray(), t => Assert.Equal(alimentos, t.GetProperty("categoryId").GetInt32()));

        var todos = await Json(await c.GetAsync($"/transactions?{mes}"));
        var medio = todos.EnumerateArray().First().GetProperty("paymentMethodId").GetInt32();
        var porMedio = await Json(await c.GetAsync($"/transactions?{mes}&paymentMethodId={medio}"));
        Assert.All(porMedio.EnumerateArray(), t => Assert.Equal(medio, t.GetProperty("paymentMethodId").GetInt32()));
        Assert.Equal(todos.EnumerateArray().Count(t => t.GetProperty("paymentMethodId").GetInt32() == medio), porMedio.GetArrayLength());

        var ambos = await Json(await c.GetAsync($"/transactions?{mes}&categoryId={alimentos}&paymentMethodId={medio}"));
        Assert.All(ambos.EnumerateArray(), t =>
        {
            Assert.Equal(alimentos, t.GetProperty("categoryId").GetInt32());
            Assert.Equal(medio, t.GetProperty("paymentMethodId").GetInt32());
        });
    }
}
