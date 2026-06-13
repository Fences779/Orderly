using System;
using System.Collections.Generic;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Data.Commerce.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for order recalculation in
/// <see cref="CommerceOrderService.RecalculateOrder"/>.
///
/// Property 2: Order recalculation produces 2dp money and bounded gross margin.
/// For ANY generated order with arbitrary order items, costs, and payments, recalculating the
/// order yields <see cref="Order.Subtotal"/>, <see cref="Order.Total"/>, <see cref="Order.Cost"/>,
/// <see cref="Order.GrossProfit"/>, <see cref="Order.PaidAmount"/>, and
/// <see cref="Order.ReceivableAmount"/> each as a monetary value with a scale of exactly 2 decimal
/// places, and a <see cref="Order.GrossMargin"/> percentage that satisfies
/// 0 ≤ grossMargin ≤ 100 rounded to 2 decimal places.
///
/// **Validates: Requirements 4.2**
/// </summary>
public class OrderRecalculationPropertyTests
{
    /// <summary>The scale (decimal places) every recomputed monetary field must carry (Req 4.2).</summary>
    private const int MoneyScale = 2;

    /// <summary>The scale the gross margin percentage is rounded to (Req 4.2).</summary>
    private const int GrossMarginScale = 2;

    // Monetary amounts for unit price / unit cost: 0.00 .. 10,000.00 at exactly 2 dp. Generating
    // price and cost independently routinely produces cost > price (loss-making lines that drive
    // gross profit negative and exercise the [0,100] clamp) as well as cost < price.
    private static readonly Gen<decimal> MoneyAmountGen =
        Gen.Int[0, 1_000_000].Select(cents => cents / 100m);

    // Line quantities: 0.00 .. 1,000.00 at 2 dp, including fractional quantities so the
    // price × quantity products require rounding to 2 dp.
    private static readonly Gen<decimal> QuantityGen =
        Gen.Int[0, 100_000].Select(hundredths => hundredths / 100m);

    // Payment amounts: 0.00 .. 10,000.00 at 2 dp. Multiple payments may sum above or below an
    // order's total, producing both positive and negative receivable amounts.
    private static readonly Gen<decimal> PaymentAmountGen =
        Gen.Int[0, 1_000_000].Select(cents => cents / 100m);

    // A single order line with arbitrary (in-bounds) quantity, unit price, and unit cost.
    // Bounds are chosen so every aggregate (subtotal, cost, gross profit, receivable) stays inside
    // the valid CommerceMoney range −999,999,999.99 … 999,999,999.99:
    //   max line  = 1,000.00 × 10,000.00 = 10,000,000.00
    //   × up to 12 lines                 = 120,000,000.00  (well within range)
    private static readonly Gen<OrderItem> OrderItemGen =
        QuantityGen.Select(MoneyAmountGen, MoneyAmountGen, (qty, price, cost) => new OrderItem
        {
            OrderId = Guid.NewGuid(),
            Quantity = qty,
            UnitPrice = CommerceMoney.From(price),
            UnitCost = CommerceMoney.From(cost),
        });

    private static readonly Gen<PaymentRecord> PaymentRecordGen =
        PaymentAmountGen.Select(amount => new PaymentRecord
        {
            Amount = CommerceMoney.From(amount),
        });

    // 0..12 line items and 0..8 payments, including the empty cases (zero totals, zero paid).
    private static readonly Gen<OrderItem[]> OrderItemsGen = OrderItemGen.Array[0, 12];
    private static readonly Gen<PaymentRecord[]> PaymentsGen = PaymentRecordGen.Array[0, 8];

    // A complete recalculation input: the line items and the payments recorded against the order.
    private static readonly Gen<(OrderItem[] Items, PaymentRecord[] Payments)> RecalcInputGen =
        OrderItemsGen.Select(PaymentsGen, (items, payments) => (items, payments));

