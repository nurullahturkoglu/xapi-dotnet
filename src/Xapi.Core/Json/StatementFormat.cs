namespace Xapi.Json;

/// <summary>The <c>format</c> of Statements returned by an LRS (IEEE 9274.1.1, 4.1.6.1 GET parameters).</summary>
public enum StatementFormat
{
    /// <summary>Everything as received.</summary>
    Exact,

    /// <summary>
    /// Only what identifies Agents, Groups, Activities and Verbs: no names, no Activity definitions, no Verb
    /// display, and only identifiers (or, for anonymous Groups, members) for Groups.
    /// </summary>
    Ids,

    /// <summary>
    /// As received, but every language map in Activity definitions and Verb displays holds one language, chosen
    /// with the accept-languages list. Substituting the LRS's canonical Activity definitions is up to the caller.
    /// </summary>
    Canonical,
}
