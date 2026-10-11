using PersonalPlatform.Domain.Common;
using PersonalPlatform.Domain.Scheduling;

namespace PersonalPlatform.Domain.Finance;

/// <summary>
/// Template that produces a transaction on each scheduled date. Generated transactions are ordinary
/// transactions (editable independently); each carries the occurrence date, and a unique index on
/// <c>(recurring_transaction_id, recurrence_date)</c> makes posting idempotent.
/// </summary>
public sealed class RecurringTransaction : IOwnedEntity
{
    /// <summary>Upper bound of occurrences posted per call, protecting against very long catch-ups.</summary>
    public const int MaxOccurrencesPerPosting = 400;

    private RecurringTransaction()
    {
    }

    /// <summary>A new template is posted from its start date (chosen explicitly by the user, possibly in the past).</summary>
    public RecurringTransaction(Guid ownerId, TransactionDetails sample, RecurrencePattern pattern, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        CreatedAt = now;
        SetTemplate(sample, pattern, now);
        NextOccurrence = pattern.FirstOnOrAfter(pattern.StartDate);
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public TransactionKind Kind { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid? DestinationAccountId { get; private set; }

    public decimal? DestinationAmount { get; private set; }

    public Guid? CategoryId { get; private set; }

    public decimal Amount { get; private set; }

    public string? Description { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    public int Interval { get; private set; }

    public Weekdays Days { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    /// <summary>Next date that has not been posted yet; null when the schedule has ended.</summary>
    public DateOnly? NextOccurrence { get; private set; }

    public bool IsPaused { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public RecurrencePattern Pattern => new(Frequency, Interval, Days, StartDate, EndDate);

    /// <summary>
    /// Editing a template never back-fills history: it continues from today or the new start date,
    /// whichever is later. Dates that were already posted are skipped thanks to the unique index.
    /// Already generated transactions are not modified.
    /// </summary>
    public void Reconfigure(TransactionDetails sample, RecurrencePattern pattern, DateOnly today, DateTimeOffset now)
    {
        Rule.Require(sample.Kind == Kind, "kind_change_not_allowed", "Template kind cannot be changed.");
        SetTemplate(sample, pattern, now);
        NextOccurrence = pattern.FirstOnOrAfter(pattern.StartDate > today ? pattern.StartDate : today);
    }

    public void Pause(DateTimeOffset now)
    {
        IsPaused = true;
        UpdatedAt = now;
    }

    /// <summary>Resuming does not back-fill dates missed while paused.</summary>
    public void Resume(DateOnly today, DateTimeOffset now)
    {
        IsPaused = false;
        if (NextOccurrence is { } next && next < today)
        {
            NextOccurrence = Pattern.FirstOnOrAfter(today);
        }

        UpdatedAt = now;
    }

    /// <summary>Dates due up to and including <paramref name="today"/>, bounded.</summary>
    public IReadOnlyList<DateOnly> DueDates(DateOnly today)
    {
        if (IsPaused || NextOccurrence is not { } next || next > today)
        {
            return [];
        }

        return Pattern.Between(next, today, MaxOccurrencesPerPosting);
    }

    public void MarkPostedThrough(DateOnly lastPosted, DateTimeOffset now)
    {
        NextOccurrence = Pattern.NextAfter(lastPosted);
        UpdatedAt = now;
    }

    private void SetTemplate(TransactionDetails sample, RecurrencePattern pattern, DateTimeOffset now)
    {
        // Validates the template exactly like a real transaction (ownership, archived accounts,
        // category kind, precision), so a template can never produce an invalid transaction.
        _ = FinancialTransaction.Create(OwnerId, sample, now);

        Kind = sample.Kind;
        AccountId = sample.Source.Id;
        DestinationAccountId = sample.Destination?.Id;
        DestinationAmount = sample.Destination is null ? null : sample.DestinationAmount;
        CategoryId = sample.Category?.Id;
        Amount = sample.Amount;
        Description = Rule.OptionalText(sample.Description, FinancialTransaction.DescriptionMaxLength, "invalid_description", "Description");
        Frequency = pattern.Frequency;
        Interval = pattern.Interval;
        Days = pattern.Days;
        StartDate = pattern.StartDate;
        EndDate = pattern.EndDate;
        UpdatedAt = now;
    }
}
