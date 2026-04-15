using System.Collections.Generic;
using UnityEngine;

public class TileGrassSpawner : MonoBehaviour
{
    [Header("Refs")]
    public AutoPlaneAreaEstimator estimator;

    [Header("Grass Prefabs")]
    public List<GameObject> grassPrefabs = new List<GameObject>();

    [Header("Spawn Count Per Tile")]
    [Range(1, 8)] public int minGrassPerTile = 1;
    [Range(1, 12)] public int maxGrassPerTile = 3;

    [Header("Placement")]
    [Tooltip("距离 tile 边缘保留的安全边距（米）")]
    [Range(0f, 0.05f)] public float tileEdgePadding = 0.005f;
    [Tooltip("同一 tile 内两株草的最小水平间距（米）")]
    [Range(0f, 0.2f)] public float minGrassSpacing = 0.04f;
    [Tooltip("每株草找可用位置的最大尝试次数")]
    [Range(1, 64)] public int maxPositionTriesPerGrass = 16;
    [Range(0f, 0.2f)] public float yOffset = 0.005f;
    [Range(0f, 360f)] public float randomYawMin = 0f;
    [Range(0f, 360f)] public float randomYawMax = 360f;

    [Header("Scale")]
    [Range(0.1f, 3f)] public float randomScaleMin = 1.2f;
    [Range(0.1f, 3f)] public float randomScaleMax = 1.8f;

    [Header("Behavior")]
    public bool spawnOnlyWhenStable = true;
    public bool clearWhenTileDisappears = false;
    public bool debugLog = false;

    private readonly Dictionary<Transform, List<GameObject>> _spawnedByTile = new Dictionary<Transform, List<GameObject>>();
    private readonly List<Transform> _activeTilesBuffer = new List<Transform>();
    private readonly List<Transform> _toRemoveBuffer = new List<Transform>();

    void Update()
    {
        if (estimator == null || estimator.tileRoot == null) return;
        if (grassPrefabs == null || grassPrefabs.Count == 0) return;
        if (spawnOnlyWhenStable && !estimator.IsStableNow) return;

        CollectActiveTiles(estimator.tileRoot, _activeTilesBuffer);

        for (int i = 0; i < _activeTilesBuffer.Count; i++)
        {
            var tile = _activeTilesBuffer[i];
            if (!_spawnedByTile.ContainsKey(tile))
            {
                SpawnGrassOnTile(tile);
            }
        }

        if (clearWhenTileDisappears)
        {
            _toRemoveBuffer.Clear();
            foreach (var kv in _spawnedByTile)
            {
                if (!_activeTilesBuffer.Contains(kv.Key))
                {
                    DestroyGrassList(kv.Value);
                    _toRemoveBuffer.Add(kv.Key);
                }
            }
            for (int i = 0; i < _toRemoveBuffer.Count; i++)
            {
                _spawnedByTile.Remove(_toRemoveBuffer[i]);
            }
        }
    }

    private void CollectActiveTiles(Transform root, List<Transform> output)
    {
        output.Clear();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            output.Add(child);
        }
    }

    private void SpawnGrassOnTile(Transform tile)
    {
        int minCount = Mathf.Max(1, minGrassPerTile);
        int maxCount = Mathf.Max(minCount, maxGrassPerTile);
        int count = Random.Range(minCount, maxCount + 1);

        var list = new List<GameObject>(count);
        var acceptedPositions = new List<Vector2>(count);
        float halfX = Mathf.Max(0.001f, tile.localScale.x * 0.5f - tileEdgePadding);
        float halfZ = Mathf.Max(0.001f, tile.localScale.z * 0.5f - tileEdgePadding);
        float baseY = estimator.tileThickness + yOffset;

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = grassPrefabs[Random.Range(0, grassPrefabs.Count)];
            if (prefab == null) continue;

            if (!TryPickPosition(acceptedPositions, halfX, halfZ, minGrassSpacing, maxPositionTriesPerGrass, out Vector2 pos2D))
                continue;

            var grass = Instantiate(prefab, tile);

            Vector3 localPos = new Vector3(
                pos2D.x,
                baseY,
                pos2D.y
            );
            grass.transform.localPosition = localPos;

            float yaw = Random.Range(randomYawMin, randomYawMax);
            grass.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float scale = Random.Range(
                Mathf.Min(randomScaleMin, randomScaleMax),
                Mathf.Max(randomScaleMin, randomScaleMax)
            );
            grass.transform.localScale = Vector3.one * scale;

            list.Add(grass);
            acceptedPositions.Add(pos2D);
        }

        _spawnedByTile[tile] = list;
        if (debugLog) Debug.Log($"[TileGrassSpawner] Spawned {list.Count} grasses on {tile.name}");
    }

    public void ClearAllGrass()
    {
        foreach (var kv in _spawnedByTile) DestroyGrassList(kv.Value);
        _spawnedByTile.Clear();
    }

    private void DestroyGrassList(List<GameObject> list)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null) Destroy(list[i]);
        }
        list.Clear();
    }

    private bool TryPickPosition(List<Vector2> accepted, float halfX, float halfZ, float minSpacing, int maxTries, out Vector2 result)
    {
        float minSpacingSq = minSpacing * minSpacing;
        for (int t = 0; t < Mathf.Max(1, maxTries); t++)
        {
            Vector2 p = new Vector2(
                Random.Range(-halfX, halfX),
                Random.Range(-halfZ, halfZ)
            );

            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
            {
                if ((p - accepted[i]).sqrMagnitude < minSpacingSq)
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
            {
                result = p;
                return true;
            }
        }

        result = Vector2.zero;
        return false;
    }
}
