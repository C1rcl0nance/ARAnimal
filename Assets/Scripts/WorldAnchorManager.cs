using UnityEngine;
using System.IO;
using easyar;

public class WorldAnchorManager : MonoBehaviour
{
    [Header("EasyAR")]
    public ARSession arSession;
    public SparseSpatialMapBuilderFrameFilter builder;
    public SparseSpatialMapTrackerFrameFilter tracker;
    public Material sparsePointCloudMaterial;
    public SparseSpatialMapController mapControllerInScene;
    public bool autoCreateAnchorWhenLocalized = true;

    [Header("Anchor")]
    public Transform anchorRoot;
    public string anchorRootName = "AnchorRoot";
    public bool autoLoadOnStart = true;
    public bool autoSaveOnChange = true;

    [Header("Runtime (Read Only)")]
    [SerializeField] private bool isLocalized;
    [SerializeField] private bool isBuilding;
    [SerializeField] private bool isLocalizing;
    [SerializeField] private string currentMapName = "";
    [SerializeField] private string currentMapId = "";
    private SparseSpatialMapController mapController;
    private bool mapControllerEventsBound;

    public bool IsLocalized => isLocalized;
    public bool IsBuilding => isBuilding;
    public bool IsLocalizing => isLocalizing;
    public string CurrentMapName => currentMapName;
    public string CurrentMapId => currentMapId;
    public Transform AnchorRoot => anchorRoot;
    public string SaveFilePath => Path.Combine(Application.persistentDataPath, "WorldAnchorState.json");

    [System.Serializable]
    private class WorldAnchorState
    {
        public bool isLocalized;
        public string currentMapName;
        public string currentMapId;
        public bool hasAnchorRoot;
        public float[] anchorPosition;
        public float[] anchorRotation;
    }

    void Awake()
    {
        CacheSessionRefs();
    }

    void Start()
    {
        if (autoLoadOnStart) LoadState();
    }

    public void SetLocalized(bool value)
    {
        isLocalized = value;
        if (autoSaveOnChange) SaveState();
    }

    public void SetCurrentMapId(string mapId)
    {
        currentMapId = string.IsNullOrEmpty(mapId) ? "" : mapId;
        if (autoSaveOnChange) SaveState();
    }

    public void SetCurrentMapInfo(string mapName, string mapId)
    {
        currentMapName = string.IsNullOrEmpty(mapName) ? "" : mapName;
        currentMapId = string.IsNullOrEmpty(mapId) ? "" : mapId;
        if (autoSaveOnChange) SaveState();
    }

    public void StartBuildMode()
    {
        CacheSessionRefs();
        if (builder == null)
        {
            Debug.LogError("[WorldAnchorManager] SparseSpatialMapBuilderFrameFilter 未绑定。");
            return;
        }

        builder.enabled = true;
        if (tracker != null) tracker.enabled = false;
        isBuilding = true;
        isLocalizing = false;
        SetLocalized(false);
    }

    public void HostCurrentMap(string mapName)
    {
        CacheSessionRefs();
        if (builder == null)
        {
            Debug.LogError("[WorldAnchorManager] 无法 Host：Builder 未绑定。");
            return;
        }

        string safeName = string.IsNullOrWhiteSpace(mapName) ? ("Map_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss")) : mapName.Trim();
        isBuilding = true;
        builder.enabled = true;

