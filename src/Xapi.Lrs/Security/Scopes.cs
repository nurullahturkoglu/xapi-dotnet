namespace Xapi.Lrs.Security;

[Flags]
internal enum ScopeSet
{
    None = 0,
    StatementsWrite = 1 << 0,
    StatementsReadMine = 1 << 1,
    StatementsRead = 1 << 2,
    State = 1 << 3,
    Define = 1 << 4,
    Profile = 1 << 5,
    AllRead = 1 << 6,
    All = 1 << 7,
}

internal static class Scopes
{
    public static string Name(XapiScope scope) => scope switch
    {
        XapiScope.StatementsWrite => "statements/write",
        XapiScope.StatementsReadMine => "statements/read/mine",
        XapiScope.StatementsRead => "statements/read",
        XapiScope.State => "state",
        XapiScope.Define => "define",
        XapiScope.Profile => "profile",
        XapiScope.AllRead => "all/read",
        XapiScope.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    public static ScopeSet Parse(string name) => name switch
    {
        "statements/write" => ScopeSet.StatementsWrite,
        "statements/read/mine" => ScopeSet.StatementsReadMine,
        "statements/read" => ScopeSet.StatementsRead,
        "state" => ScopeSet.State,
        "define" => ScopeSet.Define,
        "profile" => ScopeSet.Profile,
        "all/read" => ScopeSet.AllRead,
        "all" => ScopeSet.All,
        _ => ScopeSet.None,
    };

    public static ScopeSet ParseList(IEnumerable<string> values)
    {
        var set = ScopeSet.None;
        foreach (var value in values)
        {
            foreach (var name in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                set |= Parse(name);
            }
        }

        return set;
    }

    public static bool CanWriteStatements(ScopeSet set) => (set & (ScopeSet.All | ScopeSet.StatementsWrite)) != 0;

    public static bool CanReadAllStatements(ScopeSet set) => (set & (ScopeSet.All | ScopeSet.AllRead | ScopeSet.StatementsRead)) != 0;

    public static bool CanReadStatements(ScopeSet set) => CanReadAllStatements(set) || (set & ScopeSet.StatementsReadMine) != 0;

    public static bool CanDefine(ScopeSet set) => (set & (ScopeSet.All | ScopeSet.Define)) != 0;

    // state and profile grant reading and writing their documents; all/read grants reading (1.0.3
    // Communication 4.0, OAuth scopes)
    public static bool CanUseState(ScopeSet set, bool write) => (set & (ScopeSet.All | ScopeSet.State | (write ? ScopeSet.None : ScopeSet.AllRead))) != 0;

    public static bool CanUseProfiles(ScopeSet set, bool write) => (set & (ScopeSet.All | ScopeSet.Profile | (write ? ScopeSet.None : ScopeSet.AllRead))) != 0;
}
