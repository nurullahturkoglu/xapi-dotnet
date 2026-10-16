namespace Xapi.Lrs;

/// <summary>Names used by the built-in Basic authentication.</summary>
public static class XapiLrsAuthenticationDefaults
{
    /// <summary>
    /// The ASP.NET Core authentication scheme that checks Basic credentials against
    /// <see cref="XapiLrsOptions.Credentials"/>. <c>AddXapiLrs</c> registers it.
    /// </summary>
    public const string AuthenticationScheme = "XapiBasic";
}
