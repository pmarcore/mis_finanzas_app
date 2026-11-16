using System.Net.Http.Json;
using System.Text.Json;
using Finanzas.Domain;

namespace Finanzas.Tests;

/// <summary>
/// Las cuotas se atan al resumen (tarjeta + mes), no a una fecha: el resumen de una compra sale de su fecha de compra y los
/// cierres, y la fecha de cada cuota es el vencimiento cargado para su mes (Spec/cuotas-por-resumen.md).
/// </summary>
public class CuotasPorResumenTests : IClassFixture<PlanillaOctubreTests.Api>
{
    readonly PlanillaOctubreTests.Api api;
    public CuotasPorResumenTests(PlanillaOctubreTests.Api api) => this.api = api;

    // ---------- Dominio ----------

    static CardCycle Cycle(string closing, string due) => new() { PaymentMethodId = 9, ClosingDate = DateOnly.Parse(closing), DueDate = DateOnly.Parse(due) };

    static readonly List<CardCycle> Cycles =
    [
        Cycle("2026-11-26", "2026-12-04"),
        Cycle("2026-12-29", "2027-01-11"),
        Cycle("2027-01-28", "2027-02-09"),
    ];

    static (Transaction Tx, PaymentMethod Card) Purchase(int installments, DateOnly? firstMonth = null, DateOnly? firstDue = null)
    {
        var card = new PaymentMethod { Id = 9, Name = "Visa", Type = PaymentMethodType.Tarjeta, Cycles = Cycles };
        var tx = new Transaction
        {
            Scope = Scope.Familia, Description = "Compra", Date = new(2026, 11, 20), Amount = 90_000_00, Installments = installments,
            PaymentMethodId = 9, FirstStatementMonth = firstMonth, FirstDueDate = firstDue,
            OperationType = new OperationType { Name = "Gasto", Kind = OperationKind.Movimiento, Effect = CashEffect.Resta, ForFamilia = true },
        };
        return (tx, card);
    }

    [Fact]
    public void Each_installment_lands_on_the_due_date_loaded_for_its_month()
    {
        var (tx, card) = Purchase(3, firstMonth: new(2026, 12, 1));
        var impacts = CashImpactBuilder.Build(tx, card, _ => 1);

        Assert.Equal([new DateOnly(2026, 12, 4), new(2027, 1, 11), new(2027, 2, 9)], impacts.Select(i => i.ImpactDate));
        Assert.Equal([new DateOnly(2026, 12, 1), new(2027, 1, 1), new(2027, 2, 1)], impacts.Select(i => i.StatementMonth!.Value));
        Assert.Equal(new DateOnly(2026, 12, 4), tx.FirstDueDate);
    }

    [Fact]
    public void A_month_without_cycle_uses_the_day_of_the_last_loaded_due_date()
    {
        var (tx, card) = Purchase(5, firstMonth: new(2026, 12, 1));
        var impacts = CashImpactBuilder.Build(tx, card, _ => 1);

        // Marzo y abril no tienen cierre: usan el día 9, el del último vencimiento cargado (febrero).
        Assert.Equal(new DateOnly(2027, 3, 9), impacts[3].ImpactDate);
        Assert.Equal(new DateOnly(2027, 4, 9), impacts[4].ImpactDate);
    }

    [Fact]
    public void An_old_client_that_sends_only_the_due_date_gets_the_statement_of_that_month()
    {
        // Vencimiento viejo (11/12) de un mes que ahora vence el 04/12: pertenece al resumen de diciembre y se corrige la fecha.
        var (tx, card) = Purchase(2, firstDue: new(2026, 12, 11));
        var impacts = CashImpactBuilder.Build(tx, card, _ => 1);

        Assert.Equal(new DateOnly(2026, 12, 1), tx.FirstStatementMonth);
        Assert.Equal([new DateOnly(2026, 12, 4), new(2027, 1, 11)], impacts.Select(i => i.ImpactDate));
    }

