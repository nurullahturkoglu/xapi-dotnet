using NugetForUnity;
using NugetForUnity.Models;
using UnityEditor;
using UnityEngine;

// Installs the packages from nuget.org the way the "Manage NuGet Packages" window does.
public static class XapiPackages
{
    public static void Install()
    {
        var version = System.Environment.GetEnvironmentVariable("XAPI_PACKAGE_VERSION") ?? "0.1.0-preview.1";
        foreach (var id in new[] { "Xapi.Core", "Xapi.Client", "Xapi.Client.Outbox" })
        {
            var package = new NugetPackageIdentifier(id, version) { IsManuallyInstalled = true };
            if (!NugetPackageInstaller.InstallIdentifier(package))
            {
                Debug.LogError($"Could not install {id} {version}");
                EditorApplication.Exit(1);
            }
        }
    }
}
