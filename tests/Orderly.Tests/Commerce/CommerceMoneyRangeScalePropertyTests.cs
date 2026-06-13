using System;
using System.Globalization;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for <see cref="CommerceMoney"/> range and scale guarantees.
///
/// Property 1: Money values stay in range with scale 2.
/// For ANY decimal routed through <see cref="CommerceMoney"/>: if the correctly-rounded
/// value lies within the inclusive range −999,999,999.99 … 999,999,999.99, the resulting
/// monetary value equals that correctly-rounded input, has a scale of exactly 2 decimal
/// places, and stays within range; if it lies outside the range it is REJECTED
/// (<see cref="CommerceMoney.TryFrom"/> returns false / <see cref="CommerceMoney.From"/>
/// throws) rather than being silently truncated.
///
/// **Validates: Requirements 2.6**
/// </summary>
public class CommerceMoneyRangeScalePropertyTests
{
    // In-range-ish values: integer units across the whole valid magnitude paired with a
    // 9-digit fractional numerator. This exercises rounding to 2 dp (away from zero) and the
    // upper-boundary case where a large fraction rounds the value just outside the range.
    private static readonly Gen<decimal> InRangeGen =
        Gen.Long[-999_999_999L, 999_999_999L].Select(
            Gen.Int[0, 999_999_999],
            (units, fracDigits) =>
            {
                decimal frac = fracDigits / 1_000_000_000m; // 0 .. 0.999999999 (many decimal places)
                return units >= 0 ? units + frac : units - frac;
            });

    // Explicit boundary and midpoint-rounding edge cases, including values that round
    // across the range boundary (e.g. 999,999,999.995 rounds away-from-zero to 1e9 -> rejected).
    private static readonly Gen<decimal> BoundaryGen = Gen.OneOfConst(
        CommerceMoney.MinValue,
        CommerceMoney.MaxValue,
        0m,
        0.00m,
        0.001m,
        -0.001m,
        0.005m,   // rounds away from zero to 0.01 (in range)
        -0.005m,  // rounds away from zero to -0.01 (in range)
        999_999_999.99m,
        999_999_999.991m, // rounds to 999,999,999.99 (in range)
        999_999_999.994m, // rounds to 999,999,999.99 (in range)
        999_999_999.995m, // rounds away from zero to 1,000,000,000.00 (out of range)
        -999_999_999.995m, // rounds away from zero to -1,000,000,000.00 (out of range)
        1_000_000_000m,    // out of range
        -1_000_000_000m,   // out of range
        123.455m,          // midpoint, rounds away from zero to 123.46
        -123.455m,
        123.454m,
        123.456m);

    // Clearly out-of-range large magnitudes (well beyond the valid range, both signs) so the
    // rejection path is exercised heavily. Capped at 1e12 to stay far from decimal overflow.
    private static readonly Gen<decimal> OutOfRangeGen =
        Gen.Long[1_000_000_000L, 1_000_000_000_000L].Select(
            Gen.Int[0, 99],
            Gen.Bool,
            (units, cents, negative) =>
            {
                decimal value = units + cents / 100m;
                return negative ? -value : value;
            });

    // Combined input space spanning in-range, boundary, and out-of-range values. InRangeGen is
    // listed twice to bias generation toward the in-range/rounding path while still routinely
    // hitting boundary and rejection cases.
    private static readonly Gen<decimal> MoneyInputGen =
        Gen.OneOf(InRangeGen, InRangeGen, BoundaryGen, OutOfRangeGen);

    [Fact]
    public void Property1_money_values_stay_in_range_with_scale_2()
    {
        MoneyInputGen.Sample(
            value =>
            {
                // The correctly-rounded input, computed independently of the type under test.
                decimal expected = Math.Round(value, CommerceMoney.Scale, MidpointRounding.AwayFromZero);
                bool inRange = expected >= CommerceMoney.MinValue && expected <= CommerceMoney.MaxValue;

                bool created = CommerceMoney.TryFrom(value, out CommerceMoney money);

                if (inRange)
                {
                    Assert.True(created, $"Expected {value} (rounds to {expected}) to be accepted but TryFrom rejected it.");

                    // Equals the correctly-rounded input.
                    Assert.Equal(expected, money.Amount);

                    // Scale is exactly 2 decimal places.
                    Assert.Equal(CommerceMoney.Scale, GetScale(money.Amount));

                    // Stays within range.
                    Assert.InRange(money.Amount, CommerceMoney.MinValue, CommerceMoney.MaxValue);

                    // From agrees with TryFrom and does not throw for in-range input.
                    CommerceMoney fromMoney = CommerceMoney.From(value);
                    Assert.Equal(money, fromMoney);
                }
                else
                {
                    // Out of range: rejected rather than silently truncated.
                    Assert.False(created, $"Expected {value} (rounds to {expected}) to be rejected but TryFrom accepted it.");
                    Assert.Throws<ArgumentOutOfRangeException>(() => CommerceMoney.From(value));
                }
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("1", "1.00")]
    [InlineData("1.005", "1.01")]   // away-from-zero midpoint rounding
    [InlineData("-1.005", "-1.01")]
    [InlineData("123.456", "123.46")]
    [InlineData("999999999.99", "999999999.99")]
    [InlineData("-999999999.99", "-999999999.99")]
    public void From_accepts_in_range_value_normalized_to_scale_2(string input, string expected)
    {
        decimal value = decimal.Parse(input, CultureInfo.InvariantCulture);

        CommerceMoney money = CommerceMoney.From(value);

        Assert.Equal(expected, money.ToString());
        Assert.Equal(2, GetScale(money.Amount));
    }

    [Theory]
    [InlineData("1000000000")]       // just over the upper bound
    [InlineData("-1000000000")]      // just under the lower bound
    [InlineData("999999999.995")]    // rounds away from zero to 1e9 -> out of range
    [InlineData("-999999999.995")]
    [InlineData("5000000000.55")]
    public void From_throws_and_TryFrom_returns_false_for_out_of_range_value(string input)
    {
        decimal value = decimal.Parse(input, CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => CommerceMoney.From(value));
        Assert.False(CommerceMoney.TryFrom(value, out _));
    }

    /// <summary>Returns the scale (number of decimal places) encoded in a decimal's bits.</summary>
    private static int GetScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}
