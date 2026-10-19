using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finanzas.Domain;

namespace Finanzas.Tests;

/// <summary>
/// Presupuestar meses futuros: borrador desde el presupuesto de un mes o el real de un mes cerrado, con ajuste %, redondeo
/// a $1.000 y gastos fijos; guardado de varios meses juntos y aviso de herencia.
/// </summary>
public class PresupuestoMesesFuturosTests : IClassFixture<PlanillaOctubreTests.Api>
{
    readonly PlanillaOctubreTests.Api api;
    public PresupuestoMesesFuturosTests(PlanillaOctubreTests.Api api) => this.api = api;

    static readonly OperationType Gasto = new() { Id = 2, Name = "Gasto", Kind = OperationKind.Movimiento, Effect = CashEffect.Resta, ForFamilia = true };
    static readonly Category Alquiler = new() { Id = 1, Name = "Alquiler", Scope = Scope.Familia, Kind = CategoryKind.Gasto, BudgetType = BudgetType.Fijo };
    static readonly Category Super = new() { Id = 2, Name = "Supermercado", Scope = Scope.Familia, Kind = CategoryKind.Gasto, BudgetType = BudgetType.Variable };
    static readonly Category Servicios = new() { Id = 3, Name = "Servicios", Scope = Scope.Familia, Kind = CategoryKind.Gasto, BudgetType = BudgetType.Variable };
    static readonly Category Insumos = new() { Id = 4, Name = "Insumos", Scope = Scope.Memey, Kind = CategoryKind.Gasto, BudgetType = BudgetType.Variable };
    static readonly Category Viejo = new() { Id = 5, Name = "Viejo", Scope = Scope.Familia, Kind = CategoryKind.Gasto, BudgetType = BudgetType.Variable, Active = false };
    static readonly Category[] Cats = [Alquiler, Super, Servicios, Insumos, Viejo];

    static readonly DateOnly Oct = new(2026, 10, 1), Nov = new(2026, 11, 1), Dic = new(2026, 12, 1), Ene = new(2027, 1, 1);

    static Budget B(DateOnly month, Category c, long pesos) => new() { Month = month, CategoryId = c.Id, Amount = pesos * 100 };

    static Transaction T(DateOnly date, Category c, long pesos, TransactionStatus status = TransactionStatus.Realizado) => new()
    {
        Scope = c.Scope, OperationType = Gasto, Date = date, Description = "x", CategoryId = c.Id, Amount = pesos * 100, Status = status,
    };

    static RecurringRule Rule(Category c, DateOnly start, DateOnly? end, params RecurringRuleAmount[] amounts) => new()
    {
        Scope = c.Scope, OperationType = Gasto, Description = "Fijo", CategoryId = c.Id, StartMonth = start, EndMonth = end,
        DayOfMonth = 10, Amounts = [.. amounts],
    };

    static Dictionary<string, long> Pesos(ProposalMonth m) => m.Rows.ToDictionary(r => r.Category.Name, r => r.Proposed / 100);

    [Fact]
    public void Budget_source_applies_the_increase_once_and_rounds_to_a_thousand()
    {
        Budget[] budgets = [B(Oct, Alquiler, 500_000), B(Oct, Super, 333_333), B(Oct, Insumos, 80_000)];
        var months = Budgeting.Propose(Cats, budgets, [], [], Scope.Familia, BudgetSource.Presupuesto, Oct, Nov, Dic, useFixed: false, increasePct: 5);

        Assert.Equal([Nov, Dic], months.Select(m => m.Month));
        // 333.333 × 1,05 = 349.999,65 → 350.000. No se acumula: diciembre igual que noviembre. Memey no entra.
        Assert.All(months, m => Assert.Equal(new Dictionary<string, long> { ["Alquiler"] = 525_000, ["Supermercado"] = 350_000 }, Pesos(m)));
        Assert.All(months, m => Assert.All(m.Rows, r => Assert.Equal(ProposalOrigin.Base, r.Origin)));
        Assert.All(months, m => Assert.False(m.HasOwnBudget));
    }

    [Fact]
    public void Budget_source_uses_the_inherited_budget_when_the_month_has_none()
    {
        var months = Budgeting.Propose(Cats, [B(Oct, Super, 100_000)], [], [], Scope.Familia, BudgetSource.Presupuesto, Nov, Dic, Dic, false, null);
        Assert.Equal(100_000, Pesos(months.Single())["Supermercado"]);
    }