        builder.Host(safeName, Optional<Image>.Empty, Optional<int>.Empty, (map, error) =>
        {
            isBuilding = false;
            if (map.OnSome)
            {
                SetCurrentMapInfo(map.Value.Name, map.Value.ID);
                Debug.Log($"[WorldAnchorManager] Map Host 成功: name={map.Value.Name}, id={map.Value.ID}");
            }
            else
            {
                var err = error.OnSome ? error.Value : "unknown";
                Debug.LogError($"[WorldAnchorManager] Map Host 失败: {err}");
            }
        });
    }

    public void LoadAndLocalizeSavedMap()
    {
        if (string.IsNullOrEmpty(currentMapId))
        {
            Debug.LogWarning("[WorldAnchorManager] 当前没有已保存的 mapId。");
            return;
        }
        LoadAndLocalizeMap(currentMapName, currentMapId);
    }

    public void LoadAndLocalizeMap(string mapName, string mapId)
    {
        CacheSessionRefs();
        if (arSession == null || tracker == null)
        {
            Debug.LogError("[WorldAnchorManager] 无法加载地图：ARSession 或 Tracker 未绑定。");
            return;
        }

        if (mapController == null && mapControllerInScene != null)
        {
            mapController = mapControllerInScene;
        }

        if (mapController == null)
        {
            if (sparsePointCloudMaterial == null)
            {
                isLocalizing = false;
                Debug.LogError("[WorldAnchorManager] 无法创建 SparseSpatialMapController：sparsePointCloudMaterial 未设置。请在 Inspector 里给 WorldAnchorManager.sparsePointCloudMaterial 赋值，或直接绑定 mapControllerInScene。");
                return;
            }

            try
            {
                mapController = ARSessionFactory
                    .CreateController<SparseSpatialMapController>(
                        new ARSessionFactory.Resources { SparseSpatialMapPointCloudMaterial = sparsePointCloudMaterial })
                    .GetComponent<SparseSpatialMapController>();
            }
            catch (System.Exception ex)
            {
                isLocalizing = false;
                Debug.LogError("[WorldAnchorManager] 创建 SparseSpatialMapController 失败: " + ex.Message);
                return;
            }
        }

        if (!mapControllerEventsBound)
        {
            mapController.TargetFound += OnTargetFound;
            mapController.TargetLost += OnTargetLost;
            mapControllerEventsBound = true;
        }

        mapController.Source = new SparseSpatialMapController.MapManagerSourceData { Name = mapName, ID = mapId };
        mapController.Tracker = tracker;
        if (mapController.PointCloudRenderer) mapController.PointCloudRenderer.Show = false;

        if (builder != null) builder.enabled = false;
        tracker.enabled = true;

        SetCurrentMapInfo(mapName, mapId);
        isLocalizing = true;
        isBuilding = false;
        SetLocalized(false);

        Debug.Log($"[WorldAnchorManager] 正在加载并定位地图: name={mapName}, id={mapId}");
    }

    public void EnsureAnchorRoot()
    {
        if (anchorRoot != null) return;

        var go = new GameObject(anchorRootName);
        anchorRoot = go.transform;
        if (autoSaveOnChange) SaveState();
    }

    public void EnsureAnchorRoot(Vector3 worldPosition, Quaternion worldRotation)
    {
        EnsureAnchorRoot();
        anchorRoot.SetPositionAndRotation(worldPosition, worldRotation);
        if (autoSaveOnChange) SaveState();
    }

    public void ClearLocalizationState()
    {
        isLocalized = false;
        isBuilding = false;
        isLocalizing = false;
        currentMapName = "";
        currentMapId = "";
        if (autoSaveOnChange) SaveState();
    }

    public void SaveState()
    {
        var state = new WorldAnchorState
        {
            isLocalized = isLocalized,
            currentMapName = currentMapName,
            currentMapId = currentMapId,
            hasAnchorRoot = anchorRoot != null,
            anchorPosition = anchorRoot == null ? null : new[] { anchorRoot.position.x, anchorRoot.position.y, anchorRoot.position.z },
            anchorRotation = anchorRoot == null ? null : new[] { anchorRoot.rotation.x, anchorRoot.rotation.y, anchorRoot.rotation.z, anchorRoot.rotation.w }
        };

        var json = JsonUtility.ToJson(state, true);
        File.WriteAllText(SaveFilePath, json);
    }

    public void LoadState()
    {
        if (!File.Exists(SaveFilePath)) return;

        var json = File.ReadAllText(SaveFilePath);
        var state = JsonUtility.FromJson<WorldAnchorState>(json);
        if (state == null) return;

        isLocalized = state.isLocalized;
        currentMapName = state.currentMapName ?? "";
        currentMapId = state.currentMapId ?? "";

        if (state.hasAnchorRoot &&
            state.anchorPosition != null && state.anchorPosition.Length == 3 &&
            state.anchorRotation != null && state.anchorRotation.Length == 4)
        {
            EnsureAnchorRoot();
            var pos = new Vector3(state.anchorPosition[0], state.anchorPosition[1], state.anchorPosition[2]);
            var rot = new Quaternion(state.anchorRotation[0], state.anchorRotation[1], state.anchorRotation[2], state.anchorRotation[3]);
            anchorRoot.SetPositionAndRotation(pos, rot);
        }
    }

    public void DeleteSavedState()
    {
        if (File.Exists(SaveFilePath))
            File.Delete(SaveFilePath);
    }

    private void CacheSessionRefs()
    {
        if (arSession == null) arSession = GetComponentInParent<ARSession>();
        if (arSession == null) arSession = FindObjectOfType<ARSession>(true);
        if (builder == null && arSession != null) builder = arSession.GetComponentInChildren<SparseSpatialMapBuilderFrameFilter>(true);
        if (tracker == null && arSession != null) tracker = arSession.GetComponentInChildren<SparseSpatialMapTrackerFrameFilter>(true);
    }

    private void OnTargetFound()
    {
        isLocalizing = false;
        SetLocalized(true);
        if (autoCreateAnchorWhenLocalized && anchorRoot == null)
            EnsureAnchorRoot(mapController.transform.position, mapController.transform.rotation);
        Debug.Log("[WorldAnchorManager] 地图已定位成功。");
    }

    private void OnTargetLost()
    {
        SetLocalized(false);
        Debug.Log("[WorldAnchorManager] 地图跟踪丢失。");
    }
}
