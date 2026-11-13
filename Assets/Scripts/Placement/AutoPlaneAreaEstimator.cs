using easyar;
using System.Collections.Generic;
using UnityEngine;

public class AutoPlaneAreaEstimator : MonoBehaviour
{
    [Header("EasyAR (must assign manually)")]
    public MotionTrackerFrameSource motionTrackerFrameSource;

    [Header("Sampling")]
    [Range(1, 9)] public int sampleGrid = 7;
    [Range(0f, 0.45f)] public float samplePadding = 0.10f;
    [Range(0.02f, 0.6f)] public float clusterRadius = 0.28f;
    [Range(0.005f, 0.08f)] public float dedupDistance = 0.02f;

    [Header("Stability")]
    public float warmupSeconds = 1.5f;
    public int maxMissFrames = 20;
    public int minValidStreakToUpdate = 5;
    [Range(3, 120)] public int stableFrames = 12;
    [Range(0.01f, 0.6f)] public float stableFluctuation = 0.30f;

    [Header("EMA Smoothing")]
    [Range(0.01f, 1f)] public float emaAlpha = 0.25f;

    [Header("Grid Measure")]
    [Range(0.05f, 0.5f)] public float cellSize = 0.10f;
    [Range(0f, 0.2f)] public float shrinkMargin = 0.01f;

    [Header("Runtime Grid Render")]
    public GameObject tilePrefab;
    [Range(0.001f, 0.03f)] public float tileThickness = 0.001f;
    [Range(0f, 0.03f)] public float tileLift = 0.001f;
    [Range(0.90f, 1.0f)] public float tileFillScale = 0.99f;
    [Range(0.90f, 1.0f)] public float boundaryTileFillScale = 0.97f;
    public bool renderAsCombinedGrid = true;
    public Transform tileRoot;

    [Header("Per-Tile Height")]
    public bool enablePerTileHeight = true;
    [Range(0.01f, 1f)] public float tileHeightAlpha = 0.35f;
    [Range(0.001f, 0.2f)] public float tileHeightDecay = 0.02f;
    public bool flattenRenderHeight = true;

    [Header("Plane Grouping")]
    public bool enableMultiPlaneGrouping = true;
    [Range(0.005f, 0.2f)] public float planeHeightThreshold = 0.04f;
    [Range(1, 50)] public int minTilesPerPlane = 3;
    public bool useEightNeighborForGrouping = false;
    [Range(1.0f, 4.0f)] public float outlierHeightMultiplier = 2.2f;
    [Range(0, 4)] public int minSameGroupNeighbors = 0;
    public bool keepPlacedTiles = true;
    public bool preferGroundLikePlane = true;
    [Range(0.01f, 0.2f)] public float groundPlaneSelectionWindow = 0.08f;
    [Range(0.005f, 0.15f)] public float maxTileHeightAbovePlane = 0.035f;
    [Range(0.0f, 0.5f)] public float groundReferencePercentile = 0.2f;

    [Header("Plane Outline")]
    public bool highlightPlaneBoundary = true;
    public bool colorBoundaryTiles = true;
    public Color interiorColor = new Color(1f, 1f, 1f, 0.36f);
    public Color boundaryColor = new Color(0.13f, 0.13f, 0.13f, 0.95f);
    [Range(1, 4)] public int boundaryRings = 1;
    [Range(0f, 0.02f)] public float boundaryExtraLift = 0.0015f;
    public bool applyPhotoLikeTileStyleOnStart = true;

    [Header("Edge Fade")]
    public bool enableEdgeFade = true;
    [Range(1, 12)] public int edgeFadeRings = 4;
    [Range(0f, 1f)] public float edgeAlpha = 0.05f;

    [Header("Bounding Box")]
    public bool showLargestPlaneBoundingBox = true;
    public Color boundingBoxColor = new Color(1f, 0.82f, 0.2f, 0.95f);
    [Range(0.001f, 0.05f)] public float boundingBoxLineWidth = 0.008f;
    [Range(0f, 0.05f)] public float boundingBoxLift = 0.004f;

    [Header("Lock + Expand")]
    public bool lockWhenStable = true;
    public int stableLockFrames = 10;
    public int maxAccumulatedPoints = 600;
    public float lockedDedup2D = 0.03f;
    [Range(0.02f, 1.5f)] public float maxLockedExpandStep = 0.35f;
    [Range(0.005f, 0.2f)] public float maxLockedPlaneOffset = 0.04f;

    [Header("Behavior")]
    public bool debugLog = false;

    private float _startTime;
    private int _missFrames = 0;
    private int _validStreak = 0;
    private int _stableStreak = 0;

    private readonly Queue<float> _areaHistory = new Queue<float>();
    private bool _emaInitialized = false;
    private float _emaArea = 0f;

    private int _tileCount;
    private float _area;
    private float _areaSmoothed;
    private bool _stable;

    private Vector3 _origin;
    private Vector3 _axisX;
    private Vector3 _axisY;
    private Vector3 _normal;

    private bool _isLocked = false;
    private Vector3 _lockedOrigin;
    private Vector3 _lockedAxisX;
    private Vector3 _lockedAxisY;
    private Vector3 _lockedNormal;

    private List<Vector3> _allHits = new List<Vector3>();
    private List<Vector3> _mainCluster = new List<Vector3>();
    private List<Vector2> _poly2D = new List<Vector2>();
    private List<Vector2Int> _tileIndices = new List<Vector2Int>();
    private List<Vector2Int> _lastGoodTiles = new List<Vector2Int>();
    private readonly List<Vector2Int> _renderTiles = new List<Vector2Int>();

    private readonly List<Vector2> _lockedAccum2D = new List<Vector2>();

    private readonly List<GameObject> _tilePool = new List<GameObject>();
    private readonly Dictionary<Vector2Int, float> _tileHeights = new Dictionary<Vector2Int, float>();
    private readonly Dictionary<Vector2Int, float> _tileHeightsTarget = new Dictionary<Vector2Int, float>();

    private readonly Dictionary<Vector2Int, int> _tilePlaneGroup = new Dictionary<Vector2Int, int>();
    private readonly HashSet<Vector2Int> _boundaryTiles = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, int> _tileBoundaryDistance = new Dictionary<Vector2Int, int>();
    private readonly HashSet<Vector2Int> _stickyLargestPlaneTiles = new HashSet<Vector2Int>();
    private readonly Dictionary<GameObject, MeshRenderer[]> _tileRenderers = new Dictionary<GameObject, MeshRenderer[]>();
    private MaterialPropertyBlock _tileMpb;
    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");