    [Fact]
    public void The_statement_is_proposed_from_the_purchase_date_and_the_closing_dates()
    {
        Assert.Equal(new DateOnly(2027, 1, 1), CardCalendar.ProposeStatementMonth(Cycles, new(2026, 12, 29)));
        Assert.Equal(new DateOnly(2027, 2, 1), CardCalendar.ProposeStatementMonth(Cycles, new(2026, 12, 30)));
        Assert.Null(CardCalendar.ProposeStatementMonth(Cycles, new(2027, 2, 1)));
    }

    [Fact]
    public void Lines_of_the_same_card_and_month_are_one_statement_even_with_different_dates()
    {
        var gasto = new OperationType { Name = "Gasto", Kind = OperationKind.Movimiento, Effect = CashEffect.Resta, ForFamilia = true };
        Transaction Line(int id, string date, long amount) => new()
        {
            Id = id, Scope = Scope.Familia, Description = $"L{id}", Date = new(2026, 10, 1), Amount = amount, PaymentMethodId = 9,
            OperationType = gasto, Status = TransactionStatus.Realizado,
            Impacts = [new CashImpact { CashBoxId = 1, CardId = 9, ImpactDate = DateOnly.Parse(date), Amount = -amount }],
        };
        var box = new CashBox { Id = 1, Scope = Scope.Familia, Name = "Caja", OpeningDate = new(2026, 10, 1) };
        var statements = new List<Statement> { new() { PaymentMethodId = 9, DueDate = new(2026, 12, 11), ConfirmedAmount = 500_00 } };
        var calc = new CashFlowCalculator([box], [Line(1, "2026-12-04", 100_00), Line(2, "2026-12-11", 200_00)], statements);

        var s = Assert.Single(calc.CardStatementDetails(Scope.Familia, new(2026, 11, 16), new(2026, 12, 31)));
        Assert.Equal(300_00, s.Modeled);
        Assert.Equal(new DateOnly(2026, 12, 4), s.DueDate);
        Assert.Equal(500_00, s.Confirmed); // el resumen real se encuentra por mes aunque la fecha difiera
    }

    // ---------- API ----------

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

