using System;
using System.Collections.Generic;
using UnityEngine;

public class TileAnimalFlowController : MonoBehaviour
{
    public Action<string, string, GameObject> AnimalSpawned;

    [System.Serializable]
    public class AnimalPrefabEntry
    {
        public string animalId;
        public GameObject prefab;
        public string remoteBundleName;
        public string remoteBundleUrl;
        public string remotePrefabName;
    }

    private class RemoteAnimalConfig
    {
        public string bundleName;
        public string bundleUrl;
        public string prefabName;
    }

    [Header("Refs")]
    public TileTapDetector tapDetector;
    public AnimalPickerUI animalPickerUI;
    public MonoBehaviour animalSelectionPanelController;
    public Transform animalRoot;
    public WorldAnchorManager worldAnchorManager;
    public RemoteAnimalBundleLoader remoteBundleLoader;

    [Header("Animals")]
    public List<AnimalPrefabEntry> animalPrefabs = new List<AnimalPrefabEntry>();

    [Header("Behavior")]
    public bool openPickerWhenNoSelection = true;

    private readonly Dictionary<string, GameObject> map = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, RemoteAnimalConfig> remoteConfigMap = new Dictionary<string, RemoteAnimalConfig>();
    private readonly Dictionary<string, RemoteAnimalConfig> runtimeRemoteConfigMap = new Dictionary<string, RemoteAnimalConfig>();
    private string manualSelectedAnimalId = "";

    private void Awake()
    {
        RebuildPrefabMap();
    }

    private void Start()
    {
        if (tapDetector != null)
        {
            tapDetector.OnTileTapped += OnTileTapped;
        }
        if (animalPickerUI != null)
        {
            animalPickerUI.OnAnimalSelected += OnAnimalSelected;
        }
    }

    private void OnDestroy()
    {
        if (tapDetector != null)
        {
            tapDetector.OnTileTapped -= OnTileTapped;
        }
        if (animalPickerUI != null)
        {
            animalPickerUI.OnAnimalSelected -= OnAnimalSelected;
        }
    }