    private GameObject _combinedGridObject;
    private MeshFilter _combinedMeshFilter;
    private MeshRenderer _combinedMeshRenderer;
    private MeshCollider _combinedMeshCollider;
    private Mesh _combinedMesh;
    private GameObject _boundingBoxObject;
    private LineRenderer _boundingBoxLine;
    private Material _boundingBoxMaterial;

    public float CurrentArea => _areaSmoothed;
    public int CurrentTileCount => _renderTiles.Count;
    public float CurrentPlaneLength { get; private set; }
    public float CurrentPlaneWidth { get; private set; }
    public bool IsStableNow => _stable;
    public bool IsLocked => _isLocked;

    private Vector3 RenderOrigin => _isLocked ? _lockedOrigin : _origin;
    private Vector3 RenderAxisX => _isLocked ? _lockedAxisX : _axisX;
    private Vector3 RenderAxisY => _isLocked ? _lockedAxisY : _axisY;
    private Vector3 RenderNormal => _isLocked ? _lockedNormal : _normal;

    private void Awake()
    {
        _tileMpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        _startTime = Time.time;
        if (applyPhotoLikeTileStyleOnStart)
        {
            // Force a photo-like default tile style (continuous light tiles + dark border).
            tileThickness = 0.001f;
            tileLift = 0.0008f;
            tileFillScale = 0.99f;
            boundaryTileFillScale = 0.97f;
            interiorColor = new Color(1f, 1f, 1f, 0.36f);
            boundaryColor = new Color(0.13f, 0.13f, 0.13f, 0.95f);
            boundaryRings = 2;
            boundaryExtraLift = 0.0007f;
            highlightPlaneBoundary = true;
            colorBoundaryTiles = true;
            enableEdgeFade = true;
            edgeFadeRings = 4;
            edgeAlpha = 0.05f;
        }

        if (motionTrackerFrameSource == null)
        {
            Debug.LogError("[AutoPlaneAreaEstimator] motionTrackerFrameSource is not assigned.");
            enabled = false;
            return;
        }

        if (tileRoot == null)
        {
            Debug.LogError("[AutoPlaneAreaEstimator] tileRoot is not assigned.");
            enabled = false;
            return;
        }

        tileRoot.localScale = Vector3.one;

        if (tilePrefab == null)
        {
            Debug.LogError("[AutoPlaneAreaEstimator] tilePrefab is not assigned.");
            enabled = false;
            return;
        }

        if (renderAsCombinedGrid)
        {
            EnsureCombinedGridObject();
        }
    }

    private void OnDestroy()
    {
        if (_boundingBoxMaterial != null)
        {
            Destroy(_boundingBoxMaterial);
            _boundingBoxMaterial = null;
        }
    }

    private void Update()
    {
        if (Time.time - _startTime < warmupSeconds)
        {
            _stable = false;
            RenderCurrentGrid(_renderTiles);
            return;
        }

        _allHits = DedupPoints(SampleHitsAuto(), dedupDistance);
        if (_allHits.Count < 3) { HandleInvalidFrame("hits<3"); return; }

        _mainCluster = PickLargestCluster(_allHits, clusterRadius);
        if (_mainCluster.Count < 3) { HandleInvalidFrame("cluster<3"); return; }

        _missFrames = 0;
        _validStreak++;

        FitHorizontalFrame(_mainCluster, out _origin, out _axisX, out _axisY, out _normal);

        if (!_isLocked)
        {
            var pts2 = ProjectTo2D(_mainCluster, _origin, _axisX, _axisY);
            var hull = ConvexHull2D(pts2);
            if (hull.Count < 3) { HandleInvalidFrame("hull<3"); return; }

            _poly2D = ApplyShrinkSafe(hull, shrinkMargin);
            _tileCount = CountTiles(_poly2D, cellSize, out _tileIndices);
            _area = _tileCount * cellSize * cellSize;

            UpdateStability();

            if (_stable) _stableStreak++;
            else _stableStreak = 0;

            if (lockWhenStable && _stableStreak >= stableLockFrames)
            {
                LockBasis();
                SeedLockedAccumWithCurrentCluster();
            }
        }
        else
        {
            ExpandLockedAccumFromCurrentHits();

            if (_lockedAccum2D.Count >= 3)
            {
                var hullLocked = ConvexHull2D(new List<Vector2>(_lockedAccum2D));
                _poly2D = ApplyShrinkSafe(hullLocked, shrinkMargin);
                _tileCount = CountTiles(_poly2D, cellSize, out _tileIndices);
                _area = _tileCount * cellSize * cellSize;
            }
            else
            {
                _tileCount = 0;
                _tileIndices.Clear();
                _area = 0f;
            }

            UpdateStability();
        }

        if (_validStreak >= minValidStreakToUpdate)
        {
            _lastGoodTiles = new List<Vector2Int>(_tileIndices);
        }

        UpdatePerTileHeights(_lastGoodTiles);
        BuildPlaneGroups(_lastGoodTiles);
        // Size metrics should not depend only on render cache state.
        // When render tiles are temporarily empty, fall back to current frame tiles
        // so UI length/width won't be stuck at 0.
        var sizeMetricTiles = (_renderTiles != null && _renderTiles.Count > 0) ? _renderTiles : _tileIndices;
        UpdatePlaneSizeMetrics(sizeMetricTiles);
        RenderCurrentGrid(_renderTiles);

        if (debugLog)
        {
            Debug.Log($"[AutoPlane] lock={_isLocked}, hits={_allHits.Count}, cluster={_mainCluster.Count}, groups={GetPlaneGroupCount()}, tiles={_renderTiles.Count}, area={_areaSmoothed:F3}");
        }
    }

    private int GetPlaneGroupCount()
    {
        var groupIds = new HashSet<int>();
        foreach (var kv in _tilePlaneGroup)
        {
            groupIds.Add(kv.Value);
        }
        return groupIds.Count;
    }

    private void LockBasis()
    {
        _isLocked = true;
        _lockedOrigin = _origin;
        _lockedAxisX = _axisX;
        _lockedAxisY = _axisY;
        _lockedNormal = _normal;
    }

    private void SeedLockedAccumWithCurrentCluster()
    {
        _lockedAccum2D.Clear();
        var pts = ProjectTo2D(_mainCluster, _lockedOrigin, _lockedAxisX, _lockedAxisY);
        AddUnique2D(_lockedAccum2D, pts, lockedDedup2D, maxAccumulatedPoints);
    }

