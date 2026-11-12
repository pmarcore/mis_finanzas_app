using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finanzas.Tests;

/// <summary>
/// Detalle de los resúmenes de tarjeta por mes de vencimiento: una línea por cuota, vista unificada y filtro de fijos
/// (foto de octubre, base propia). Los casos usan vencimientos de 2027 para no mezclarse con los datos importados.
/// </summary>
public class ConsumosPorTarjetaTests : IClassFixture<PlanillaOctubreTests.Api>
{
    readonly PlanillaOctubreTests.Api api;
    public ConsumosPorTarjetaTests(PlanillaOctubreTests.Api api) => this.api = api;

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

    static async Task<List<int>> CardIds(HttpClient c) =>
        (await Json(await c.GetAsync("/payment-methods"))).EnumerateArray()
            .Where(m => m.GetProperty("isCard").GetBoolean()).Select(m => m.GetProperty("id").GetInt32()).ToList();

    static async Task<int> CategoryId(HttpClient c, string scope = "familia") =>
        (await Json(await c.GetAsync($"/categories?scope={scope}"))).EnumerateArray()
            .First(x => x.GetProperty("kind").GetString() == "gasto").GetProperty("id").GetInt32();

    static async Task<int> TypeId(HttpClient c, string kind, string effect = "resta") =>
        (await Json(await c.GetAsync("/operation-types"))).EnumerateArray()
            .First(t => t.GetProperty("kind").GetString() == kind && (kind == "pagoTarjeta" || t.GetProperty("effect").GetString() == effect))
            .GetProperty("id").GetInt32();