    [Fact]
    public void Actual_source_sums_only_realized_expenses_of_the_whole_month()
    {
        Transaction[] txs =
        [
            T(new(2026, 9, 1), Super, 10_000), T(new(2026, 9, 30), Super, 20_400),
            T(new(2026, 9, 15), Super, 99_000, TransactionStatus.Previsto),
            T(new(2026, 10, 1), Super, 50_000),
            T(new(2026, 9, 10), Servicios, 400),
            T(new(2026, 9, 10), Viejo, 70_000),
        ];
        var m = Budgeting.Propose(Cats, [], txs, [], Scope.Familia, BudgetSource.Real, new(2026, 9, 1), Nov, Nov, false, null).Single();
        // 30.400 → 30.000; 400 → 0 se descarta; la categoría inactiva no se presupuesta.
        Assert.Equal(new Dictionary<string, long> { ["Supermercado"] = 30_000 }, Pesos(m));
    }

    [Fact]
    public void Fixed_categories_take_the_fixed_amount_in_force_each_month()
    {
        var alquiler = Rule(Alquiler, Oct, null,
            new() { FromMonth = Oct, Amount = 600_000_00 }, new() { FromMonth = Dic, IncreasePercent = 10 });
        var gimnasio = Rule(Servicios, Oct, Nov, new RecurringRuleAmount { FromMonth = Oct, Amount = 30_000_00 });
        Budget[] budgets = [B(Oct, Alquiler, 500_000), B(Oct, Super, 200_000), B(Oct, Servicios, 50_000)];

        var months = Budgeting.Propose(Cats, budgets, [], [alquiler, gimnasio], Scope.Familia, BudgetSource.Presupuesto, Oct, Nov, Ene, true, 5);

        // Fijo: el monto del fijo (sin el %), con el incremento de diciembre.
        Assert.Equal([600_000, 660_000, 660_000], months.Select(m => Pesos(m)["Alquiler"]));
        Assert.Equal(ProposalOrigin.Fijos, months[0].Rows.First(r => r.Category == Alquiler).Origin);
        // Variable con fijos: el mayor entre la base ajustada (52.500 → 53.000) y los fijos; el fijo termina en noviembre.
        var nov = months[0].Rows.First(r => r.Category == Servicios);
        Assert.Equal((53_000_00L, 30_000_00L, 53_000_00L, ProposalOrigin.Mayor), (nov.Base, nov.Fixed!.Value, nov.Proposed, nov.Origin));
        Assert.Equal(ProposalOrigin.Base, months[1].Rows.First(r => r.Category == Servicios).Origin);
        Assert.Equal(210_000, Pesos(months[2])["Supermercado"]);
    }

    [Fact]
    public void A_fixed_expense_without_base_is_proposed_and_a_variable_takes_the_fixed_when_higher()
    {
        var rule = Rule(Super, Oct, null, new RecurringRuleAmount { FromMonth = Oct, Amount = 250_000_00 });
        var m = Budgeting.Propose(Cats, [B(Oct, Super, 200_000)], [], [rule, Rule(Alquiler, Oct, null, new RecurringRuleAmount { FromMonth = Oct, Amount = 1_00 })],
            Scope.Familia, BudgetSource.Presupuesto, Oct, Nov, Nov, true, null).Single();
        Assert.Equal(new Dictionary<string, long> { ["Alquiler"] = 1, ["Supermercado"] = 250_000 }, Pesos(m));
    }

    [Fact]
    public void Source_of_a_month_tells_if_it_inherits()
    {
        Budget[] budgets = [B(Oct, Super, 1)];
        Assert.Equal((true, (DateOnly?)null), Budgeting.SourceOf(budgets, Oct));
        Assert.Equal((false, (DateOnly?)Oct), Budgeting.SourceOf(budgets, Dic));
        Assert.Equal((false, (DateOnly?)null), Budgeting.SourceOf(budgets, new(2026, 9, 1)));
    }

    // --- API, sobre la foto de octubre ---

    static string Month(int ahead) => DateOnly.FromDateTime(DateTime.Now).AddMonths(ahead).ToString("yyyy-MM");

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