    static async Task<int> CardId(HttpClient c, string name) =>
        (await Json(await c.GetAsync("/payment-methods"))).EnumerateArray()
            .First(m => m.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();

    static async Task<int> GastoTypeId(HttpClient c) =>
        (await Json(await c.GetAsync("/operation-types"))).EnumerateArray()
            .First(t => t.GetProperty("kind").GetString() == "movimiento" && t.GetProperty("effect").GetString() == "resta").GetProperty("id").GetInt32();

    static async Task<int> CategoryId(HttpClient c) =>
        (await Json(await c.GetAsync("/categories?scope=familia"))).EnumerateArray()
            .First(x => x.GetProperty("kind").GetString() == "gasto").GetProperty("id").GetInt32();

    /// <summary>Tarjeta nueva, con sus propios cierres, para no tocar las de la planilla.</summary>
    static async Task<int> NewCard(HttpClient c, string name, params (string Closing, string Due)[] cycles)
    {
        var card = await Json(await c.PostAsJsonAsync("/payment-methods", new { id = 0, name, type = "tarjeta", cashBoxId = 1, isCard = true }));
        var id = card.GetProperty("id").GetInt32();
        await SaveCycles(c, id, cycles);
        return id;
    }

    static async Task<JsonElement> SaveCycles(HttpClient c, int card, params (string Closing, string Due)[] cycles) =>
        await Json(await c.PutAsJsonAsync($"/cards/{card}/cycles",
            cycles.Select(x => new { cardId = card, month = x.Closing[..7], closingDate = x.Closing, dueDate = x.Due })));

    static async Task<JsonElement> Buy(HttpClient c, int card, string date, string description, decimal amount, int installments = 1,
        string? statementMonth = null, bool pinned = false) =>
        await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = await GastoTypeId(c), date, description, categoryId = await CategoryId(c),
            amount, paymentMethodId = card, installments, firstStatementMonth = statementMonth, statementPinned = pinned,
        }));

    static async Task<List<JsonElement>> Statements(HttpClient c, string month, int card) =>
        (await Json(await c.GetAsync($"/cards/statements/{month}?scope=familia&cardId={card}"))).GetProperty("cards").EnumerateArray().ToList();

    static bool Has(JsonElement statement, string description) =>
        statement.GetProperty("lines").EnumerateArray().Any(l => l.GetProperty("description").GetString() == description);

    [Fact]
    public async Task Correcting_the_december_due_date_merges_the_imported_installments_into_one_statement()
    {
        // El caso real: la planilla trae diciembre al 11/12 y el banco confirma 04/12.
        var c = await Client();
        var card = await CardId(c, "Visa Santander");
        var before = await Statements(c, "2026-12", card);
        var totalBefore = before.Sum(s => Num(s, "modeled"));

        var cycles = (await Json(await c.GetAsync($"/cards/{card}/cycles"))).EnumerateArray()
            .Select(x => (Closing: x.GetProperty("closingDate").GetString()!, Due: x.GetProperty("dueDate").GetString()!))
            .Select(x => x.Due == "2026-12-11" ? ("2026-11-26", "2026-12-04") : x).ToArray();
        await SaveCycles(c, card, cycles);

        var after = Assert.Single(await Statements(c, "2026-12", card));
        Assert.Equal("2026-12-04", after.GetProperty("dueDate").GetString());
        Assert.Equal(totalBefore, Num(after, "modeled"));
        // El inicio también muestra un solo vencimiento de esa tarjeta en diciembre.
        var dashboard = await Json(await c.GetAsync("/dashboard?scope=familia"));
        var december = dashboard.GetProperty("cardPayments").EnumerateArray().SelectMany(p => p.GetProperty("byCard").EnumerateArray())
            .Where(b => b.GetProperty("cardId").GetInt32() == card && b.GetProperty("dueDate").GetString()!.StartsWith("2026-12")).ToList();
        Assert.True(december.Count <= 1);
        Assert.All(december, b => Assert.Equal("2026-12-04", b.GetProperty("dueDate").GetString()));
    }

    [Fact]
    public async Task Changing_a_closing_date_moves_the_purchase_to_the_next_statement_with_all_its_installments()
    {
        var c = await Client();
        var card = await NewCard(c, "Visa prueba cierre",
            ("2027-11-26", "2027-12-04"), ("2027-12-29", "2028-01-11"), ("2028-01-28", "2028-02-09"), ("2028-02-26", "2028-03-06"));
        var tx = await Buy(c, card, "2027-12-29", "Regalo de fin de año", 30_000, installments: 2);
        Assert.Equal("2028-01", tx.GetProperty("firstStatementMonth").GetString());
        Assert.Equal("2028-01-11", tx.GetProperty("firstDueDate").GetString());
        Assert.Contains(await Statements(c, "2028-01", card), s => Has(s, "Regalo de fin de año"));

        // Diciembre cierra el 28/12: la compra del 29/12 pasa a febrero y su cuota 2 a marzo.
        var saved = await SaveCycles(c, card,
            ("2027-11-26", "2027-12-04"), ("2027-12-28", "2028-01-11"), ("2028-01-28", "2028-02-09"), ("2028-02-26", "2028-03-06"));
        var moved = Assert.Single(saved.GetProperty("moved").EnumerateArray());
        Assert.Equal("2028-01", moved.GetProperty("fromMonth").GetString());
        Assert.Equal("2028-02", moved.GetProperty("toMonth").GetString());

        Assert.DoesNotContain(await Statements(c, "2028-01", card), s => Has(s, "Regalo de fin de año"));
        var feb = Assert.Single(await Statements(c, "2028-02", card));
        Assert.True(Has(feb, "Regalo de fin de año"));
        Assert.Equal("2028-02-09", feb.GetProperty("dueDate").GetString());
        Assert.Contains(await Statements(c, "2028-03", card), s => Has(s, "Regalo de fin de año"));

        // Volver al cierre del 29/12 la devuelve a enero.
        await SaveCycles(c, card,
            ("2027-11-26", "2027-12-04"), ("2027-12-29", "2028-01-11"), ("2028-01-28", "2028-02-09"), ("2028-02-26", "2028-03-06"));
        Assert.Contains(await Statements(c, "2028-01", card), s => Has(s, "Regalo de fin de año"));
    }

    [Fact]
    public async Task A_pinned_purchase_keeps_its_statement_when_closing_dates_change()
    {
        var c = await Client();
        var card = await NewCard(c, "Visa prueba fijada", ("2027-11-26", "2027-12-04"), ("2027-12-29", "2028-01-11"), ("2028-01-28", "2028-02-09"));
        // El comercio la presentó tarde: el banco la puso en febrero aunque por fecha iba a enero.
        var tx = await Buy(c, card, "2027-12-20", "Presentada tarde", 10_000, statementMonth: "2028-02", pinned: true);
        Assert.True(tx.GetProperty("statementPinned").GetBoolean());
        Assert.Equal("2028-02", tx.GetProperty("firstStatementMonth").GetString());

        var saved = await SaveCycles(c, card, ("2027-11-26", "2027-12-04"), ("2027-12-15", "2028-01-11"), ("2028-01-28", "2028-02-08"));
        Assert.Empty(saved.GetProperty("moved").EnumerateArray());
        var feb = Assert.Single(await Statements(c, "2028-02", card));
        Assert.True(Has(feb, "Presentada tarde"));
        Assert.Equal("2028-02-08", feb.GetProperty("dueDate").GetString()); // la fecha sí sigue al cierre
    }

    [Fact]
    public async Task Without_pin_the_statement_comes_from_the_purchase_date_even_if_the_client_sends_another()
    {
        var c = await Client();
        var card = await NewCard(c, "Visa prueba sin fijar", ("2027-11-26", "2027-12-04"), ("2027-12-29", "2028-01-11"));
        var tx = await Buy(c, card, "2027-12-01", "Sin fijar", 5_000, statementMonth: "2027-12");
        Assert.Equal("2028-01", tx.GetProperty("firstStatementMonth").GetString());
        Assert.False(tx.GetProperty("statementPinned").GetBoolean());

        // Editar la fecha de compra la cambia de resumen.
        var id = tx.GetProperty("id").GetInt32();
        var edited = await Json(await c.PutAsJsonAsync($"/transactions/{id}", new
        {
            scope = "familia", personId = 1, operationTypeId = await GastoTypeId(c), date = "2027-11-20", description = "Sin fijar",
            categoryId = await CategoryId(c), amount = 5_000, paymentMethodId = card, installments = 1,
        }));
        Assert.Equal("2027-12", edited.GetProperty("firstStatementMonth").GetString());
        Assert.Equal("2027-12-04", edited.GetProperty("firstDueDate").GetString());
    }

    [Fact]
    public async Task A_card_payment_is_matched_by_month_and_follows_the_new_due_date()
    {
        var c = await Client();
        var card = await NewCard(c, "Visa prueba pago", ("2027-11-26", "2027-12-11"));
        await Buy(c, card, "2027-11-10", "Compra a pagar", 40_000);
        var pagoType = (await Json(await c.GetAsync("/operation-types"))).EnumerateArray()
            .First(t => t.GetProperty("kind").GetString() == "pagoTarjeta").GetProperty("id").GetInt32();
        var pago = await Json(await c.PostAsJsonAsync("/transactions", new
        {
            scope = "familia", personId = 1, operationTypeId = pagoType, date = "2026-11-10", description = "Pago adelantado",
            amount = 15_000, paymentMethodId = 1, paidCardId = card, firstStatementMonth = "2027-12",
        }));
        Assert.Equal("2027-12-11", pago.GetProperty("firstDueDate").GetString());

        await SaveCycles(c, card, ("2027-11-26", "2027-12-04"));
        var s = Assert.Single(await Statements(c, "2027-12", card));
        Assert.Equal("2027-12-04", s.GetProperty("dueDate").GetString());
        Assert.Equal(15_000m, Num(s, "paid"));
        Assert.Equal(25_000m, Num(s, "outstanding"));
        var updated = await Json(await c.GetAsync($"/transactions/{pago.GetProperty("id").GetInt32()}"));
        Assert.Equal("2027-12-04", updated.GetProperty("firstDueDate").GetString());
    }
}
