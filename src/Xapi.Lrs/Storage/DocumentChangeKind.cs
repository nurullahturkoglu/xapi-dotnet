using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>The kinds of <see cref="DocumentChange"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public enum DocumentChangeKind
{
    /// <summary>Nothing is written.</summary>
    None,

    /// <summary>The document is created or replaced.</summary>
    Store,

    /// <summary>The document is deleted.</summary>
    Delete,
}
