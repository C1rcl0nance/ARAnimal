using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class AnchoredLayoutRuntimeLoader : MonoBehaviour
{
    public event Action<int> OnLayoutLoaded;

    [Serializable]
    public class LayoutItem
    {
        public string animalId;
        public string prefabName;
        public float px;
        public float py;
        public float pz;
        public float rx;
        public float ry;
        public float rz;
        public float rw;
        public float sx = 1f;
        public float sy = 1f;
        public float sz = 1f;
    }

    [Serializable]
    public class LayoutData
    {
        public string mapId;
        public List<LayoutItem> items = new List<LayoutItem>();
    }

    [Serializable]
    public class RemoteBundleConfig
    {
        public string animalId;
        public string bundleName;
        public string bundleUrl;
    }

    [Header("Refs")]
    public WorldAnchorManager worldAnchorManager;
    public RemoteAnimalBundleLoader remoteBundleLoader;
    public TileAnimalFlowController flowController;
    public Transform anchoredAnimalRoot;
    public TMP_Text statusText;

    [Header("Layout")]
    public string relativeLayoutPath = "layouts/AnchoredLayout.json";
    public bool autoLoad = true;
    public bool requireLocalized = true;
    public bool loadOnlyOnce = true;
    public bool clearRootBeforeSpawn = true;
    public string targetMapId = "";

    [Header("Bundle Resolve")]
    public List<RemoteBundleConfig> bundleConfigs = new List<RemoteBundleConfig>();
    public string fallbackBundleBaseUrl = "http://115.190.8.234/bundles/android/";

    private readonly Dictionary<string, RemoteBundleConfig> _bundleConfigMap = new Dictionary<string, RemoteBundleConfig>(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private bool _loggedMissingFile;

    public string LayoutFilePath => Path.Combine(Application.persistentDataPath, relativeLayoutPath);

    private void Awake()
    {
        ResolveRefs();
        BuildBundleMap();
    }

    private void Update()
    {
        if (!autoLoad)
        {
            return;
        }

        if (loadOnlyOnce && _loaded)
        {
            return;
        }

        if (!CanLoadNow())
        {
            return;
        }

        StartCoroutine(LoadAndSpawnRoutine());
    }

    public void ReloadNow()
    {
        _loaded = false;
        StartCoroutine(LoadAndSpawnRoutine());
    }

    private bool CanLoadNow()
    {
        if (!requireLocalized)
        {
            return true;
        }

        if (worldAnchorManager == null)
        {
            SetStatus("加载布局失败：worldAnchorManager未绑定");
            return false;
        }

        if (!worldAnchorManager.IsLocalized)
        {
            return false;
        }

        string expect = string.IsNullOrWhiteSpace(targetMapId) ? "" : targetMapId.Trim();
        if (!string.IsNullOrEmpty(expect) && !string.Equals(worldAnchorManager.CurrentPlaceId, expect, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private IEnumerator LoadAndSpawnRoutine()
    {
        if (loadOnlyOnce && _loaded)
        {
            yield break;
        }

        autoLoad = false;

        ResolveRefs();
        BuildBundleMap();

        if (remoteBundleLoader == null)
        {
            SetStatus("加载布局失败：remoteBundleLoader未绑定");
            autoLoad = true;
            yield break;
        }

        if (anchoredAnimalRoot == null)
        {
            SetStatus("加载布局失败：anchoredAnimalRoot未绑定");
            autoLoad = true;
            yield break;
        }

        string layoutPath = LayoutFilePath;
        if (!File.Exists(layoutPath))
        {
            if (!_loggedMissingFile)
            {
                _loggedMissingFile = true;
                SetStatus("布局文件不存在：" + layoutPath);
            }
            autoLoad = true;
            yield break;
        }

        LayoutData layout;
        try
        {
            string json = File.ReadAllText(layoutPath);
            layout = JsonUtility.FromJson<LayoutData>(json);
        }
        catch (Exception ex)
        {
            SetStatus("读取布局失败：" + ex.Message);
            autoLoad = true;
            yield break;
        }

        if (layout == null || layout.items == null)
        {
            SetStatus("布局文件无效");
            autoLoad = true;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(targetMapId))
        {
            string expect = targetMapId.Trim();
            string got = (layout.mapId ?? "").Trim();
            if (!string.Equals(expect, got, StringComparison.Ordinal))
            {
                SetStatus($"布局mapId不匹配，expect={expect}, got={got}");
                autoLoad = true;
                yield break;
            }
        }

        if (clearRootBeforeSpawn)
        {
            ClearChildren(anchoredAnimalRoot);
        }

        int ok = 0;
        for (int i = 0; i < layout.items.Count; i++)
        {
            LayoutItem item = layout.items[i];
            if (item == null)
            {
                continue;
            }

            string animalId = Normalize(item.animalId);
            string prefabName = Normalize(item.prefabName);
            if (string.IsNullOrEmpty(prefabName))
            {
                continue;
            }

            if (!TryResolveBundleConfig(animalId, out RemoteBundleConfig cfg))
            {
                SetStatus($"缺少bundle配置：animalId={animalId}, prefab={prefabName}");
                continue;
            }

            bool done = false;
            GameObject loadedPrefab = null;
            remoteBundleLoader.LoadAnimalPrefab(cfg.bundleName, cfg.bundleUrl, prefabName, p =>
            {
                loadedPrefab = p;
                done = true;
            });

            while (!done)
            {
                yield return null;
            }

            if (loadedPrefab == null)
            {
                Debug.LogWarning($"[AnchoredLayoutRuntimeLoader] prefab load failed: animalId={animalId}, prefab={prefabName}, bundle={cfg.bundleName}");
                continue;
            }

            var instance = Instantiate(loadedPrefab, anchoredAnimalRoot);
            instance.name = prefabName;
            instance.transform.localPosition = new Vector3(item.px, item.py, item.pz);
            instance.transform.localRotation = new Quaternion(item.rx, item.ry, item.rz, item.rw);
            instance.transform.localScale = new Vector3(item.sx, item.sy, item.sz);
            ok++;
        }

        _loaded = true;
        OnLayoutLoaded?.Invoke(ok);
        SetStatus($"布局加载完成：{ok}/{layout.items.Count}");
        autoLoad = true;
    }

    private void BuildBundleMap()
    {
        _bundleConfigMap.Clear();

        for (int i = 0; i < bundleConfigs.Count; i++)
        {
            var entry = bundleConfigs[i];
            if (entry == null)
            {
                continue;
            }

            string animalId = Normalize(entry.animalId);
            if (string.IsNullOrEmpty(animalId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.bundleName))
            {
                entry.bundleName = animalId + "_bundle";
            }

            if (string.IsNullOrWhiteSpace(entry.bundleUrl))
            {
                entry.bundleUrl = BuildFallbackUrl(entry.bundleName);
            }

            _bundleConfigMap[animalId] = entry;
        }

        if (flowController != null)
        {
            for (int i = 0; i < flowController.animalPrefabs.Count; i++)
            {
                var ap = flowController.animalPrefabs[i];
                if (ap == null)
                {
                    continue;
                }

                string animalId = Normalize(ap.animalId);
                if (string.IsNullOrEmpty(animalId))
                {
                    continue;
                }

                if (_bundleConfigMap.ContainsKey(animalId))
                {
                    continue;
                }

                string bundleName = Normalize(ap.remoteBundleName);
                if (string.IsNullOrEmpty(bundleName))
                {
                    bundleName = animalId + "_bundle";
                }

                string bundleUrl = Normalize(ap.remoteBundleUrl);
                if (string.IsNullOrEmpty(bundleUrl))
                {
                    bundleUrl = BuildFallbackUrl(bundleName);
                }

                _bundleConfigMap[animalId] = new RemoteBundleConfig
                {
                    animalId = animalId,
                    bundleName = bundleName,
                    bundleUrl = bundleUrl,
                };
            }
        }
    }

    private bool TryResolveBundleConfig(string animalId, out RemoteBundleConfig cfg)
    {
        cfg = null;
        string normalizedId = Normalize(animalId);
        if (string.IsNullOrEmpty(normalizedId))
        {
            return false;
        }

        if (_bundleConfigMap.TryGetValue(normalizedId, out cfg) && cfg != null)
        {
            return true;
        }

        string bundleName = normalizedId + "_bundle";
        cfg = new RemoteBundleConfig
        {
            animalId = normalizedId,
            bundleName = bundleName,
            bundleUrl = BuildFallbackUrl(bundleName),
        };
        _bundleConfigMap[normalizedId] = cfg;
        return true;
    }

    private string BuildFallbackUrl(string bundleName)
    {
        string baseUrl = string.IsNullOrWhiteSpace(fallbackBundleBaseUrl)
            ? "http://115.190.8.234/bundles/android/"
            : fallbackBundleBaseUrl.Trim();

        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        return baseUrl + bundleName;
    }

    private void ResolveRefs()
    {
        if (worldAnchorManager == null)
        {
            worldAnchorManager = FindObjectOfType<WorldAnchorManager>();
        }

        if (remoteBundleLoader == null)
        {
            remoteBundleLoader = FindObjectOfType<RemoteAnimalBundleLoader>();
        }

        if (flowController == null)
        {
            flowController = FindObjectOfType<TileAnimalFlowController>();
        }

        if (anchoredAnimalRoot == null)
        {
            GameObject root = GameObject.Find("AnchoredAnimalRoot");
            if (root != null)
            {
                anchoredAnimalRoot = root.transform;
            }
        }
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child != null)
            {
                Destroy(child.gameObject);
            }
        }
    }

    private static string Normalize(string input)
    {
        return string.IsNullOrWhiteSpace(input) ? "" : input.Trim();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log("[AnchoredLayoutRuntimeLoader] " + message);
    }
}
