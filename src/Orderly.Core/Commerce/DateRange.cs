namespace Orderly.Core.Commerce;

/// <summary>
/// Industry-agnostic inclusive date/time window value object with a start and an end.
/// </summary>
public readonly struct DateRange : IEquatable<DateRange>
{
    /// <summary>
    /// Creates a date range. The <paramref name="end"/> must be greater than or equal to
    /// <paramref name="start"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="end"/> precedes <paramref name="start"/>.</exception>
    public DateRange(DateTime start, DateTime end)
    {
        if (end < start)
        {
            throw new ArgumentException("The end of a date range must not precede its start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    /// <summary>The inclusive start of the window.</summary>
    public DateTime Start { get; }

    /// <summary>The inclusive end of the window.</summary>
    public DateTime End { get; }

    /// <summary>The duration spanned by the window.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Returns whether the supplied moment falls within the inclusive window.</summary>
    public bool Contains(DateTime moment) => moment >= Start && moment <= End;

    public bool Equals(DateRange other) => Start == other.Start && End == other.End;

    public override bool Equals(object? obj) => obj is DateRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public override string ToString() => $"{Start:o} .. {End:o}";

    public static bool operator ==(DateRange left, DateRange right) => left.Equals(right);

    public static bool operator !=(DateRange left, DateRange right) => !left.Equals(right);
}
