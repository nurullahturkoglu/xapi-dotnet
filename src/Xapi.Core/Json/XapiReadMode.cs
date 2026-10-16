namespace Xapi.Json;

/// <summary>How strictly <see cref="XapiJson"/> reads JSON.</summary>
public enum XapiReadMode
{
    /// <summary>
    /// For input an LRS must judge: rejects unknown properties, explicit nulls, duplicate keys, case-mismatched
    /// names and properties from a later xAPI version, then runs <see cref="Validation.StatementValidator"/>.
    /// </summary>
    Strict,

    /// <summary>
    /// For reading LRS responses: ignores unknown properties, nulls and duplicate keys, accepts properties of any
    /// supported version, and skips validation. Wrong JSON types and missing required properties still fail.
    /// </summary>
    Lenient,
}
