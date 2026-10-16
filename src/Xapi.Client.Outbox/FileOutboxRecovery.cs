namespace Xapi.Client.Outbox;

/// <summary>What <see cref="FileOutboxStore"/> had to repair when it opened its journal.</summary>
/// <param name="UnreadableRecords">
/// Complete journal lines that failed their checksum or could not be read, and were skipped. Each one is a
/// change that had been acknowledged, so a Statement may be missing (a skipped add), sent again (a skipped
/// removal, harmless because ids are fixed) or still pending (a skipped dead letter). This only happens when the
/// file was damaged on disk, not after a crash.
/// </param>
/// <param name="DiscardedTailBytes">
/// The length of an incomplete last line that was cut off: a write interrupted by a crash or power loss. Its
/// change was never acknowledged, so nothing that was reported as stored is lost.
/// </param>
/// <param name="CorruptJournalCopyPath">
/// When <paramref name="UnreadableRecords"/> is not 0, the path of a copy of the journal as it was found, for
/// inspection. The store itself continues with a rewritten journal.
/// </param>
public sealed record FileOutboxRecovery(int UnreadableRecords, long DiscardedTailBytes, string? CorruptJournalCopyPath);
