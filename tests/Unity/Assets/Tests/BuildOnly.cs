#if UNITY_EDITOR
#nullable enable

using System;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine.TestTools;
using Xapi.Unity.Tests;

[assembly: TestPlayerBuildModifier(typeof(BuildOnly))]
[assembly: PostBuildCleanup(typeof(BuildOnly))]

namespace Xapi.Unity.Tests
{
    // With XAPI_UNITY_PLAYER_PATH set, or -xapiPlayerPath on the command line, a player test run only builds the
    // player there and quits, so the player can run somewhere the editor cannot start it, such as a WebGL build in a
    // headless browser. The player then reports through ConsoleReporter. -buildPlayerPath alone still starts the
    // player.
    public class BuildOnly : ITestPlayerBuildModifier, IPostBuildCleanup
    {
        private static string? Path => Environment.GetEnvironmentVariable("XAPI_UNITY_PLAYER_PATH") ?? Argument("-xapiPlayerPath");

        // CI runs the editor in a container that gets only the environment variables GameCI passes to it
        private static string? Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions playerOptions)
        {
            if (Path != null)
            {
                playerOptions.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost | BuildOptions.WaitForPlayerConnection);
                playerOptions.locationPathName = Path;
            }

            return playerOptions;
        }

        public void Cleanup()
        {
            if (Path != null)
            {
                // Quitting skips the runner's own cleanup, which deletes the scene it built the player from
                EditorApplication.update += () =>
                {
                    foreach (var scene in AssetDatabase.FindAssets("InitTestScene t:Scene", new[] { "Assets" }))
                    {
                        AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(scene));
                    }

                    EditorApplication.Exit(0);
                };
            }
        }
    }
}
#endif
