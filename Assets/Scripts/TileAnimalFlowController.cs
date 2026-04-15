using System.Collections.Generic;
using UnityEngine;

public class TileAnimalFlowController : MonoBehaviour
{
    [System.Serializable]
    public class AnimalPrefabEntry
    {
        public string animalId;      // 如 "cat"
        public GameObject prefab;    // 对应动物预制体
    }

    [Header("Refs")]
    public TileTapDetector tapDetector;
    public AnimalPickerUI animalPickerUI;
    public Transform animalRoot;
    public WorldAnchorManager worldAnchorManager;

    [Header("Animals")]
    public List<AnimalPrefabEntry> animalPrefabs = new List<AnimalPrefabEntry>();

    private Dictionary<string, GameObject> _map;

    void Awake()
    {
        _map = new Dictionary<string, GameObject>();
        foreach (var e in animalPrefabs)
        {
            if (e != null && !string.IsNullOrEmpty(e.animalId) && e.prefab != null)
                _map[e.animalId] = e.prefab;
        }
    }

    void Start()
    {
        if (tapDetector != null) tapDetector.OnTileTapped += OnTileTapped;
        if (animalPickerUI != null) animalPickerUI.OnAnimalSelected += OnAnimalSelected;
    }

    void OnDestroy()
    {
        if (tapDetector != null) tapDetector.OnTileTapped -= OnTileTapped;
        if (animalPickerUI != null) animalPickerUI.OnAnimalSelected -= OnAnimalSelected;
    }

    private void OnTileTapped(GameObject tile, Vector3 worldPos)
    {
        if (worldAnchorManager == null)
        {
            Debug.LogWarning("[Flow] worldAnchorManager 未绑定，已阻止放置。");
            return;
        }

        if (!worldAnchorManager.IsLocalized)
        {
            Debug.Log("[Flow] 尚未完成地图定位，暂不允许放置。");
            return;
        }

        if (animalPickerUI != null) animalPickerUI.Show(worldPos);
        Debug.Log($"[Flow] Tile tapped: {tile.name}, world={worldPos}");
    }

    private void OnAnimalSelected(string animalId, Vector3 worldPos)
    {
        if (!_map.TryGetValue(animalId, out var prefab) || prefab == null)
        {
            Debug.LogWarning($"[Flow] 未配置动物 prefab: {animalId}");
            return;
        }

        if (worldAnchorManager == null)
        {
            Debug.LogWarning("[Flow] worldAnchorManager 未绑定，已阻止放置。");
            return;
        }

        if (!worldAnchorManager.IsLocalized)
        {
            Debug.LogWarning("[Flow] 尚未完成地图定位，已阻止放置。");
            return;
        }

        Transform parent = animalRoot;
        if (worldAnchorManager.AnchorRoot == null)
            worldAnchorManager.EnsureAnchorRoot(worldPos, Quaternion.identity);
        parent = worldAnchorManager.AnchorRoot;

        Quaternion rot = Quaternion.identity;
        var go = Instantiate(prefab, worldPos, rot, parent);
        Debug.Log($"[Flow] Spawned {animalId} at {worldPos}, go={go.name}");
    }
}