    private void ExpandLockedAccumFromCurrentHits()
    {
        var filteredWorld = FilterHitsNearLockedPlane(_allHits, _lockedOrigin, _lockedNormal, maxLockedPlaneOffset);
        var pts = ProjectTo2D(filteredWorld, _lockedOrigin, _lockedAxisX, _lockedAxisY);

        if (_lockedAccum2D.Count >= 3 && pts.Count > 0)
        {
            var hull = ConvexHull2D(new List<Vector2>(_lockedAccum2D));
            var nearHull = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (IsInsideOrNearPolygon(pts[i], hull, maxLockedExpandStep))
                {
                    nearHull.Add(pts[i]);
                }
            }
            pts = nearHull;
        }

        AddUnique2DWithGrowthLimit(_lockedAccum2D, pts, lockedDedup2D, maxAccumulatedPoints, maxLockedExpandStep);
    }

    private void UpdateStability()
    {
        if (!_emaInitialized) { _emaArea = _area; _emaInitialized = true; }
        else _emaArea = Mathf.Lerp(_emaArea, _area, emaAlpha);

        _areaSmoothed = _emaArea;
        PushArea(_areaSmoothed);
        _stable = IsStable(_areaHistory, stableFluctuation, stableFrames);
    }

    private List<Vector2> ApplyShrinkSafe(List<Vector2> hull, float margin)
    {
        var safe = InsetPolygonTowardCentroid(hull, margin);
        return (safe != null && safe.Count >= 3) ? safe : hull;
    }

    private void HandleInvalidFrame(string reason)
    {
        _missFrames++;
        _validStreak = 0;
        _stable = false;
        _stableStreak = 0;

        RenderCurrentGrid(_renderTiles);

        if (_missFrames > maxMissFrames)
        {
            _areaHistory.Clear();
            _emaInitialized = false;
            _emaArea = 0f;
            _areaSmoothed = 0f;
            _area = 0f;
            _tileCount = 0;

            _lastGoodTiles.Clear();
            _renderTiles.Clear();
            _tileIndices.Clear();
            _tileHeights.Clear();
            _tileHeightsTarget.Clear();
            _tilePlaneGroup.Clear();
            _boundaryTiles.Clear();
            _tileBoundaryDistance.Clear();
            if (!keepPlacedTiles)
            {
                _stickyLargestPlaneTiles.Clear();
            }

            if (keepPlacedTiles && _stickyLargestPlaneTiles.Count > 0)
            {
                _renderTiles.AddRange(_stickyLargestPlaneTiles);
            }

            UpdatePlaneSizeMetrics(_renderTiles);
            RenderCurrentGrid(_renderTiles);
            _missFrames = 0;
        }

        if (debugLog) Debug.Log($"[AutoPlane] invalid: {reason}, miss={_missFrames}/{maxMissFrames}");
    }

    private void UpdatePlaneSizeMetrics(List<Vector2Int> tiles)
    {
        if (tiles == null || tiles.Count == 0)
        {
            CurrentPlaneLength = 0f;
            CurrentPlaneWidth = 0f;
            return;
        }

        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.x < minX) minX = tile.x;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.y > maxY) maxY = tile.y;
        }

        CurrentPlaneLength = (maxX - minX + 1) * cellSize;
        CurrentPlaneWidth = (maxY - minY + 1) * cellSize;
    }

    private List<Vector3> SampleHitsAuto()
    {
        var all = new List<Vector3>();

        var center = motionTrackerFrameSource.HitTestAgainstHorizontalPlane(new Vector2(0.5f, 0.5f));
        if (center != null && center.Count > 0) all.Add(center[0]);

        int n = Mathf.Max(1, sampleGrid);
        for (int iy = 0; iy < n; iy++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                float tx = (n == 1) ? 0.5f : ix / (float)(n - 1);
                float ty = (n == 1) ? 0.5f : iy / (float)(n - 1);
                float u = Mathf.Lerp(samplePadding, 1f - samplePadding, tx);
                float v = Mathf.Lerp(samplePadding, 1f - samplePadding, ty);

                var ret = motionTrackerFrameSource.HitTestAgainstHorizontalPlane(new Vector2(u, v));
                if (ret != null && ret.Count > 0) all.Add(ret[0]);
            }
        }

        return all;
    }

    private void BuildPlaneGroups(List<Vector2Int> visibleTiles)
    {
        _tilePlaneGroup.Clear();
        _boundaryTiles.Clear();
        _tileBoundaryDistance.Clear();
        _renderTiles.Clear();

        if (visibleTiles == null || visibleTiles.Count == 0)
        {
            return;
        }

        if (!enableMultiPlaneGrouping)
        {
            _renderTiles.AddRange(visibleTiles);
            for (int i = 0; i < visibleTiles.Count; i++)
            {
                _tilePlaneGroup[visibleTiles[i]] = 0;
            }
            BuildBoundaryForGroups();
            BuildBoundaryDistanceMap();
            return;
        }

        var visibleSet = new HashSet<Vector2Int>(visibleTiles);
        var unvisited = new HashSet<Vector2Int>(visibleTiles);
        var rawCandidates = new List<List<Vector2Int>>();
        var filteredCandidates = new List<List<Vector2Int>>();

        while (unvisited.Count > 0)
        {
            Vector2Int seed = default;
            foreach (var tile in unvisited)
            {
                seed = tile;
                break;
            }

            var rawGroup = GrowGroup(seed, visibleSet, unvisited);
            if (rawGroup.Count < minTilesPerPlane)
            {
                continue;
            }

            rawCandidates.Add(rawGroup);

            var filteredGroup = FilterGroupOutliers(rawGroup);
            if (filteredGroup.Count >= minTilesPerPlane)
            {
                filteredCandidates.Add(filteredGroup);
            }
        }

        List<Vector2Int> selectedLargestGroup = SelectPrimaryGroup(filteredCandidates.Count > 0 ? filteredCandidates : rawCandidates);
        if (selectedLargestGroup != null && selectedLargestGroup.Count > 0)
        {
            selectedLargestGroup = PruneTilesAbovePlane(selectedLargestGroup);
        }

        if (selectedLargestGroup == null || selectedLargestGroup.Count == 0)
        {
            // Do not fallback to all visible tiles, otherwise elevated objects can leak in.
            if (keepPlacedTiles && _stickyLargestPlaneTiles.Count > 0)
            {
                _renderTiles.AddRange(_stickyLargestPlaneTiles);
            }
            else
            {
                _renderTiles.AddRange(_lastGoodTiles);
            }
            for (int i = 0; i < _renderTiles.Count; i++)
            {
                _tilePlaneGroup[_renderTiles[i]] = 0;
            }
        }
        else
        {
            _renderTiles.AddRange(selectedLargestGroup);
            for (int i = 0; i < _renderTiles.Count; i++)
            {
                _tilePlaneGroup[_renderTiles[i]] = 0;
            }
        }

        // Keep already-placed tiles after lock, so they do not disappear due temporary scan loss.
        if (keepPlacedTiles && _isLocked)
        {
            _stickyLargestPlaneTiles.UnionWith(_renderTiles);
            _renderTiles.Clear();
            _renderTiles.AddRange(_stickyLargestPlaneTiles);
            _tilePlaneGroup.Clear();
            for (int i = 0; i < _renderTiles.Count; i++)
            {
                _tilePlaneGroup[_renderTiles[i]] = 0;
            }
        }

        BuildBoundaryForGroups();
        BuildBoundaryDistanceMap();
    }

    private List<Vector2Int> GrowGroup(Vector2Int seed, HashSet<Vector2Int> visibleSet, HashSet<Vector2Int> unvisited)
    {
        var result = new List<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(seed);
        unvisited.Remove(seed);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            result.Add(current);
            float currentHeight = GetTileHeight(current);

            var neighbors = GetNeighborTiles(current);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector2Int next = neighbors[i];
                if (!visibleSet.Contains(next) || !unvisited.Contains(next))
                {
                    continue;
                }

                float nextHeight = GetTileHeight(next);
                if (Mathf.Abs(currentHeight - nextHeight) > planeHeightThreshold)
                {
                    continue;
                }

                unvisited.Remove(next);
                queue.Enqueue(next);
            }
        }

        return result;
    }

    private List<Vector2Int> GetNeighborTiles(Vector2Int id)
    {
        var neighbors = new List<Vector2Int>(useEightNeighborForGrouping ? 8 : 4)
        {
            new Vector2Int(id.x + 1, id.y),
            new Vector2Int(id.x - 1, id.y),
            new Vector2Int(id.x, id.y + 1),
            new Vector2Int(id.x, id.y - 1)
        };

        if (useEightNeighborForGrouping)
        {
            neighbors.Add(new Vector2Int(id.x + 1, id.y + 1));
            neighbors.Add(new Vector2Int(id.x + 1, id.y - 1));
            neighbors.Add(new Vector2Int(id.x - 1, id.y + 1));
            neighbors.Add(new Vector2Int(id.x - 1, id.y - 1));
        }

        return neighbors;
    }

    private static List<Vector2Int> GetFourNeighbors(Vector2Int id)
    {
        return new List<Vector2Int>(4)
        {
            new Vector2Int(id.x + 1, id.y),
            new Vector2Int(id.x - 1, id.y),
            new Vector2Int(id.x, id.y + 1),
            new Vector2Int(id.x, id.y - 1)
        };
    }

    private void BuildBoundaryForGroups()
    {
        _boundaryTiles.Clear();
        var ring = new HashSet<Vector2Int>();

        foreach (var kv in _tilePlaneGroup)
        {
            Vector2Int tile = kv.Key;
            int group = kv.Value;
            bool boundary = false;

            var neighbors = GetNeighborTiles(tile);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector2Int n = neighbors[i];
                if (!_tilePlaneGroup.TryGetValue(n, out int ng) || ng != group)
                {
                    boundary = true;
                    break;
                }
            }

            if (boundary)
            {
                ring.Add(tile);
            }
        }

        _boundaryTiles.UnionWith(ring);

        int ringsToGrow = Mathf.Max(1, boundaryRings) - 1;
        for (int r = 0; r < ringsToGrow; r++)
        {
            var nextRing = new HashSet<Vector2Int>();
            foreach (var tile in ring)
            {
                if (!_tilePlaneGroup.TryGetValue(tile, out int group))
                {
                    continue;
                }

                var neighbors = GetFourNeighbors(tile);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    Vector2Int n = neighbors[i];
                    if (_tilePlaneGroup.TryGetValue(n, out int ng) && ng == group && !_boundaryTiles.Contains(n))
                    {
                        nextRing.Add(n);
                    }
                }
            }

            if (nextRing.Count == 0)
            {
                break;
            }

            _boundaryTiles.UnionWith(nextRing);
            ring = nextRing;
        }
    }

    private void BuildBoundaryDistanceMap()
    {
        _tileBoundaryDistance.Clear();
        if (_tilePlaneGroup.Count == 0)
        {
            return;
        }

        var queue = new Queue<Vector2Int>();
        foreach (var tile in _boundaryTiles)
        {
            _tileBoundaryDistance[tile] = 0;
            queue.Enqueue(tile);
        }

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int currentDistance = _tileBoundaryDistance[current];
            if (!_tilePlaneGroup.TryGetValue(current, out int group))
            {
                continue;
            }

            var neighbors = GetFourNeighbors(current);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Vector2Int next = neighbors[i];
                if (!_tilePlaneGroup.TryGetValue(next, out int nextGroup) || nextGroup != group)
                {
                    continue;
                }

                int nextDistance = currentDistance + 1;
                if (_tileBoundaryDistance.TryGetValue(next, out int oldDistance) && oldDistance <= nextDistance)
                {
                    continue;
                }

                _tileBoundaryDistance[next] = nextDistance;
                queue.Enqueue(next);
            }
        }
    }

    private float GetTileHeight(Vector2Int id)
    {
        return _tileHeights.TryGetValue(id, out float h) ? h : 0f;
    }

    private List<Vector2Int> FilterGroupOutliers(List<Vector2Int> group)
    {
        if (group == null || group.Count == 0)
        {
            return new List<Vector2Int>();
        }

        float sum = 0f;
        for (int i = 0; i < group.Count; i++)
        {
            sum += GetTileHeight(group[i]);
        }
        float mean = sum / Mathf.Max(1, group.Count);
        float threshold = planeHeightThreshold * Mathf.Max(1f, outlierHeightMultiplier);

        var kept = new List<Vector2Int>(group.Count);
        for (int i = 0; i < group.Count; i++)
        {
            Vector2Int id = group[i];
            if (Mathf.Abs(GetTileHeight(id) - mean) <= threshold)
            {
                kept.Add(id);
            }
        }

        if (minSameGroupNeighbors <= 0 || kept.Count == 0)
        {
            return kept;
        }

        var keptSet = new HashSet<Vector2Int>(kept);
        var dense = new List<Vector2Int>(kept.Count);
        for (int i = 0; i < kept.Count; i++)
        {
            Vector2Int id = kept[i];
            int neighborCount = 0;
            var neighbors = GetFourNeighbors(id);
            for (int n = 0; n < neighbors.Count; n++)
            {
                if (keptSet.Contains(neighbors[n]))
                {
                    neighborCount++;
                }
            }

            if (neighborCount >= minSameGroupNeighbors)
            {
                dense.Add(id);
            }
        }

        return dense;
    }

    private List<Vector2Int> SelectPrimaryGroup(List<List<Vector2Int>> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (!preferGroundLikePlane)
        {
            List<Vector2Int> largest = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (largest == null || c.Count > largest.Count)
                {
                    largest = c;
                }
            }
            return largest;
        }

        float minHeight = float.MaxValue;
        var means = new float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            float m = MeanHeight(candidates[i]);
            means[i] = m;
            if (m < minHeight) minHeight = m;
        }

        List<Vector2Int> best = null;
        int bestCount = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (means[i] > minHeight + groundPlaneSelectionWindow)
            {
                continue;
            }

            int count = candidates[i].Count;
            if (count > bestCount)
            {
                bestCount = count;
                best = candidates[i];
            }
        }

        if (best != null)
        {
            return best;
        }

        // Fallback: all groups outside window, use largest.
        for (int i = 0; i < candidates.Count; i++)
        {
            if (best == null || candidates[i].Count > best.Count)
            {
                best = candidates[i];
            }
        }
        return best;
    }

    private float MeanHeight(List<Vector2Int> group)
    {
        if (group == null || group.Count == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < group.Count; i++)
        {
            sum += GetTileHeight(group[i]);
        }
        return sum / Mathf.Max(1, group.Count);
    }

    private List<Vector2Int> PruneTilesAbovePlane(List<Vector2Int> group)
    {
        if (group == null || group.Count == 0)
        {
            return group;
        }

        float baseHeight = GetReferencePlaneHeight(group);
        var kept = new List<Vector2Int>(group.Count);
        for (int i = 0; i < group.Count; i++)
        {
            float h = GetTileHeight(group[i]);
            if (h <= baseHeight + maxTileHeightAbovePlane)
            {
                kept.Add(group[i]);
            }
        }

        return kept.Count >= minTilesPerPlane ? kept : group;
    }

    private float GetReferencePlaneHeight(List<Vector2Int> group)
    {
        if (group == null || group.Count == 0)
        {
            return 0f;
        }

        var heights = new List<float>(group.Count);
        for (int i = 0; i < group.Count; i++)
        {
            heights.Add(GetTileHeight(group[i]));
        }
        heights.Sort();

        float p = Mathf.Clamp01(groundReferencePercentile);
        int idx = Mathf.Clamp(Mathf.FloorToInt((heights.Count - 1) * p), 0, heights.Count - 1);
        return heights[idx];
    }

    private void RenderTiles(List<Vector2Int> indices)
    {
        EnsurePool(indices.Count);

        for (int i = 0; i < _tilePool.Count; i++)
        {
            bool active = i < indices.Count;
            _tilePool[i].SetActive(active);
            if (!active) continue;

            Vector2Int id = indices[i];
            Vector2 local = new Vector2((id.x + 0.5f) * cellSize, (id.y + 0.5f) * cellSize);
            bool isBoundary = highlightPlaneBoundary && _boundaryTiles.Contains(id);

            float heightOffset = 0f;
            if (enablePerTileHeight && !flattenRenderHeight && _tileHeights.TryGetValue(id, out float h))
            {
                heightOffset = h;
            }

            float extraLift = isBoundary ? boundaryExtraLift : 0f;
            Vector3 center = ToWorld(local) + RenderNormal * (heightOffset + tileThickness * 0.5f + tileLift + extraLift);

            var go = _tilePool[i];
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(RenderAxisY, RenderNormal);
            float fillScale = isBoundary ? boundaryTileFillScale : tileFillScale;
            fillScale = Mathf.Clamp(fillScale, 0.90f, 1.0f);
            go.transform.localScale = new Vector3(cellSize * fillScale, tileThickness, cellSize * fillScale);
            ApplyTileVisual(go, id, isBoundary);
        }
    }

    private void RenderCurrentGrid(List<Vector2Int> indices)
    {
        if (renderAsCombinedGrid)
        {
            RenderCombinedGrid(indices);
        }
        else
        {
            if (_combinedGridObject != null)
            {
                _combinedGridObject.SetActive(false);
            }
            RenderTiles(indices);
        }

        UpdateBoundingBoxVisual(indices);
    }

    private void EnsureBoundingBoxObject()
    {
        if (_boundingBoxObject != null && _boundingBoxLine != null)
        {
            return;
        }

        _boundingBoxObject = new GameObject("LargestPlaneBoundingBox");
        if (tileRoot != null)
        {
            _boundingBoxObject.transform.SetParent(tileRoot, false);
        }

        _boundingBoxLine = _boundingBoxObject.AddComponent<LineRenderer>();
        _boundingBoxLine.loop = false;
        _boundingBoxLine.positionCount = 5;
        _boundingBoxLine.useWorldSpace = true;
        _boundingBoxLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _boundingBoxLine.receiveShadows = false;
        _boundingBoxLine.numCornerVertices = 0;
        _boundingBoxLine.numCapVertices = 0;
        _boundingBoxLine.textureMode = LineTextureMode.Stretch;
        _boundingBoxLine.alignment = LineAlignment.View;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader != null)
        {
            _boundingBoxMaterial = new Material(shader);
            _boundingBoxLine.sharedMaterial = _boundingBoxMaterial;
        }
    }

    private void UpdateBoundingBoxVisual(List<Vector2Int> indices)
    {
        if (!showLargestPlaneBoundingBox || indices == null || indices.Count == 0)
        {
            if (_boundingBoxObject != null)
            {
                _boundingBoxObject.SetActive(false);
            }
            return;
        }

        EnsureBoundingBoxObject();
        if (_boundingBoxObject == null || _boundingBoxLine == null)
        {
            return;
        }

        _boundingBoxObject.SetActive(true);

        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;
        for (int i = 0; i < indices.Count; i++)
        {
            var tile = indices[i];
            if (tile.x < minX) minX = tile.x;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.y > maxY) maxY = tile.y;
        }

        float xMin = minX * cellSize;
        float xMax = (maxX + 1) * cellSize;
        float yMin = minY * cellSize;
        float yMax = (maxY + 1) * cellSize;

        float lift = tileLift + boundaryExtraLift + boundingBoxLift;
        Vector3 up = RenderNormal * lift;
        Vector3 p0 = ToWorld(new Vector2(xMin, yMin)) + up;
        Vector3 p1 = ToWorld(new Vector2(xMax, yMin)) + up;
        Vector3 p2 = ToWorld(new Vector2(xMax, yMax)) + up;
        Vector3 p3 = ToWorld(new Vector2(xMin, yMax)) + up;

        _boundingBoxLine.startWidth = boundingBoxLineWidth;
        _boundingBoxLine.endWidth = boundingBoxLineWidth;
        _boundingBoxLine.startColor = boundingBoxColor;
        _boundingBoxLine.endColor = boundingBoxColor;
        _boundingBoxLine.SetPosition(0, p0);
        _boundingBoxLine.SetPosition(1, p1);
        _boundingBoxLine.SetPosition(2, p2);
        _boundingBoxLine.SetPosition(3, p3);
        _boundingBoxLine.SetPosition(4, p0);

        if (_boundingBoxMaterial != null)
        {
            if (_boundingBoxMaterial.HasProperty(BaseColorPropId))
            {
                _boundingBoxMaterial.SetColor(BaseColorPropId, boundingBoxColor);
            }
            if (_boundingBoxMaterial.HasProperty(ColorPropId))
            {
                _boundingBoxMaterial.SetColor(ColorPropId, boundingBoxColor);
            }
        }
    }

    private void EnsureCombinedGridObject()
    {
        if (_combinedGridObject != null)
        {
            return;
        }

        _combinedGridObject = new GameObject("CombinedGridOverlay");
        if (tileRoot != null)
        {
            _combinedGridObject.transform.SetParent(tileRoot, false);
        }
        _combinedGridObject.tag = tilePrefab != null ? tilePrefab.tag : "Untagged";
        _combinedGridObject.layer = tilePrefab != null ? tilePrefab.layer : 0;

        _combinedMeshFilter = _combinedGridObject.AddComponent<MeshFilter>();
        _combinedMeshRenderer = _combinedGridObject.AddComponent<MeshRenderer>();
        _combinedMeshCollider = _combinedGridObject.AddComponent<MeshCollider>();
        _combinedMesh = new Mesh();
        _combinedMesh.name = "CombinedGridOverlayMesh";
        _combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _combinedMesh.MarkDynamic();
        _combinedMeshFilter.sharedMesh = _combinedMesh;
        _combinedMeshCollider.sharedMesh = _combinedMesh;

        MeshRenderer sourceRenderer = tilePrefab.GetComponentInChildren<MeshRenderer>(true);
        if (sourceRenderer != null && sourceRenderer.sharedMaterial != null)
        {
            _combinedMeshRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        }
        else
        {
            // Fallback material to avoid invisible mesh when prefab has no valid material.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                if (mat.HasProperty(BaseColorPropId)) mat.SetColor(BaseColorPropId, interiorColor);
                if (mat.HasProperty(ColorPropId)) mat.SetColor(ColorPropId, interiorColor);
                _combinedMeshRenderer.sharedMaterial = mat;
            }
        }

        _combinedMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _combinedMeshRenderer.receiveShadows = false;
    }

    private void RenderCombinedGrid(List<Vector2Int> indices)
    {
        EnsureCombinedGridObject();
        _combinedGridObject.SetActive(true);

        for (int i = 0; i < _tilePool.Count; i++)
        {
            _tilePool[i].SetActive(false);
        }

        if (indices == null || indices.Count == 0)
        {
            _combinedMesh.Clear();
            return;
        }

        int quadCount = indices.Count;
        var vertices = new Vector3[quadCount * 4];
        var colors = new Color[quadCount * 4];
        // Double-sided quads to avoid back-face culling visibility issues.
        var triangles = new int[quadCount * 12];
        var uvs = new Vector2[quadCount * 4];

        int vertexOffset = 0;
        int triOffset = 0;
        for (int i = 0; i < indices.Count; i++)
        {
            Vector2Int id = indices[i];
            bool isBoundary = highlightPlaneBoundary && _boundaryTiles.Contains(id);
            float heightOffset = 0f;
            if (enablePerTileHeight && !flattenRenderHeight && _tileHeights.TryGetValue(id, out float h))
            {
                heightOffset = h;
            }

            float extraLift = isBoundary ? boundaryExtraLift : 0f;
            Vector2 localCenter = new Vector2((id.x + 0.5f) * cellSize, (id.y + 0.5f) * cellSize);
            Vector3 center = ToWorld(localCenter) + RenderNormal * (heightOffset + tileThickness * 0.5f + tileLift + extraLift);

            // Keep one continuous overlay: use full cell size so quads touch without visible gaps.
            float half = cellSize * 0.5f;
            Vector3 x = RenderAxisX * half;
            Vector3 y = RenderAxisY * half;

            vertices[vertexOffset + 0] = center - x - y;
            vertices[vertexOffset + 1] = center + x - y;
            vertices[vertexOffset + 2] = center + x + y;
            vertices[vertexOffset + 3] = center - x + y;

            Color tileColor = ResolveTileColor(id, isBoundary);
            colors[vertexOffset + 0] = tileColor;
            colors[vertexOffset + 1] = tileColor;
            colors[vertexOffset + 2] = tileColor;
            colors[vertexOffset + 3] = tileColor;

            uvs[vertexOffset + 0] = new Vector2(id.x, id.y);
            uvs[vertexOffset + 1] = new Vector2(id.x + 1, id.y);
            uvs[vertexOffset + 2] = new Vector2(id.x + 1, id.y + 1);
            uvs[vertexOffset + 3] = new Vector2(id.x, id.y + 1);

            triangles[triOffset + 0] = vertexOffset + 0;
            triangles[triOffset + 1] = vertexOffset + 2;
            triangles[triOffset + 2] = vertexOffset + 1;
            triangles[triOffset + 3] = vertexOffset + 0;
            triangles[triOffset + 4] = vertexOffset + 3;
            triangles[triOffset + 5] = vertexOffset + 2;
            triangles[triOffset + 6] = vertexOffset + 0;
            triangles[triOffset + 7] = vertexOffset + 1;
            triangles[triOffset + 8] = vertexOffset + 2;
            triangles[triOffset + 9] = vertexOffset + 0;
            triangles[triOffset + 10] = vertexOffset + 2;
            triangles[triOffset + 11] = vertexOffset + 3;

            vertexOffset += 4;
            triOffset += 12;
        }

        _combinedMesh.Clear();
        _combinedMesh.vertices = vertices;
        _combinedMesh.colors = colors;
        _combinedMesh.uv = uvs;
        _combinedMesh.triangles = triangles;
        _combinedMesh.RecalculateBounds();
        _combinedMesh.RecalculateNormals();
        if (_combinedMeshCollider != null)
        {
            // Force collider refresh after mesh data update.
            _combinedMeshCollider.sharedMesh = null;
            _combinedMeshCollider.sharedMesh = _combinedMesh;
        }
    }

    private Color ResolveTileColor(Vector2Int id, bool isBoundary)
    {
        if (enableEdgeFade)
        {
            int distance = _tileBoundaryDistance.TryGetValue(id, out int d) ? d : 0;
            float t = Mathf.Clamp01(distance / (float)Mathf.Max(1, edgeFadeRings));
            float alpha = Mathf.Lerp(edgeAlpha, interiorColor.a, t);
            return new Color(interiorColor.r, interiorColor.g, interiorColor.b, alpha);
        }

        return isBoundary ? boundaryColor : interiorColor;
    }

    private void ApplyTileVisual(GameObject tile, Vector2Int id, bool isBoundary)
    {
        if (!colorBoundaryTiles)
        {
            return;
        }

        if (!_tileRenderers.TryGetValue(tile, out MeshRenderer[] renderers) || renderers == null)
        {
            return;
        }

        Color targetColor = ResolveTileColor(id, isBoundary);

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer.sharedMaterial == null)
            {
                continue;
            }

            bool hasBase = renderer.sharedMaterial.HasProperty(BaseColorPropId);
            bool hasColor = renderer.sharedMaterial.HasProperty(ColorPropId);
            if (!hasBase && !hasColor)
            {
                continue;
            }

            renderer.GetPropertyBlock(_tileMpb);
            if (hasBase)
            {
                _tileMpb.SetColor(BaseColorPropId, targetColor);
            }
            if (hasColor)
            {
                _tileMpb.SetColor(ColorPropId, targetColor);
            }
            renderer.SetPropertyBlock(_tileMpb);
        }
    }

    private void UpdatePerTileHeights(List<Vector2Int> visibleTiles)
    {
        if (!enablePerTileHeight)
        {
            _tileHeights.Clear();
            _tileHeightsTarget.Clear();
            return;
        }

        _tileHeightsTarget.Clear();
        var visible = new HashSet<Vector2Int>(visibleTiles);

        for (int i = 0; i < _allHits.Count; i++)
        {
            Vector3 d = _allHits[i] - RenderOrigin;
            float x = Vector3.Dot(d, RenderAxisX);
            float y = Vector3.Dot(d, RenderAxisY);
            float h = Vector3.Dot(d, RenderNormal);
            var id = new Vector2Int(Mathf.FloorToInt(x / cellSize), Mathf.FloorToInt(y / cellSize));
            if (!visible.Contains(id)) continue;

            if (_tileHeightsTarget.TryGetValue(id, out float oldH))
                _tileHeightsTarget[id] = Mathf.Max(oldH, h);
            else
                _tileHeightsTarget[id] = h;
        }

        for (int i = 0; i < visibleTiles.Count; i++)
        {
            var id = visibleTiles[i];
            float current = _tileHeights.TryGetValue(id, out float oldH) ? oldH : 0f;
            if (_tileHeightsTarget.TryGetValue(id, out float target))
                _tileHeights[id] = Mathf.Lerp(current, target, tileHeightAlpha);
            else
                _tileHeights[id] = Mathf.Lerp(current, 0f, tileHeightDecay);
        }

        var stale = new List<Vector2Int>();
        foreach (var key in _tileHeights.Keys)
        {
            if (!visible.Contains(key)) stale.Add(key);
        }
        for (int i = 0; i < stale.Count; i++) _tileHeights.Remove(stale[i]);
    }

    private void EnsurePool(int need)
    {
        while (_tilePool.Count < need)
        {
            var tile = Instantiate(tilePrefab, tileRoot);
            tile.name = "TileCell";

            if (tile.GetComponentInChildren<Collider>() == null)
            {
                tile.AddComponent<BoxCollider>();
            }

            var renderers = tile.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            _tilePool.Add(tile);
            _tileRenderers[tile] = renderers;
        }
    }

    private void FitHorizontalFrame(List<Vector3> pts, out Vector3 origin, out Vector3 axisX, out Vector3 axisY, out Vector3 normal)
    {
        origin = Mean(pts);
        normal = Vector3.up;

        Vector3 camForward = Camera.main ? Camera.main.transform.forward : Vector3.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 1e-6f) camForward = Vector3.forward;
        camForward.Normalize();

        axisX = Vector3.Cross(normal, camForward);
        if (axisX.sqrMagnitude < 1e-6f) axisX = Vector3.right;
        axisX.Normalize();

        axisY = Vector3.Cross(normal, axisX).normalized;
    }

    private List<Vector2> ProjectTo2D(List<Vector3> worldPts, Vector3 origin, Vector3 axisX, Vector3 axisY)
    {
        var list = new List<Vector2>(worldPts.Count);
        foreach (var w in worldPts)
        {
            Vector3 d = w - origin;
            list.Add(new Vector2(Vector3.Dot(d, axisX), Vector3.Dot(d, axisY)));
        }
        return list;
    }

    private static void AddUnique2D(List<Vector2> dst, List<Vector2> src, float minDist, int cap)
    {
        float sq = minDist * minDist;
        for (int i = 0; i < src.Count; i++)
        {
            bool dup = false;
            for (int j = 0; j < dst.Count; j++)
            {
                if ((src[i] - dst[j]).sqrMagnitude <= sq) { dup = true; break; }
            }
            if (!dup) dst.Add(src[i]);
            if (dst.Count > cap) dst.RemoveAt(0);
        }
    }

    private static void AddUnique2DWithGrowthLimit(List<Vector2> dst, List<Vector2> src, float minDist, int cap, float maxGrowStep)
    {
        float dedupSq = minDist * minDist;
        float growSq = maxGrowStep * maxGrowStep;
        for (int i = 0; i < src.Count; i++)
        {
            bool dup = false;
            bool nearExisting = dst.Count == 0;
            for (int j = 0; j < dst.Count; j++)
            {
                float d2 = (src[i] - dst[j]).sqrMagnitude;
                if (d2 <= dedupSq) { dup = true; break; }
                if (d2 <= growSq) nearExisting = true;
            }

            if (!dup && nearExisting) dst.Add(src[i]);
            if (dst.Count > cap) dst.RemoveAt(0);
        }
    }

    private List<Vector2> InsetPolygonTowardCentroid(List<Vector2> poly, float inset)
    {
        if (poly == null || poly.Count < 3) return null;
        if (inset <= 0f) return new List<Vector2>(poly);

        Vector2 c = Centroid(poly);
        var result = new List<Vector2>(poly.Count);
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 v = poly[i] - c;
            float len = v.magnitude;
            if (len <= inset + 1e-5f) return null;
            result.Add(c + v.normalized * (len - inset));
        }
        return result;
    }

    private int CountTiles(List<Vector2> poly, float size, out List<Vector2Int> tileIndices)
    {
        tileIndices = new List<Vector2Int>();
        if (poly == null || poly.Count < 3) return 0;

        GetAABB(poly, out Vector2 min, out Vector2 max);
        int minIx = Mathf.FloorToInt(min.x / size);
        int maxIx = Mathf.CeilToInt(max.x / size);
        int minIy = Mathf.FloorToInt(min.y / size);
        int maxIy = Mathf.CeilToInt(max.y / size);

        int count = 0;
        for (int ix = minIx; ix <= maxIx; ix++)
        {
            for (int iy = minIy; iy <= maxIy; iy++)
            {
                Vector2 c = new Vector2((ix + 0.5f) * size, (iy + 0.5f) * size);
                if (PointInPolygon(c, poly))
                {
                    count++;
                    tileIndices.Add(new Vector2Int(ix, iy));
                }
            }
        }
        return count;
    }

    private void PushArea(float a)
    {
        _areaHistory.Enqueue(a);
        while (_areaHistory.Count > stableFrames) _areaHistory.Dequeue();
    }

    private bool IsStable(Queue<float> q, float threshold, int required)
    {
        if (q.Count < required) return false;
        float min = float.MaxValue, max = float.MinValue, sum = 0f;
        foreach (var v in q) { if (v < min) min = v; if (v > max) max = v; sum += v; }
        float avg = sum / q.Count;
        return avg > 1e-6f && (max - min) <= avg * threshold;
    }

    private static List<Vector3> DedupPoints(List<Vector3> pts, float minDist)
    {
        var result = new List<Vector3>();
        float sq = minDist * minDist;
        foreach (var p in pts)
        {
            bool dup = false;
            foreach (var q in result)
            {
                if ((p - q).sqrMagnitude <= sq) { dup = true; break; }
            }
            if (!dup) result.Add(p);
        }
        return result;
    }

    private static List<Vector3> FilterHitsNearLockedPlane(List<Vector3> hits, Vector3 planeOrigin, Vector3 planeNormal, float maxOffset)
    {
        var kept = new List<Vector3>(hits.Count);
        for (int i = 0; i < hits.Count; i++)
        {
            float offset = Mathf.Abs(Vector3.Dot(hits[i] - planeOrigin, planeNormal));
            if (offset <= maxOffset) kept.Add(hits[i]);
        }
        return kept;
    }

    private static bool IsInsideOrNearPolygon(Vector2 p, List<Vector2> poly, float nearDistance)
    {
        if (poly == null || poly.Count < 3) return true;
        if (PointInPolygon(p, poly)) return true;
        return DistanceToPolygonEdges(p, poly) <= nearDistance;
    }

    private static float DistanceToPolygonEdges(Vector2 p, List<Vector2> poly)
    {
        float best = float.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Count];
            float d = DistancePointToSegment(p, a, b);
            if (d < best) best = d;
        }
        return best;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float ab2 = ab.sqrMagnitude;
        if (ab2 <= 1e-8f) return (p - a).magnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab2);
        Vector2 q = a + ab * t;
        return (p - q).magnitude;
    }

    private static List<Vector3> PickLargestCluster(List<Vector3> pts, float radius)
    {
        var clusters = new List<List<Vector3>>();
        foreach (var p in pts)
        {
            bool assigned = false;
            for (int i = 0; i < clusters.Count; i++)
            {
                if (Vector3.Distance(p, Mean(clusters[i])) <= radius)
                {
                    clusters[i].Add(p);
                    assigned = true;
                    break;
                }
            }
            if (!assigned) clusters.Add(new List<Vector3> { p });
        }

        List<Vector3> best = new List<Vector3>();
        foreach (var c in clusters) if (c.Count > best.Count) best = c;
        return best;
    }

    private static Vector3 Mean(List<Vector3> pts)
    {
        Vector3 s = Vector3.zero;
        for (int i = 0; i < pts.Count; i++) s += pts[i];
        return s / Mathf.Max(1, pts.Count);
    }

    private static void GetAABB(List<Vector2> pts, out Vector2 min, out Vector2 max)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
            maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
        }
        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
    }

    private static bool PointInPolygon(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            bool hit = ((a.y > p.y) != (b.y > p.y)) &&
                       (p.x < (b.x - a.x) * (p.y - a.y) / ((b.y - a.y) + 1e-8f) + a.x);
            if (hit) inside = !inside;
        }
        return inside;
    }

    private static List<Vector2> ConvexHull2D(List<Vector2> pts)
    {
        if (pts.Count <= 3) return new List<Vector2>(pts);
        pts.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        List<Vector2> lower = new List<Vector2>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    private static Vector2 Centroid(List<Vector2> poly)
    {
        float area2 = 0f, cx = 0f, cy = 0f;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p0 = poly[i];
            Vector2 p1 = poly[(i + 1) % n];
            float cross = p0.x * p1.y - p1.x * p0.y;
            area2 += cross;
            cx += (p0.x + p1.x) * cross;
            cy += (p0.y + p1.y) * cross;
        }

        if (Mathf.Abs(area2) < 1e-6f)
        {
            Vector2 s = Vector2.zero;
            foreach (var p in poly) s += p;
            return s / Mathf.Max(1, poly.Count);
        }

        float f = 1f / (3f * area2);
        return new Vector2(cx * f, cy * f);
    }

    private Vector3 ToWorld(Vector2 local2D) => RenderOrigin + RenderAxisX * local2D.x + RenderAxisY * local2D.y;
}
