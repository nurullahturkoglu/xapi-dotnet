namespace Xapi.Lrs;

internal static class XapiLrsDiagnostics
{
    // [Experimental] id of the storage seams (ADR-011): public for the provider packages and custom stores, but
    // not covered by semantic versioning, also after 1.0, until each seam is declared stable.
    public const string ExperimentalStorage = "XAPI0001";

    // UrlFormat of the [Experimental] attributes: the compiler links the XAPI0001 error to this page
    public const string ExperimentalStorageUrl = "https://nurullahturkoglu.github.io/xapi-dotnet/lrs.html#experimental-storage-api";
}