    [Fact]
    public async Task Proposal_from_the_october_budget_and_bulk_save_replace_only_that_scope()
    {
        var c = await Client();
        var octubre = await Json(await c.GetAsync("/budgets/2026-10?scope=familia"));
        var expected = octubre.EnumerateArray().Sum(b => Budgeting.RoundToThousand((long)(b.GetProperty("amount").GetDecimal() * 100)) / 100m);

        var proposal = await Json(await c.GetAsync($"/budgets/proposal?scope=familia&source=budget&sourceMonth=2026-10&from={Month(1)}&to={Month(2)}&useFixed=false"));
        Assert.Equal(2, proposal.GetArrayLength());
        Assert.All(proposal.EnumerateArray(), m => Assert.Equal(expected, m.GetProperty("total").GetDecimal()));
        Assert.Equal("base", proposal[0].GetProperty("rows")[0].GetProperty("origin").GetString());

        var memeyCat = (await Json(await c.GetAsync("/categories?scope=memey"))).EnumerateArray()
            .First(x => x.GetProperty("kind").GetString() == "gasto").GetProperty("id").GetInt32();
        await Json(await c.PutAsJsonAsync($"/budgets/{Month(1)}?scope=memey", new[] { new { month = Month(1), categoryId = memeyCat, amount = 1_000 } }));

        var body = proposal.EnumerateArray().Select(m => new
        {
            month = m.GetProperty("month").GetString(),
            items = m.GetProperty("rows").EnumerateArray().Select(r => new { categoryId = r.GetProperty("categoryId").GetInt32(), amount = r.GetProperty("proposed").GetDecimal() }).ToList(),
        });
        var saved = await Json(await c.PutAsJsonAsync("/budgets?scope=familia", body));
        Assert.Equal(expected * 2, saved.EnumerateArray().Sum(b => b.GetProperty("amount").GetDecimal()));

        // El mes ya tiene presupuesto propio y el de Memey sigue intacto.
        var again = await Json(await c.GetAsync($"/budgets/proposal?scope=familia&source=budget&sourceMonth=2026-10&from={Month(1)}&to={Month(1)}"));
        Assert.True(again[0].GetProperty("hasOwnBudget").GetBoolean());
        Assert.Single((await Json(await c.GetAsync($"/budgets/{Month(1)}?scope=memey"))).EnumerateArray());
        var source = await Json(await c.GetAsync($"/budgets/{Month(1)}/source?scope=familia"));
        Assert.True(source.GetProperty("hasOwnBudget").GetBoolean());
    }

    [Fact]
    public async Task A_month_without_budget_reports_where_it_inherits_from()
    {
        var c = await Client();
        var source = await Json(await c.GetAsync("/budgets/2026-10/source?scope=familia"));
        Assert.True(source.GetProperty("hasOwnBudget").GetBoolean());
        var later = await Json(await c.GetAsync($"/budgets/{Month(11)}/source?scope=familia"));
        Assert.False(later.GetProperty("hasOwnBudget").GetBoolean());
        Assert.False(string.IsNullOrEmpty(later.GetProperty("inheritedFrom").GetString()));
    }

    [Theory]
    [InlineData("source=actual&sourceMonth={0}&from={1}&to={1}")]  // el mes en curso no está cerrado
    [InlineData("source=budget&sourceMonth=2026-10&from={2}&to={1}")] // hasta antes que desde
    [InlineData("source=budget&sourceMonth=2026-10&from={1}&to={3}")] // más de 12 meses
    [InlineData("source=budget&sourceMonth=2026-10&from={4}&to={1}")] // desde en el pasado
    [InlineData("source=otra&sourceMonth=2026-10&from={1}&to={1}")]
    public async Task Invalid_proposals_are_rejected(string query)
    {
        var c = await Client();
        var q = string.Format(query, Month(0), Month(1), Month(2), Month(13), Month(-1));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/budgets/proposal?scope=familia&{q}")).StatusCode);
    }

    [Fact]
    public async Task Bulk_save_rejects_a_category_of_the_other_scope()
    {
        var c = await Client();
        var familiaCat = (await Json(await c.GetAsync("/categories?scope=familia"))).EnumerateArray().First().GetProperty("id").GetInt32();
        var res = await c.PutAsJsonAsync("/budgets?scope=memey", new[] { new { month = Month(1), items = new[] { new { categoryId = familiaCat, amount = 1m } } } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
