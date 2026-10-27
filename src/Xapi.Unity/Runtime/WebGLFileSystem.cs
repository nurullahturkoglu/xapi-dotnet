#nullable enable

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Xapi.Unity
{
    /// <summary>
    /// Saves <c>Application.persistentDataPath</c> to the browser's IndexedDB in WebGL players. Unity keeps that
    /// directory in memory and writes it to IndexedDB only when told to, so files written there, such as a
    /// <c>FileOutboxStore</c> journal, are gone after a page reload unless this runs after they change.
    /// </summary>
    public static class WebGLFileSystem
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void XapiUnity_PersistFileSystem();
#endif

        /// <summary>
        /// Starts saving <c>Application.persistentDataPath</c> to IndexedDB. The browser finishes it after the
        /// current frame. Outside WebGL players it does nothing.
        /// </summary>
        public static void Persist()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            XapiUnity_PersistFileSystem();
#endif
        }
    }
}
