namespace Xapi.Client.Outbox.Internal;

/// <summary>One change to the outbox, as written to the <see cref="FileOutboxStore"/> journal.</summary>
internal abstract record JournalRecord;

internal sealed record AddRecord(OutboxEntry Entry) : JournalRecord;

internal sealed record RemoveRecord(IReadOnlyList<Guid> Ids) : JournalRecord;

internal sealed record AttemptRecord(IReadOnlyList<Guid> Ids, string Error) : JournalRecord;

internal sealed record DeadLetterRecord(OutboxDeadLetter DeadLetter) : JournalRecord;

internal sealed record RequeueRecord(Guid Id) : JournalRecord;

internal sealed record RemoveDeadLetterRecord(Guid Id) : JournalRecord;
