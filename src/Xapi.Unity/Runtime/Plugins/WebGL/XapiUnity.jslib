mergeInto(LibraryManager.library, {
  XapiUnity_PersistFileSystem: function () {
    // Unity mounts Application.persistentDataPath with IDBFS. queuePersist batches the writes of one frame and
    // queues a sync behind one already running; FS.syncfs is the Emscripten API it is built on.
    if (typeof IDBFS !== 'undefined' && IDBFS.queuePersist && Module.__unityIdbfsMount) {
      IDBFS.queuePersist(Module.__unityIdbfsMount.mount);
    } else {
      FS.syncfs(false, function (error) {
        if (error) console.error('Could not save Application.persistentDataPath to IndexedDB', error);
      });
    }
  },
});
