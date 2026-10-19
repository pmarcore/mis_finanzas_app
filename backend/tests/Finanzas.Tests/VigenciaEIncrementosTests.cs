using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finanzas.Domain;

namespace Finanzas.Tests;

/// <summary>Gastos fijos con vigencia desde-hasta e incrementos % previstos que se acumulan sobre el monto vigente.</summary>
public class VigenciaEIncrementosTests : IClassFixture<PlanillaOctubreTests.Api>
{
    readonly PlanillaOctubreTests.Api api;
    public VigenciaEIncrementosTests(PlanillaOctubreTests.Api api) => this.api = api;

    static readonly OperationType Gasto = new() { Id = 2, Name = "Gasto", Kind = OperationKind.Movimiento, Effect = CashEffect.Resta, ForFamilia = true };

    [Fact]
    public void Increases_compound_on_the_amount_in_force_and_a_new_amount_replaces_them()
    {
        var rule = new RecurringRule
        {
            Id = 1, Scope = Scope.Familia, OperationType = Gasto, Description = "Alquiler", StartMonth = new(2026, 1, 1),
            EndMonth = new(2026, 10, 1), DayOfMonth = 10, PaymentMethodId = 1,
            Amounts =
            [
                new() { FromMonth = new(2026, 1, 1), Amount = 1_000_000_00 },
                new() { FromMonth = new(2026, 4, 1), IncreasePercent = 10 },
                new() { FromMonth = new(2026, 7, 1), IncreasePercent = 5 },
                new() { FromMonth = new(2026, 9, 1), Amount = 1_200_000_00 },
                new() { FromMonth = new(2026, 10, 1), IncreasePercent = -2.5m },
            ],
        };
        Assert.Equal(1_000_000_00, RecurringRuleExpander.AmountFor(rule, new(2026, 3, 1)));
        Assert.Equal(1_100_000_00, RecurringRuleExpander.AmountFor(rule, new(2026, 4, 1)));
        Assert.Equal(1_155_000_00, RecurringRuleExpander.AmountFor(rule, new(2026, 8, 1)));
        Assert.Equal(1_200_000_00, RecurringRuleExpander.AmountFor(rule, new(2026, 9, 1)));

        var debit = new PaymentMethod { Id = 1, Name = "Débito", Type = PaymentMethodType.Debito, CashBoxId = 1 };
        var expanded = RecurringRuleExpander.Expand(rule, debit, new(2026, 12, 1)).ToList();
        // La vigencia corta en octubre aunque el horizonte llegue a diciembre.
        Assert.Equal(10, expanded.Count);
        Assert.Equal(1_170_000_00, expanded[^1].Amount);
    }

    static object Rule(int categoryId, string start, string? end, params object[] amounts) => new
    {
        id = 0, scope = "familia", personId = 1, operationTypeId = 2, description = "Prepaga", categoryId,
        paymentMethodId = 1, dayOfMonth = 15, startMonth = start, endMonth = end, amounts,
    };

    static string Month(int ahead) => DateOnly.FromDateTime(DateTime.Now).AddMonths(ahead).ToString("yyyy-MM");

    [Fact]
    public async Task Previstos_of_the_whole_range_carry_the_increases()
    {
        await api.EnsureImportedAsync();
        var c = api.CreateClient();
        var categories = await c.GetFromJsonAsync<JsonElement>("/categories?scope=familia");
        var gasto = categories.EnumerateArray().First(x => x.GetProperty("kind").GetString() == "gasto").GetProperty("id").GetInt32();

        var res = await c.PostAsJsonAsync("/recurring-rules", Rule(gasto, Month(1), Month(3),
            new { fromMonth = Month(1), amount = 100_000m },
            new { fromMonth = Month(2), amount = 0m, increasePercent = 10m },
            new { fromMonth = Month(3), amount = 0m, increasePercent = 5m }));
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var rule = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rule");
        var id = rule.GetProperty("id").GetInt32();

        // El DTO devuelve el monto que resulta de cada incremento.
        Assert.Equal([100_000m, 110_000m, 115_500m], rule.GetProperty("amounts").EnumerateArray().Select(a => a.GetProperty("amount").GetDecimal()));
        Assert.Equal(10m, rule.GetProperty("amounts")[1].GetProperty("increasePercent").GetDecimal());

        var previstos = (await c.GetFromJsonAsync<JsonElement>("/transactions")).EnumerateArray()
            .Where(t => t.TryGetProperty("recurringRuleId", out var r) && r.ValueKind == JsonValueKind.Number && r.GetInt32() == id)
            .OrderBy(t => t.GetProperty("date").GetString())
            .Select(t => t.GetProperty("amount").GetDecimal()).ToList();
        Assert.Equal([100_000m, 110_000m, 115_500m], previstos);
    }

    [Fact]
    public async Task The_first_change_must_be_an_amount_and_the_end_cannot_precede_the_start()
    {
        var c = api.CreateClient();
        var onlyIncrease = await c.PostAsJsonAsync("/recurring-rules", Rule(1, Month(1), null, new { fromMonth = Month(1), amount = 0m, increasePercent = 10m }));
        Assert.Equal(HttpStatusCode.BadRequest, onlyIncrease.StatusCode);

        var backwards = await c.PostAsJsonAsync("/recurring-rules", Rule(1, Month(3), Month(1), new { fromMonth = Month(3), amount = 1_000m }));
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }
}
