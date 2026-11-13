using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class RemoteAnimalBundleLoader : MonoBehaviour
{
    private sealed class StreamingDownloadHandler : DownloadHandlerScript
    {
        private readonly FileStream fileStream;
        public ulong ReceivedBytes { get; private set; }

        public StreamingDownloadHandler(string path, int bufferSize = 64 * 1024) : base(new byte[bufferSize])
        {
            fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
            {
                return true;
            }

            fileStream.Write(data, 0, dataLength);
            ReceivedBytes += (ulong)dataLength;
            return true;
        }

        protected override void CompleteContent()
        {
            fileStream.Flush();
        }

        public void CloseStream()
        {
            try
            {
                fileStream.Dispose();
            }
            catch
            {
            }
        }
    }

    [Header("Debug")]
    public bool debugLog = true;

    public event Action<string> BundleDownloadStarted;
    public event Action<string, float> BundleDownloadProgress;
    public event Action<string, bool, string> BundleDownloadFinished;
    public event Action<string, ulong, ulong> BundleDownloadBytes;

    private readonly Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
    private readonly Dictionary<string, Coroutine> loadCoroutines = new Dictionary<string, Coroutine>();
    private readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

    public bool IsBundleCached(string bundleName, string bundleUrl = "")
    {
        string normalizedBundleName = NormalizeBundleName(bundleName, bundleUrl);
        if (string.IsNullOrWhiteSpace(normalizedBundleName))
        {
            return false;
        }

        if (loadedBundles.TryGetValue(normalizedBundleName, out AssetBundle loadedBundle) && loadedBundle != null)
        {
            return true;
        }

        string localPath = GetLocalBundlePath(normalizedBundleName);
        return File.Exists(localPath);
    }

    public void PreloadBundle(string bundleName, string bundleUrl, Action<bool> onFinished = null)
    {
        string normalizedBundleName = NormalizeBundleName(bundleName, bundleUrl);
        if (string.IsNullOrWhiteSpace(normalizedBundleName))
        {
            onFinished?.Invoke(false);
            return;
        }

        StartCoroutine(PreloadBundleRoutine(normalizedBundleName, bundleUrl, onFinished));
    }

    public void LoadAnimalPrefab(string bundleName, string bundleUrl, string prefabName, Action<GameObject> onLoaded)
    {
        string normalizedBundleName = NormalizeBundleName(bundleName, bundleUrl);
        if (string.IsNullOrWhiteSpace(normalizedBundleName))
        {
            Debug.LogError("[RemoteAnimalBundleLoader] bundleName and bundleUrl are both empty.");
            onLoaded?.Invoke(null);
            return;
        }

        if (string.IsNullOrWhiteSpace(prefabName))
        {
            Debug.LogWarning("[RemoteAnimalBundleLoader] prefabName is empty.");
            onLoaded?.Invoke(null);
            return;
        }

        string prefabKey = BuildPrefabCacheKey(normalizedBundleName, prefabName.Trim());
        if (prefabCache.TryGetValue(prefabKey, out GameObject cached) && cached != null)
        {
            onLoaded?.Invoke(cached);
            return;
        }

        StartCoroutine(LoadAnimalPrefabRoutine(normalizedBundleName, bundleUrl, prefabName.Trim(), onLoaded));
    }

    private IEnumerator LoadAnimalPrefabRoutine(string bundleName, string bundleUrl, string prefabName, Action<GameObject> onLoaded)
    {
        yield return EnsureBundleLoaded(bundleName, bundleUrl);

        if (!loadedBundles.TryGetValue(bundleName, out AssetBundle loadedBundle) || loadedBundle == null)
        {
            onLoaded?.Invoke(null);
            yield break;
        }

        AssetBundleRequest request = loadedBundle.LoadAssetAsync<GameObject>(prefabName);
        yield return request;

        GameObject prefab = request.asset as GameObject;
        if (prefab == null)
        {
            Debug.LogError($"[RemoteAnimalBundleLoader] Prefab not found in bundle: {prefabName}");
            onLoaded?.Invoke(null);
            yield break;
        }

        prefabCache[BuildPrefabCacheKey(bundleName, prefabName)] = prefab;
        onLoaded?.Invoke(prefab);
    }

    private IEnumerator PreloadBundleRoutine(string bundleName, string bundleUrl, Action<bool> onFinished)
    {
        yield return EnsureBundleLoaded(bundleName, bundleUrl);
        bool success = loadedBundles.TryGetValue(bundleName, out AssetBundle loadedBundle) && loadedBundle != null;
        onFinished?.Invoke(success);
    }

    private IEnumerator EnsureBundleLoaded(string bundleName, string bundleUrl)
    {
        if (loadedBundles.TryGetValue(bundleName, out AssetBundle loadedBundle) && loadedBundle != null)
        {
            yield break;
        }

        if (loadCoroutines.TryGetValue(bundleName, out Coroutine loadCoroutine) && loadCoroutine != null)
        {
            yield return loadCoroutine;
            yield break;
        }

        loadCoroutine = StartCoroutine(LoadBundleRoutine(bundleName, bundleUrl));
        loadCoroutines[bundleName] = loadCoroutine;
        yield return loadCoroutine;
        loadCoroutines[bundleName] = null;
    }

    private IEnumerator LoadBundleRoutine(string bundleName, string bundleUrl)
    {
        string localPath = GetLocalBundlePath(bundleName);
        if (File.Exists(localPath))
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(localPath);
            if (bundle != null)
            {
                loadedBundles[bundleName] = bundle;
                Log($"Loaded bundle from local cache: {localPath}");
                ulong localSize = TryGetFileLength(localPath);
                BundleDownloadBytes?.Invoke(bundleName, localSize, localSize);
                BundleDownloadFinished?.Invoke(bundleName, true, "cache");
                yield break;
            }

            Debug.LogWarning($"[RemoteAnimalBundleLoader] Local bundle load failed, redownloading: {localPath}");
            File.Delete(localPath);
        }

        if (string.IsNullOrWhiteSpace(bundleUrl))
        {
            Debug.LogError($"[RemoteAnimalBundleLoader] bundleUrl is empty for bundle '{bundleName}'.");
            yield break;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath));

        string tempPath = localPath + ".downloading";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var streamingHandler = new StreamingDownloadHandler(tempPath);
        using (UnityWebRequest request = new UnityWebRequest(bundleUrl.Trim(), UnityWebRequest.kHttpVerbGET))
        {
            request.downloadHandler = streamingHandler;

            Log($"Downloading bundle '{bundleName}': {bundleUrl}");
            BundleDownloadStarted?.Invoke(bundleName);

            var operation = request.SendWebRequest();
            ulong totalBytes = 0;
            float elapsed = 0f;
            float debugTimer = 0f;

            while (!operation.isDone)
            {
                elapsed += Time.unscaledDeltaTime;
                debugTimer += Time.unscaledDeltaTime;

                if (totalBytes == 0)
                {
                    totalBytes = TryGetTotalBytes(request);
                }

                ulong downloadedBytes = CalculateDownloadedBytes(request, streamingHandler, tempPath, totalBytes);
                float progress = CalculateProgress(request, downloadedBytes, totalBytes);

                if (progress <= 0.0001f && elapsed > 0.35f)
                {
                    BundleDownloadProgress?.Invoke(bundleName, -1f);
                }
                else
                {
                    BundleDownloadProgress?.Invoke(bundleName, Mathf.Clamp01(progress));
                }

                BundleDownloadBytes?.Invoke(bundleName, downloadedBytes, totalBytes);

                if (debugLog && debugTimer >= 0.5f)
                {
                    debugTimer = 0f;
                    ulong tempFileBytes = TryGetFileLength(tempPath);
                    Debug.Log($"[RemoteAnimalBundleLoader] downloading '{bundleName}': bytes={downloadedBytes}, stream={streamingHandler.ReceivedBytes}, temp={tempFileBytes}, unity={request.downloadedBytes}, total={totalBytes}, progress={request.downloadProgress:0.000}, code={request.responseCode}");
                }

                yield return null;
            }

            ulong finalBytes = CalculateDownloadedBytes(request, streamingHandler, tempPath, totalBytes);
            BundleDownloadBytes?.Invoke(bundleName, finalBytes, totalBytes);
            BundleDownloadProgress?.Invoke(bundleName, 1f);

            if (request.result != UnityWebRequest.Result.Success)
            {
                streamingHandler.CloseStream();
                Debug.LogError($"[RemoteAnimalBundleLoader] Bundle download failed: {request.error}, code={request.responseCode}");
                BundleDownloadFinished?.Invoke(bundleName, false, request.error);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                yield break;
            }
        }

        streamingHandler.CloseStream();

        if (!File.Exists(tempPath))
        {
            Debug.LogError($"[RemoteAnimalBundleLoader] Temp download file missing: {tempPath}");
            BundleDownloadFinished?.Invoke(bundleName, false, "temp_file_missing");
            yield break;
        }

        if (File.Exists(localPath))
        {
            File.Delete(localPath);
        }

        File.Move(tempPath, localPath);

        AssetBundle loadedBundle = AssetBundle.LoadFromFile(localPath);
        if (loadedBundle == null)
        {
            Debug.LogError($"[RemoteAnimalBundleLoader] Downloaded bundle could not be loaded: {localPath}");
            BundleDownloadFinished?.Invoke(bundleName, false, "load_failed");
            yield break;
        }

        loadedBundles[bundleName] = loadedBundle;
        Log($"Downloaded and loaded bundle: {localPath}");
        BundleDownloadFinished?.Invoke(bundleName, true, "downloaded");
    }

    private static ulong CalculateDownloadedBytes(UnityWebRequest request, StreamingDownloadHandler streamingHandler, string tempPath, ulong totalBytes)
    {
        ulong downloadedBytes = 0;

        if (streamingHandler != null && streamingHandler.ReceivedBytes > downloadedBytes)
        {
            downloadedBytes = streamingHandler.ReceivedBytes;
        }

        if (request != null && request.downloadedBytes > downloadedBytes)
        {
            downloadedBytes = request.downloadedBytes;
        }

        ulong tempFileBytes = TryGetFileLength(tempPath);
        if (tempFileBytes > downloadedBytes)
        {
            downloadedBytes = tempFileBytes;
        }

        if (downloadedBytes == 0 && totalBytes > 0 && request != null && request.downloadProgress > 0f)
        {
            downloadedBytes = (ulong)(Mathf.Clamp01(request.downloadProgress) * totalBytes);
        }

        if (downloadedBytes > 0 && totalBytes > 0 && downloadedBytes > totalBytes)
        {
            return totalBytes;
        }

        return downloadedBytes;
    }

    private static float CalculateProgress(UnityWebRequest request, ulong downloadedBytes, ulong totalBytes)
    {
        if (totalBytes > 0)
        {
            return Mathf.Clamp01((float)downloadedBytes / totalBytes);
        }

        if (request == null)
        {
            return 0f;
        }

        return Mathf.Clamp01(request.downloadProgress);
    }

    private void OnDestroy()
    {
        foreach (var kv in loadedBundles)
        {
            if (kv.Value != null)
            {
                kv.Value.Unload(false);
            }
        }

        loadedBundles.Clear();
        loadCoroutines.Clear();
        prefabCache.Clear();
    }

    private static string BuildPrefabCacheKey(string bundleName, string prefabName)
    {
        return bundleName + "::" + prefabName;
    }

    private static string NormalizeBundleName(string bundleName, string bundleUrl)
    {
        if (!string.IsNullOrWhiteSpace(bundleName))
        {
            return bundleName.Trim();
        }

        if (string.IsNullOrWhiteSpace(bundleUrl))
        {
            return null;
        }

        return Path.GetFileName(bundleUrl.Trim());
    }

    private static string GetLocalBundlePath(string bundleName)
    {
        return Path.Combine(Application.persistentDataPath, "AssetBundles", bundleName);
    }

    private void Log(string message)
    {
        if (debugLog)
        {
            Debug.Log($"[RemoteAnimalBundleLoader] {message}");
        }
    }

    private static ulong TryGetTotalBytes(UnityWebRequest request)
    {
        if (request == null)
        {
            return 0;
        }

        string contentLength = request.GetResponseHeader("Content-Length");
        if (string.IsNullOrWhiteSpace(contentLength))
        {
            return 0;
        }

        return ulong.TryParse(contentLength, out ulong result) ? result : 0;
    }

    private static ulong TryGetFileLength(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0;
        }

        try
        {
            return (ulong)new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