    [Fact]
    public void Property2_recalculation_produces_2dp_money_and_bounded_gross_margin()
    {
        var service = new CommerceOrderService();

        RecalcInputGen.Sample(
            input =>
            {
                var order = new Order { WorkspaceId = Guid.NewGuid() };

                service.RecalculateOrder(order, input.Items, input.Payments);

                // Every recomputed monetary field has a scale of exactly 2 decimal places (Req 4.2).
                AssertMoneyScale2(order.Subtotal, nameof(order.Subtotal));
                AssertMoneyScale2(order.Total, nameof(order.Total));
                AssertMoneyScale2(order.Cost, nameof(order.Cost));
                AssertMoneyScale2(order.GrossProfit, nameof(order.GrossProfit));
                AssertMoneyScale2(order.PaidAmount, nameof(order.PaidAmount));
                AssertMoneyScale2(order.ReceivableAmount, nameof(order.ReceivableAmount));

                // Each line total is likewise normalized to scale 2.
                foreach (OrderItem item in input.Items)
                {
                    AssertMoneyScale2(item.LineTotal, nameof(item.LineTotal));
                }

                // Gross margin is a percentage in the inclusive range [0, 100] (Req 4.2).
                Assert.InRange(order.GrossMargin, 0m, 100m);

                // Gross margin is rounded to (at most) 2 decimal places: rounding it to 2 dp is a
                // no-op, so its scale is ≤ 2.
                Assert.Equal(
                    order.GrossMargin,
                    Math.Round(order.GrossMargin, GrossMarginScale, MidpointRounding.AwayFromZero));
                Assert.True(
                    GetScale(order.GrossMargin) <= GrossMarginScale,
                    $"GrossMargin {order.GrossMargin} has scale {GetScale(order.GrossMargin)} > {GrossMarginScale}.");
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Recalculation_of_empty_order_yields_zero_money_and_zero_margin()
    {
        var service = new CommerceOrderService();
        var order = new Order { WorkspaceId = Guid.NewGuid() };

        service.RecalculateOrder(order, Array.Empty<OrderItem>(), Array.Empty<PaymentRecord>());

        Assert.Equal(CommerceMoney.Zero, order.Subtotal);
        Assert.Equal(CommerceMoney.Zero, order.Total);
        Assert.Equal(CommerceMoney.Zero, order.Cost);
        Assert.Equal(CommerceMoney.Zero, order.GrossProfit);
        Assert.Equal(CommerceMoney.Zero, order.PaidAmount);
        Assert.Equal(CommerceMoney.Zero, order.ReceivableAmount);
        Assert.Equal(0m, order.GrossMargin);
    }

    [Fact]
    public void Recalculation_computes_totals_cost_profit_and_margin_from_lines_and_payments()
    {
        var service = new CommerceOrderService();
        var order = new Order { WorkspaceId = Guid.NewGuid() };

        // 2 × (price 100.00, cost 60.00) => subtotal 200.00, cost 120.00, gross profit 80.00.
        var items = new List<OrderItem>
        {
            new()
            {
                OrderId = Guid.NewGuid(),
                Quantity = 2m,
                UnitPrice = CommerceMoney.From(100.00m),
                UnitCost = CommerceMoney.From(60.00m),
            },
        };
        var payments = new List<PaymentRecord>
        {
            new() { Amount = CommerceMoney.From(50.00m) },
        };

        service.RecalculateOrder(order, items, payments);

        Assert.Equal(CommerceMoney.From(200.00m), order.Subtotal);
        Assert.Equal(CommerceMoney.From(200.00m), order.Total);
        Assert.Equal(CommerceMoney.From(120.00m), order.Cost);
        Assert.Equal(CommerceMoney.From(80.00m), order.GrossProfit);
        Assert.Equal(CommerceMoney.From(50.00m), order.PaidAmount);
        Assert.Equal(CommerceMoney.From(150.00m), order.ReceivableAmount);

        // 80.00 / 200.00 * 100 = 40.00
        Assert.Equal(40.00m, order.GrossMargin);
        Assert.Equal(CommerceMoney.From(200.00m), items[0].LineTotal);
    }

    [Fact]
    public void Recalculation_clamps_gross_margin_to_zero_when_cost_exceeds_total()
    {
        var service = new CommerceOrderService();
        var order = new Order { WorkspaceId = Guid.NewGuid() };

        // Cost (80.00) exceeds price (50.00): gross profit is negative, margin clamps to 0.
        var items = new List<OrderItem>
        {
            new()
            {
                OrderId = Guid.NewGuid(),
                Quantity = 1m,
                UnitPrice = CommerceMoney.From(50.00m),
                UnitCost = CommerceMoney.From(80.00m),
            },
        };

        service.RecalculateOrder(order, items, Array.Empty<PaymentRecord>());

        Assert.Equal(CommerceMoney.From(-30.00m), order.GrossProfit);
        Assert.Equal(0m, order.GrossMargin);
        Assert.InRange(order.GrossMargin, 0m, 100m);
    }

    /// <summary>Asserts a monetary value's amount carries a scale of exactly 2 decimal places.</summary>
    private static void AssertMoneyScale2(CommerceMoney money, string name)
    {
        Assert.Equal(MoneyScale, GetScale(money.Amount));
    }

    /// <summary>Returns the scale (number of decimal places) encoded in a decimal's bits.</summary>
    private static int GetScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}