    static async Task<int> Compra(HttpClient c, int card, string description, decimal amount, string firstDue, int installments = 1, string scope = "familia")
    {
        var t = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope, personId = 1, operationTypeId = await TypeId(c, "movimiento"), date = "2026-11-01", description,
            categoryId = await CategoryId(c, scope), amount, paymentMethodId = card, installments, firstDueDate = firstDue,
        }));
        return t.GetProperty("id").GetInt32();
    }

    static Task<JsonElement> Detalle(HttpClient c, string month, string query = "") =>
        c.GetAsync($"/cards/statements/{month}?scope=familia{query}").ContinueWith(r => Json(r.Result)).Unwrap();

    /// <summary>Resumen de una tarjeta en el mes; una tarjeta puede tener dos vencimientos en el mismo mes, por eso el filtro opcional.</summary>
    static JsonElement CardOf(JsonElement detalle, int card, string? due = null) =>
        detalle.GetProperty("cards").EnumerateArray()
            .Single(x => x.GetProperty("cardId").GetInt32() == card && (due == null || x.GetProperty("dueDate").GetString() == due));

    static IEnumerable<JsonElement> Lines(JsonElement card) => card.GetProperty("lines").EnumerateArray();

    [Fact]
    public async Task A_purchase_in_six_installments_shows_one_line_per_statement_with_the_remainder_in_the_first()
    {
        var c = await Client();
        var card = (await CardIds(c)).First();
        var id = await Compra(c, card, "Heladera en cuotas", 60_000.05m, "2027-05-05", installments: 6);

        for (var i = 0; i < 6; i++)
        {
            var month = new DateOnly(2027, 5 + i, 1).ToString("yyyy-MM");
            var line = Lines(CardOf(await Detalle(c, month, $"&cardId={card}"), card, $"{month}-05"))
                .Single(l => l.GetProperty("transactionId").GetInt32() == id);
            Assert.Equal(i + 1, line.GetProperty("installment").GetInt32());
            Assert.Equal(6, line.GetProperty("installments").GetInt32());
            Assert.Equal(i == 0 ? -10_000.05m : -10_000m, Num(line, "amount"));
            Assert.Equal("2026-11-01", line.GetProperty("purchaseDate").GetString());
        }
        // Fuera de las seis cuotas no aparece.
        var after = await Detalle(c, "2027-11", $"&cardId={card}");
        Assert.DoesNotContain(after.GetProperty("cards").EnumerateArray().SelectMany(Lines), l => l.GetProperty("transactionId").GetInt32() == id);
    }

    [Fact]
    public async Task The_header_matches_the_dashboard_and_a_partial_payment_reduces_the_outstanding()
    {
        var c = await Client();

        // Con los datos importados: lo pendiente de cada resumen es lo que muestra Pagos de tarjeta del inicio.
        var dashboard = await Json(await c.GetAsync("/dashboard?scope=familia"));
        var byCard = dashboard.GetProperty("cardPayments").EnumerateArray().SelectMany(p => p.GetProperty("byCard").EnumerateArray()).ToList();
        foreach (var month in new[] { "2026-11", "2026-12" })
            foreach (var s in (await Detalle(c, month)).GetProperty("cards").EnumerateArray())
            {
                var expected = byCard.Where(b => b.GetProperty("cardId").GetInt32() == s.GetProperty("cardId").GetInt32()
                                                 && b.GetProperty("dueDate").GetString() == s.GetProperty("dueDate").GetString()).Sum(b => Num(b, "total"));
                Assert.Equal(expected, Num(s, "outstanding"));
            }

        // Un pago parcial ya realizado de un resumen futuro.
        var card = (await CardIds(c)).First();
        await Compra(c, card, "Notebook", 80_000, "2027-04-05");
        var before = CardOf(await Detalle(c, "2027-04", $"&cardId={card}"), card, "2027-04-05");
        Assert.Equal(-Lines(before).Sum(l => Num(l, "amount")), Num(before, "modeled"));
        Assert.Equal(0m, Num(before, "paid"));

        await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = await TypeId(c, "pagoTarjeta"), date = "2026-11-02", description = "Adelanto",
            amount = 30_000, paymentMethodId = 1, paidCardId = card, firstDueDate = "2027-04-05",
        }));
        var after = CardOf(await Detalle(c, "2027-04", $"&cardId={card}"), card, "2027-04-05");
        Assert.Equal(30_000m, Num(after, "paid"));
        Assert.Equal(Num(before, "expected"), Num(after, "expected"));
        Assert.Equal(Num(before, "outstanding") - 30_000, Num(after, "outstanding"));
    }

    [Fact]
    public async Task The_unified_view_groups_every_card_of_the_month_and_adds_their_totals()
    {
        var c = await Client();
        var cards = await CardIds(c);
        Assert.True(cards.Count >= 2, "La planilla tiene al menos dos tarjetas.");
        await Compra(c, cards[0], "Zapatillas", 50_000, "2027-06-10");
        await Compra(c, cards[1], "Campera", 70_000, "2027-06-15");

        var all = await Detalle(c, "2027-06");
        var list = all.GetProperty("cards").EnumerateArray().ToList();
        Assert.Contains(list, x => x.GetProperty("cardId").GetInt32() == cards[0]);
        Assert.Contains(list, x => x.GetProperty("cardId").GetInt32() == cards[1]);
        Assert.Equal(list.Sum(x => Num(x, "expected")), Num(all, "total"));
        Assert.Equal(list.Sum(x => Num(x, "filteredTotal")), Num(all, "filteredTotal"));

        // Con cardId queda solo esa tarjeta.
        var one = await Detalle(c, "2027-06", $"&cardId={cards[1]}");
        Assert.All(one.GetProperty("cards").EnumerateArray(), x => Assert.Equal(cards[1], x.GetProperty("cardId").GetInt32()));
    }

    [Fact]
    public async Task The_fixed_filter_changes_the_lines_but_not_the_header_and_future_fixed_land_in_their_statement()
    {
        var c = await Client();
        var card = (await CardIds(c)).Last();

        // Cierres de enero y febrero 2027, sin perder los cargados.
        var cycles = (await Json(await c.GetAsync($"/cards/{card}/cycles"))).EnumerateArray()
            .Select(x => new { closingDate = x.GetProperty("closingDate").GetString(), dueDate = x.GetProperty("dueDate").GetString() })
            .Where(x => string.CompareOrdinal(x.dueDate, "2027-02-01") < 0)
            .Concat([new { closingDate = (string?)"2027-01-20", dueDate = (string?)"2027-02-05" }, new { closingDate = (string?)"2027-02-20", dueDate = (string?)"2027-03-05" }])
            .ToList();
        await Json(await c.PutAsJsonAsync($"/cards/{card}/cycles", cycles));

        var rule = await Json(await c.PostAsJsonAsync("/recurring-rules", new
        {
            scope = "familia", personId = 1, operationTypeId = await TypeId(c, "movimiento"), description = "Streaming fijo",
            categoryId = await CategoryId(c), paymentMethodId = card, dayOfMonth = 10, startMonth = "2027-01", endMonth = "2027-02",
            amounts = new[] { new { fromMonth = "2027-01", amount = 9_000 } },
        }));
        Assert.Empty(rule.GetProperty("missingCycles").EnumerateArray());
        var ruleId = rule.GetProperty("rule").GetProperty("id").GetInt32();
        await Compra(c, card, "Compra suelta", 12_000, "2027-02-05");

        // El fijo del 10/01 cierra el 20/01: vence en febrero, como previsto.
        var todos = CardOf(await Detalle(c, "2027-02", $"&cardId={card}"), card, "2027-02-05");
        var fijo = Lines(todos).Single(l => l.GetProperty("recurringRuleId").ValueKind == JsonValueKind.Number && l.GetProperty("recurringRuleId").GetInt32() == ruleId);
        Assert.Equal("previsto", fijo.GetProperty("status").GetString());
        Assert.Equal(-9_000m, Num(fijo, "amount"));
        Assert.Contains(Lines(todos), l => l.GetProperty("description").GetString() == "Compra suelta");

        var soloFijos = CardOf(await Detalle(c, "2027-02", $"&cardId={card}&recurring=true"), card, "2027-02-05");
        Assert.All(Lines(soloFijos), l => Assert.Equal(JsonValueKind.Number, l.GetProperty("recurringRuleId").ValueKind));
        Assert.Contains(Lines(soloFijos), l => l.GetProperty("description").GetString() == "Streaming fijo");
        Assert.Equal(-Lines(soloFijos).Sum(l => Num(l, "amount")), Num(soloFijos, "filteredTotal"));

        var sinFijos = CardOf(await Detalle(c, "2027-02", $"&cardId={card}&recurring=false"), card, "2027-02-05");
        Assert.All(Lines(sinFijos), l => Assert.Equal(JsonValueKind.Null, l.GetProperty("recurringRuleId").ValueKind));
        Assert.Contains(Lines(sinFijos), l => l.GetProperty("description").GetString() == "Compra suelta");

        foreach (var prop in new[] { "modeled", "paid", "expected", "outstanding" })
        {
            Assert.Equal(Num(todos, prop), Num(soloFijos, prop));
            Assert.Equal(Num(todos, prop), Num(sinFijos, prop));
        }
        Assert.Equal(Num(todos, "filteredTotal"), Num(soloFijos, "filteredTotal") + Num(sinFijos, "filteredTotal"));

        // El de febrero cae en el resumen de marzo.
        Assert.Contains(Lines(CardOf(await Detalle(c, "2027-03", $"&cardId={card}&recurring=true"), card, "2027-03-05")),
            l => l.GetProperty("description").GetString() == "Streaming fijo");

        // Y el listado de movimientos también filtra por gasto fijo.
        var txFijos = await Json(await c.GetAsync("/transactions?scope=familia&from=2027-01-01&to=2027-02-28&recurring=true"));
        Assert.Contains(txFijos.EnumerateArray(), t => t.GetProperty("description").GetString() == "Streaming fijo");
        Assert.All(txFijos.EnumerateArray(), t => Assert.Equal(JsonValueKind.Number, t.GetProperty("recurringRuleId").ValueKind));
        var txSueltos = await Json(await c.GetAsync("/transactions?scope=familia&from=2027-01-01&to=2027-02-28&recurring=false"));
        Assert.DoesNotContain(txSueltos.EnumerateArray(), t => t.GetProperty("description").GetString() == "Streaming fijo");
    }

    [Fact]
    public async Task Each_entity_sees_only_its_own_lines_and_months()
    {
        var c = await Client();
        var card = (await CardIds(c)).First();
        await Compra(c, card, "Insumos Memey", 15_000, "2028-01-05", scope: "memey");

        var memey = await Json(await c.GetAsync("/cards/statements/2028-01?scope=memey"));
        Assert.Contains(memey.GetProperty("cards").EnumerateArray().SelectMany(Lines), l => l.GetProperty("description").GetString() == "Insumos Memey");
        var familia = await Detalle(c, "2028-01");
        Assert.DoesNotContain(familia.GetProperty("cards").EnumerateArray().SelectMany(Lines), l => l.GetProperty("description").GetString() == "Insumos Memey");

        var memeyMonths = (await Json(await c.GetAsync("/cards/statements/months?scope=memey"))).EnumerateArray().ToList();
        Assert.Contains(memeyMonths, m => m.GetProperty("dueMonth").GetString() == "2028-01" && Num(m, "total") == 15_000m);
        var familiaMonths = (await Json(await c.GetAsync("/cards/statements/months?scope=familia"))).EnumerateArray().Select(m => m.GetProperty("dueMonth").GetString()).ToList();
        Assert.DoesNotContain("2028-01", familiaMonths);
        Assert.Equal(familiaMonths.OrderBy(m => m, StringComparer.Ordinal), familiaMonths);
    }

    [Fact]
    public async Task Invalid_month_and_non_card_are_rejected()
    {
        var c = await Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/cards/statements/2027-13?scope=familia")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/cards/statements/2027-01?scope=familia&cardId=1")).StatusCode);
    }
}
