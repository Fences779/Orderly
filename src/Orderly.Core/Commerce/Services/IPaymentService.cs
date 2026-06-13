namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Describes how <see cref="IPaymentService"/> should associate a <see cref="CashFlowEntry"/> with a
/// <see cref="PaymentRecord"/> when the payment is recorded (Req 4.18). A payment links to
/// <b>at most one</b> cash-flow entry, so exactly one of three mutually exclusive behaviors applies:
/// record no entry, generate a single new entry, or link to one pre-existing entry.
/// </summary>
public sealed record PaymentCashFlowOptions
{
    private PaymentCashFlowOptions(
        PaymentCashFlowBehavior behavior,
        CashFlowDirection direction,
        string? categoryName,
        Guid? existingCashFlowEntryId)
    {
        Behavior = behavior;
        Direction = direction;
        CategoryName = categoryName;
        ExistingCashFlowEntryId = existingCashFlowEntryId;
    }

    /// <summary>Which cash-flow association behavior this options value selects.</summary>
    public PaymentCashFlowBehavior Behavior { get; }

    /// <summary>
    /// The direction of the entry to generate when <see cref="Behavior"/> is
    /// <see cref="PaymentCashFlowBehavior.Generate"/>. Defaults to <see cref="CashFlowDirection.Income"/>
    /// because a recorded payment is most commonly money received.
    /// </summary>
    public CashFlowDirection Direction { get; }

    /// <summary>Optional category label applied to a generated entry; ignored for other behaviors.</summary>
    public string? CategoryName { get; }

    /// <summary>
    /// The identity of the pre-existing cash-flow entry to link when <see cref="Behavior"/> is
    /// <see cref="PaymentCashFlowBehavior.LinkExisting"/>; <c>null</c> for other behaviors.
    /// </summary>
    public Guid? ExistingCashFlowEntryId { get; }

    /// <summary>Record the payment only; do not generate or link any cash-flow entry.</summary>
    public static PaymentCashFlowOptions None { get; } =
        new(PaymentCashFlowBehavior.None, CashFlowDirection.Income, categoryName: null, existingCashFlowEntryId: null);

    /// <summary>
    /// Generate exactly one new <see cref="CashFlowEntry"/> for the payment and link them in both
    /// directions. The generated entry takes its amount and timestamp from the payment.
    /// </summary>
    /// <param name="direction">The direction of the generated entry (defaults to income).</param>
    /// <param name="categoryName">An optional category label for the generated entry.</param>
    public static PaymentCashFlowOptions Generate(
        CashFlowDirection direction = CashFlowDirection.Income,
        string? categoryName = null)
        => new(PaymentCashFlowBehavior.Generate, direction, categoryName, existingCashFlowEntryId: null);

    /// <summary>
    /// Link the payment to a single pre-existing <see cref="CashFlowEntry"/> identified by
    /// <paramref name="cashFlowEntryId"/> rather than generating a new one.
    /// </summary>
    public static PaymentCashFlowOptions LinkExisting(Guid cashFlowEntryId)
        => new(PaymentCashFlowBehavior.LinkExisting, CashFlowDirection.Income, categoryName: null, cashFlowEntryId);
}

/// <summary>
/// The mutually exclusive cash-flow association behaviors a payment can request (Req 4.18).
/// </summary>
public enum PaymentCashFlowBehavior
{
    /// <summary>Record the payment with no associated cash-flow entry.</summary>
    None = 0,

    /// <summary>Generate exactly one new cash-flow entry and link it to the payment.</summary>
    Generate = 1,

    /// <summary>Link the payment to a single pre-existing cash-flow entry.</summary>
    LinkExisting = 2,
}

/// <summary>
/// The outcome of recording a payment through <see cref="IPaymentService"/>: the persisted
/// <see cref="PaymentRecord"/> together with the single <see cref="CashFlowEntry"/> it is linked to,
/// or <c>null</c> when no entry was generated or linked. By construction the payment links to
/// <b>at most one</b> cash-flow entry (Req 4.18).
/// </summary>
public sealed record PaymentResult
{
    /// <summary>The persisted payment record.</summary>
    public required PaymentRecord Payment { get; init; }

    /// <summary>
    /// The single cash-flow entry generated for or linked to the payment, or <c>null</c> when the
    /// payment was recorded without an associated entry.
    /// </summary>
    public CashFlowEntry? CashFlowEntry { get; init; }
}

/// <summary>
/// Universal payment service (Req 4.1). Records <see cref="PaymentRecord"/> values and, per
/// Req 4.18, generates or links <b>at most one</b> corresponding <see cref="CashFlowEntry"/> for each
/// payment. The single <see cref="PaymentRecord.CashFlowEntryId"/> reference structurally enforces the
/// at-most-one cardinality, and this service never produces a second entry for a payment that is
/// already linked.
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Records <paramref name="payment"/> and associates it with a cash-flow entry as directed by
    /// <paramref name="options"/> (Req 4.18):
    /// <list type="bullet">
    ///   <item><description><see cref="PaymentCashFlowBehavior.None"/> — the payment is persisted with no entry.</description></item>
    ///   <item><description><see cref="PaymentCashFlowBehavior.Generate"/> — exactly one new <see cref="CashFlowEntry"/> is created from the payment's amount and timestamp and linked in both directions.</description></item>
    ///   <item><description><see cref="PaymentCashFlowBehavior.LinkExisting"/> — the payment is linked to the supplied pre-existing entry.</description></item>
    /// </list>
    /// When the payment already references a cash-flow entry, the existing link is reused and no
    /// additional entry is generated, so a payment links to at most one entry.
    /// </summary>
    /// <param name="payment">The payment to record. Not null.</param>
    /// <param name="options">The cash-flow association behavior. Defaults to <see cref="PaymentCashFlowOptions.None"/> when null.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The persisted payment and the single linked entry, or a null entry when none was associated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="payment"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="PaymentCashFlowBehavior.LinkExisting"/> is requested but the referenced entry does not exist.</exception>
    Task<PaymentResult> RecordPaymentAsync(
        PaymentRecord payment,
        PaymentCashFlowOptions? options = null,
        CancellationToken cancellationToken = default);
}
