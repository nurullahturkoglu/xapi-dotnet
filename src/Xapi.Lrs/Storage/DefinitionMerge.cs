namespace Xapi.Lrs.Storage;

// Canonical Activity definitions (IEEE 9274.1.1, 4.2.4.3): a newer definition updates the stored one, but
// language map and extension entries it does not mention are kept, so definitions sent in different
// languages add up instead of replacing each other.
internal static class DefinitionMerge
{
    public static ActivityDefinition? Merge(ActivityDefinition? stored, ActivityDefinition? incoming)
    {
        if (incoming is null)
        {
            return stored;
        }

        if (stored is null)
        {
            return incoming;
        }

        // The interaction properties only make sense together, so they are taken as one unit
        var interaction = incoming.InteractionType is not null ? incoming : stored;

        return new ActivityDefinition
        {
            Name = Merge(stored.Name, incoming.Name),
            Description = Merge(stored.Description, incoming.Description),
            Type = incoming.Type ?? stored.Type,
            MoreInfo = incoming.MoreInfo ?? stored.MoreInfo,
            InteractionType = interaction.InteractionType,
            CorrectResponsesPattern = interaction.CorrectResponsesPattern,
            Choices = interaction.Choices,
            Scale = interaction.Scale,
            Source = interaction.Source,
            Target = interaction.Target,
            Steps = interaction.Steps,
            Extensions = Merge(stored.Extensions, incoming.Extensions),
        };
    }

    private static LanguageMap? Merge(LanguageMap? stored, LanguageMap? incoming)
    {
        if (stored is null || incoming is null)
        {
            return incoming ?? stored;
        }

        var merged = stored;
        foreach (var entry in incoming)
        {
            merged = merged.With(entry.Key, entry.Value);
        }

        return merged;
    }

    private static Extensions? Merge(Extensions? stored, Extensions? incoming)
    {
        if (stored is null || incoming is null)
        {
            return incoming ?? stored;
        }

        var merged = stored;
        foreach (var entry in incoming)
        {
            merged = merged.With(entry.Key, entry.Value);
        }

        return merged;
    }
}
