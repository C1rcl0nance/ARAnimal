using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class LayoutFileBootstrapper : MonoBehaviour
{
    [Header("Layout Path")]
    public string relativeLayoutPath = "layouts/AnchoredLayout.json";
    public bool overwriteIfExists = false;
    public bool copyOnStart = true;

    public string PersistentLayoutPath => Path.Combine(Application.persistentDataPath, relativeLayoutPath);

    private void Start()
    {
        if (!copyOnStart)
        {
            return;
        }

        StartCoroutine(CopyLayoutIfNeeded());
    }

    public IEnumerator CopyLayoutIfNeeded()
    {
        string persistentPath = PersistentLayoutPath;
        if (File.Exists(persistentPath) && !overwriteIfExists)
        {
            Debug.Log("[LayoutFileBootstrapper] Skip copy, persistent layout already exists: " + persistentPath);
            yield break;
        }

        string directory = Path.GetDirectoryName(persistentPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string streamingPath = Path.Combine(Application.streamingAssetsPath, relativeLayoutPath).Replace("\\", "/");

#if UNITY_ANDROID && !UNITY_EDITOR
        using (UnityWebRequest request = UnityWebRequest.Get(streamingPath))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[LayoutFileBootstrapper] Read StreamingAssets failed: {request.error}, path={streamingPath}");
                yield break;
            }

            File.WriteAllBytes(persistentPath, request.downloadHandler.data);
        }
#else
        if (!File.Exists(streamingPath))
        {
            Debug.LogError("[LayoutFileBootstrapper] Streaming layout not found: " + streamingPath);
            yield break;
        }

        File.Copy(streamingPath, persistentPath, true);
#endif

        Debug.Log("[LayoutFileBootstrapper] Layout copied to persistent path: " + persistentPath);
    }
}