    private void RebuildPrefabMap()
    {
        map.Clear();
        remoteConfigMap.Clear();
        runtimeRemoteConfigMap.Clear();
        for (int i = 0; i < animalPrefabs.Count; i++)
        {
            var entry = animalPrefabs[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.animalId))
            {
                continue;
            }

            string animalId = entry.animalId.Trim();
            if (entry.prefab != null)
            {
                map[animalId] = entry.prefab;
            }
            if (!string.IsNullOrWhiteSpace(entry.remotePrefabName))
            {
                remoteConfigMap[animalId] = new RemoteAnimalConfig
                {
                    bundleName = string.IsNullOrWhiteSpace(entry.remoteBundleName) ? "" : entry.remoteBundleName.Trim(),
                    bundleUrl = string.IsNullOrWhiteSpace(entry.remoteBundleUrl) ? "" : entry.remoteBundleUrl.Trim(),
                    prefabName = entry.remotePrefabName.Trim(),
                };
            }
        }
    }

    private void OnTileTapped(GameObject tile, Vector3 worldPos)
    {
        if (!TryGetSelectedAnimalId(out string animalId))
        {
            Debug.Log("[Flow] No animal selected. Select an animal first.");
            if (openPickerWhenNoSelection)
            {
                if (animalPickerUI != null)
                {
                    animalPickerUI.OpenPanel();
                }
                else
                {
                    OpenSelectionPanelFallback();
                }
            }
            return;
        }

        if (TryGetSelectionPanelController(out var panelController))
        {
            if (!panelController.CanPlaceCurrentSelection(out string reason))
            {
                string hint = string.IsNullOrWhiteSpace(reason) ? "空间不足" : reason;
                panelController.ShowPlacementBlockedHint(hint);
                Debug.LogWarning($"[Flow] Placement blocked: {hint}, animal={animalId}");
                return;
            }
        }

        SpawnSelectedAt(animalId, worldPos);
        Debug.Log($"[Flow] Tile tapped: {tile.name}, world={worldPos}");
    }

    private void OnAnimalSelected(string animalId)
    {
        if (string.IsNullOrWhiteSpace(animalId))
        {
            Debug.LogWarning("[Flow] Empty animal selection.");
            return;
        }

        if (!map.ContainsKey(animalId) && !remoteConfigMap.ContainsKey(animalId))
        {
            Debug.LogWarning($"[Flow] Selected animal id has no prefab: {animalId}");
            return;
        }

        Debug.Log($"[Flow] Selected animal: {animalId}");
    }

    private void SpawnSelectedAt(string animalId, Vector3 worldPos)
    {
        if (!map.TryGetValue(animalId, out var prefab) || prefab == null)
        {
            SpawnRemoteAt(animalId, worldPos);
            return;
        }

        Transform parent = animalRoot != null ? animalRoot : transform;
        Quaternion rotation = Quaternion.identity;
        var go = Instantiate(prefab, worldPos, rotation, parent);
        AnimalSpawned?.Invoke(animalId, prefab.name, go);
        Debug.Log($"[Flow] Spawned {animalId} at {worldPos}, go={go.name}");
    }

    private void SpawnRemoteAt(string animalId, Vector3 worldPos)
    {
        if (remoteBundleLoader == null)
        {
            Debug.LogWarning($"[Flow] remoteBundleLoader is not assigned: {animalId}");
            return;
        }

        if (!TryGetRemoteConfig(animalId, out RemoteAnimalConfig config) || config == null)
        {
            Debug.LogWarning($"[Flow] Remote prefab not configured: {animalId}");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.prefabName))
        {
            Debug.LogWarning($"[Flow] Remote prefab name is empty: {animalId}");
            return;
        }

        remoteBundleLoader.LoadAnimalPrefab(config.bundleName, config.bundleUrl, config.prefabName, prefab =>
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[Flow] Remote prefab load failed: {animalId}, bundle={config.bundleName}, prefab={config.prefabName}");
                return;
            }

            Transform parent = animalRoot != null ? animalRoot : transform;
            var go = Instantiate(prefab, worldPos, Quaternion.identity, parent);
            AnimalSpawned?.Invoke(animalId, config.prefabName, go);
            Debug.Log($"[Flow] Spawned remote {animalId} at {worldPos}, bundle={config.bundleName}, prefab={config.prefabName}, go={go.name}");
        });
    }

    public void SelectAnimal(string animalId)
    {
        manualSelectedAnimalId = string.IsNullOrWhiteSpace(animalId) ? "" : animalId.Trim();
        if (animalPickerUI != null)
        {
            animalPickerUI.SelectAnimal(manualSelectedAnimalId);
        }
    }

    public void SetRuntimeRemoteVariant(string animalId, string bundleName, string bundleUrl, string prefabName)
    {
        if (string.IsNullOrWhiteSpace(animalId))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return;
        }

        string normalizedId = animalId.Trim();
        runtimeRemoteConfigMap[normalizedId] = new RemoteAnimalConfig
        {
            bundleName = string.IsNullOrWhiteSpace(bundleName) ? "" : bundleName.Trim(),
            bundleUrl = string.IsNullOrWhiteSpace(bundleUrl) ? "" : bundleUrl.Trim(),
            prefabName = prefabName.Trim(),
        };
    }

    public void ClearRuntimeRemoteVariant(string animalId)
    {
        if (string.IsNullOrWhiteSpace(animalId))
        {
            return;
        }

        runtimeRemoteConfigMap.Remove(animalId.Trim());
    }

    private bool TryGetSelectedAnimalId(out string animalId)
    {
        if (animalPickerUI != null && animalPickerUI.HasSelection)
        {
            animalId = animalPickerUI.SelectedAnimalId;
            return !string.IsNullOrWhiteSpace(animalId);
        }

        animalId = manualSelectedAnimalId;
        return !string.IsNullOrWhiteSpace(animalId);
    }

    private bool TryGetRemoteConfig(string animalId, out RemoteAnimalConfig config)
    {
        if (runtimeRemoteConfigMap.TryGetValue(animalId, out config) && config != null)
        {
            return true;
        }

        return remoteConfigMap.TryGetValue(animalId, out config) && config != null;
    }

    private void OpenSelectionPanelFallback()
    {
        if (animalSelectionPanelController == null)
        {
            var panelObj = GameObject.Find("AnimalTypePanel");
            if (panelObj != null)
            {
                var component = panelObj.GetComponent("AnimalSelectionPanelController");
                if (component is MonoBehaviour monoBehaviour)
                {
                    animalSelectionPanelController = monoBehaviour;
                }
            }
        }

        if (animalSelectionPanelController != null)
        {
            animalSelectionPanelController.Invoke("OpenPanel", 0f);
        }
    }

    private bool TryGetSelectionPanelController(out AnimalSelectionPanelController controller)
    {
        controller = null;
        if (animalSelectionPanelController is AnimalSelectionPanelController typed)
        {
            controller = typed;
            return true;
        }

        if (animalSelectionPanelController == null)
        {
            var panelObj = GameObject.Find("AnimalTypePanel");
            if (panelObj != null)
            {
                animalSelectionPanelController = panelObj.GetComponent("AnimalSelectionPanelController") as MonoBehaviour;
            }
        }

        controller = animalSelectionPanelController as AnimalSelectionPanelController;
        return controller != null;
    }
}
