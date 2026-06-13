namespace Orderly.Core.Commerce;

/// <summary>
/// Industry-agnostic monetary value object.
/// Every monetary value is a <see cref="decimal"/> constrained to the inclusive range
/// -999,999,999.99 .. 999,999,999.99 with a scale of exactly 2 decimal places (Req 2.6).
/// Values are normalized (rounded) to 2 decimal places; magnitudes outside the range are
/// rejected rather than silently truncated.
/// </summary>
public readonly struct CommerceMoney : IEquatable<CommerceMoney>, IComparable<CommerceMoney>
{
    /// <summary>Inclusive lower bound of a valid monetary value.</summary>
    public const decimal MinValue = -999_999_999.99m;

    /// <summary>Inclusive upper bound of a valid monetary value.</summary>
    public const decimal MaxValue = 999_999_999.99m;

    /// <summary>The number of decimal places every monetary value is normalized to.</summary>
    public const int Scale = 2;

    private CommerceMoney(decimal amount)
    {
        Amount = amount;
    }

    /// <summary>The normalized monetary amount: in range and scaled to exactly 2 decimal places.</summary>
    public decimal Amount { get; }

    /// <summary>A zero monetary value (0.00).</summary>
    public static CommerceMoney Zero { get; } = new(0.00m);

    /// <summary>
    /// Creates a <see cref="CommerceMoney"/> from a raw decimal, normalizing it to 2 decimal places.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the normalized value falls outside the inclusive range
    /// <see cref="MinValue"/> .. <see cref="MaxValue"/>.
    /// </exception>
    public static CommerceMoney From(decimal value)
    {
        if (!TryFrom(value, out var money))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Monetary value must be within the inclusive range {MinValue} .. {MaxValue}.");
        }

        return money;
    }

    /// <summary>
    /// Attempts to create a <see cref="CommerceMoney"/> from a raw decimal. Returns <c>false</c>
    /// (without throwing) when the normalized value is out of range.
    /// </summary>
    public static bool TryFrom(decimal value, out CommerceMoney money)
    {
        decimal normalized = Normalize(value);
        if (normalized < MinValue || normalized > MaxValue)
        {
            money = default;
            return false;
        }

        money = new CommerceMoney(normalized);
        return true;
    }

    /// <summary>
    /// Rounds <paramref name="value"/> to exactly 2 decimal places (away from zero on a tie) and
    /// guarantees the resulting decimal carries a scale of exactly 2 (e.g. 1 -> 1.00).
    /// </summary>
    private static decimal Normalize(decimal value)
    {
        // Math.Round limits the scale to <= 2; adding 0.00m guarantees the scale is exactly 2.
        return Math.Round(value, Scale, MidpointRounding.AwayFromZero) + 0.00m;
    }

    public int CompareTo(CommerceMoney other) => Amount.CompareTo(other.Amount);

    public bool Equals(CommerceMoney other) => Amount == other.Amount;

    public override bool Equals(object? obj) => obj is CommerceMoney other && Equals(other);

    public override int GetHashCode() => Amount.GetHashCode();

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator ==(CommerceMoney left, CommerceMoney right) => left.Equals(right);

    public static bool operator !=(CommerceMoney left, CommerceMoney right) => !left.Equals(right);

    public static bool operator <(CommerceMoney left, CommerceMoney right) => left.CompareTo(right) < 0;

    public static bool operator >(CommerceMoney left, CommerceMoney right) => left.CompareTo(right) > 0;

    public static bool operator <=(CommerceMoney left, CommerceMoney right) => left.CompareTo(right) <= 0;

    public static bool operator >=(CommerceMoney left, CommerceMoney right) => left.CompareTo(right) >= 0;
}
