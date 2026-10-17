namespace Xapi.Client.Outbox;

/// <summary>What <see cref="FileOutboxStore"/> had to repair when it opened its journal.</summary>
public sealed record FileOutboxRecovery
{
    /// <summary>Creates a recovery report.</summary>
    /// <param name="unreadableRecords">The number of journal lines that were skipped; see <see cref="UnreadableRecords"/>.</param>
    /// <param name="discardedTailBytes">The length of the incomplete last line; see <see cref="DiscardedTailBytes"/>.</param>
    /// <param name="corruptJournalCopyPath">The path of a copy of the damaged journal, if one was made.</param>
    public FileOutboxRecovery(int unreadableRecords, long discardedTailBytes, string? corruptJournalCopyPath)
    {
        UnreadableRecords = unreadableRecords;
        DiscardedTailBytes = discardedTailBytes;
        CorruptJournalCopyPath = corruptJournalCopyPath;
    }

    /// <summary>
    /// Complete journal lines that failed their checksum or could not be read, and were skipped. Each one is a
    /// change that had been acknowledged, so a Statement may be missing (a skipped add), sent again (a skipped
    /// removal, harmless because ids are fixed) or still pending (a skipped dead letter). This only happens when the
    /// file was damaged on disk, not after a crash.
    /// </summary>
    public int UnreadableRecords { get; init; }

    /// <summary>
    /// The length of an incomplete last line that was cut off: a write interrupted by a crash or power loss. Its
    /// change was never acknowledged, so nothing that was reported as stored is lost.
    /// </summary>
    public long DiscardedTailBytes { get; init; }

    /// <summary>
    /// When <see cref="UnreadableRecords"/> is not 0, the path of a copy of the journal as it was found, for
    /// inspection. The store itself continues with a rewritten journal.
    /// </summary>
    public string? CorruptJournalCopyPath { get; init; }
}
