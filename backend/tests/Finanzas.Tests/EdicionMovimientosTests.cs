using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finanzas.Tests;

/// <summary>Editar y borrar movimientos recalcula caja, resúmenes de tarjeta y presupuesto vs. real (foto de octubre, base propia).</summary>
public class EdicionMovimientosTests : IClassFixture<PlanillaOctubreTests.Api>
{
    const string Corte = "2026-10-08";
    readonly PlanillaOctubreTests.Api api;
    public EdicionMovimientosTests(PlanillaOctubreTests.Api api) => this.api = api;

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

    static async Task<decimal> CashNow(HttpClient c, string scope = "familia") =>
        Num(await Json(await c.GetAsync($"/dashboard?scope={scope}&date={Corte}")), "cashNow");

    /// <summary>Lo que falta pagar del resumen de noviembre de la Visa Santander (familia).</summary>
    static async Task<decimal> VisaNoviembre(HttpClient c) =>
        (await Json(await c.GetAsync($"/dashboard?scope=familia&date={Corte}"))).GetProperty("cardPayments").EnumerateArray()
            .SelectMany(p => p.GetProperty("byCard").EnumerateArray())
            .Where(b => b.GetProperty("cardId").GetInt32() == 4 && b.GetProperty("dueDate").GetString() == "2026-11-11")
            .Sum(b => Num(b, "total"));

    static async Task<decimal> Real(HttpClient c, string category)
    {
        var r = await Json(await c.GetAsync($"/reports/budget-vs-actual?month=2026-10&cutoff={Corte}&scope=familia"));
        return r.GetProperty("rows").EnumerateArray().Where(x => x.GetProperty("category").GetString() == category).Sum(x => Num(x, "actual"));
    }

    static async Task<int> CategoryId(HttpClient c, string name) =>
        (await Json(await c.GetAsync("/categories?scope=familia"))).EnumerateArray()
            .First(x => x.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();

    [Fact]
    public async Task Editing_and_deleting_an_expense_updates_cash_and_budget_actual()
    {
        var c = await Client();
        var alimentos = await CategoryId(c, "Alimentos");
        var cash = await CashNow(c);
        var real = await Real(c, "Alimentos");

        var gasto = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "Súper", categoryId = alimentos,
            amount = 10_000, paymentMethodId = 1,
        }));
        var id = gasto.GetProperty("id").GetInt32();

        var editado = await Json(await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "Súper grande", categoryId = alimentos,
            amount = 25_000, paymentMethodId = 1,
        }));
        Assert.Equal("Súper grande", editado.GetProperty("description").GetString());
        Assert.Equal(25_000m, Num(await Json(await c.GetAsync($"/transactions/{id}")), "amount"));
        Assert.Equal(cash - 25_000, await CashNow(c));
        Assert.Equal(real + 25_000, await Real(c, "Alimentos"));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/transactions/{id}")).StatusCode);
        Assert.Equal(cash, await CashNow(c));
        Assert.Equal(real, await Real(c, "Alimentos"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/transactions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/transactions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Editing_an_income_amount_and_date_moves_the_cash()
    {
        var c = await Client();
        var sueldo = await CategoryId(c, "Sueldo");
        var cash = await CashNow(c);

        var ingreso = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 1, date = "2026-10-03", description = "Extra", categoryId = sueldo,
            amount = 40_000, paymentMethodId = 1,
        }));
        var id = ingreso.GetProperty("id").GetInt32();
        Assert.Equal(cash + 40_000, await CashNow(c));

        // Pasa a después del corte: deja de estar en la caja actual.
        await Json(await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = 1, date = "2026-10-20", description = "Extra", categoryId = sueldo,
            amount = 60_000, paymentMethodId = 1, status = "previsto",
        }));
        Assert.Equal(cash, await CashNow(c));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/transactions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Moving_a_purchase_to_a_card_takes_it_out_of_cash_and_into_the_statement()
    {
        var c = await Client();
        var cash = await CashNow(c);
        var visa = await VisaNoviembre(c);

        var gasto = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "Zapatillas", amount = 30_000, paymentMethodId = 1,
        }));
        var id = gasto.GetProperty("id").GetInt32();
        Assert.Equal(cash - 30_000, await CashNow(c));

        // A Visa en 3 cuotas, sin vencimiento: se propone el del cierre del 29/10 (vence 11/11).
        var conTarjeta = await Json(await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "Zapatillas", amount = 30_000,
            paymentMethodId = 4, installments = 3,
        }));
        Assert.Equal("2026-11-11", conTarjeta.GetProperty("firstDueDate").GetString());
        Assert.Equal(cash, await CashNow(c));
        Assert.Equal(visa + 10_000, await VisaNoviembre(c));

        // De vuelta a débito: sin cuotas ni vencimiento, sale de la caja otra vez.
        var debito = await Json(await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "Zapatillas", amount = 30_000,
            paymentMethodId = 1, installments = 3, firstDueDate = "2026-11-11",
        }));
        Assert.Equal(1, debito.GetProperty("installments").GetInt32());
        Assert.Equal(JsonValueKind.Null, debito.GetProperty("firstDueDate").ValueKind);
        Assert.Equal(visa, await VisaNoviembre(c));
        Assert.Equal(cash - 30_000, await CashNow(c));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/transactions/{id}")).StatusCode);
        Assert.Equal(cash, await CashNow(c));
    }

    [Fact]
    public async Task Editing_or_deleting_a_card_payment_changes_what_is_left_to_pay()
    {
        var c = await Client();
        var cash = await CashNow(c);
        var visa = await VisaNoviembre(c);

        var pago = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 3, date = "2026-10-07", description = "Adelanto Visa", amount = 100_000,
            paymentMethodId = 1, paidCardId = 4, firstDueDate = "2026-11-11",
        }));
        var id = pago.GetProperty("id").GetInt32();
        Assert.Equal(visa - 100_000, await VisaNoviembre(c));

        await Json(await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = 3, date = "2026-10-07", description = "Adelanto Visa", amount = 50_000,
            paymentMethodId = 1, paidCardId = 4, firstDueDate = "2026-11-11",
        }));
        Assert.Equal(visa - 50_000, await VisaNoviembre(c));
        Assert.Equal(cash - 50_000, await CashNow(c));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/transactions/{id}")).StatusCode);
        Assert.Equal(visa, await VisaNoviembre(c));
        Assert.Equal(cash, await CashNow(c));
    }

    [Fact]
    public async Task Invalid_edits_are_rejected_and_leave_the_movement_as_it_was()
    {
        var c = await Client();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/transactions/999999", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "x", amount = 1, paymentMethodId = 1,
        })).StatusCode);

        var cash = await CashNow(c);
        var gasto = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = 2, date = "2026-10-06", description = "Kiosco", amount = 5_000, paymentMethodId = 1,
        }));
        var id = gasto.GetProperty("id").GetInt32();

        // Pago de tarjeta sin tarjeta ni vencimiento.
        var malo = await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = 3, date = "2026-10-06", description = "Kiosco", amount = 5_000, paymentMethodId = 1,
        });
        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        Assert.Equal(cash - 5_000, await CashNow(c));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/transactions/{id}")).StatusCode);
    }
}
