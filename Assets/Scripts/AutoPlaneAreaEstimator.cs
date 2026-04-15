using easyar;
using System.Collections.Generic;
using UnityEngine;

public class AutoPlaneAreaEstimator : MonoBehaviour
{
    [Header("EasyAR (必须手动拖拽)")]
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

    [Header("Runtime Grid Render (纯预制体)")]
    public GameObject tilePrefab; // 必须手动拖拽
    [Range(0.001f, 0.03f)] public float tileThickness = 0.001f;
    [Range(0f, 0.03f)] public float tileLift = 0.001f;
    public Transform tileRoot;

    [Header("Per-Tile Height")]
    public bool enablePerTileHeight = true;
    [Range(0.01f, 1f)] public float tileHeightAlpha = 0.35f;
    [Range(0.001f, 0.2f)] public float tileHeightDecay = 0.02f;

    [Header("Lock + Expand")]
    public bool lockWhenStable = true;
    public int stableLockFrames = 10;
    [Tooltip("锁定后累计点的最大数量（防无限增长）")]
    public int maxAccumulatedPoints = 600;
    [Tooltip("锁定后累计点去重半径（局部2D）")]
    public float lockedDedup2D = 0.03f;
    [Tooltip("锁定后每次允许向外扩张的最大距离（局部2D）。过大的远点会被忽略，防止扩到空气区域")]
    [Range(0.02f, 1.5f)] public float maxLockedExpandStep = 0.35f;
    [Tooltip("锁定后允许的平面法向高度误差（米）。超出该误差的命中点将被忽略")]
    [Range(0.005f, 0.2f)] public float maxLockedPlaneOffset = 0.04f;

    [Header("Behavior")]
    public bool debugLog = false;

    // runtime state
    private float _startTime;
    private int _missFrames = 0;
    private int _validStreak = 0;
    private int _stableStreak = 0;

    private Queue<float> _areaHistory = new Queue<float>();
    private bool _emaInitialized = false;
    private float _emaArea = 0f;

    private int _tileCount;
    private float _area;
    private float _areaSmoothed;
    private bool _stable;

    // current frame basis
    private Vector3 _origin;
    private Vector3 _axisX;
    private Vector3 _axisY;
    private Vector3 _normal;

    // locked basis
    private bool _isLocked = false;
    private Vector3 _lockedOrigin;
    private Vector3 _lockedAxisX;
    private Vector3 _lockedAxisY;
    private Vector3 _lockedNormal;

    // current frame data
    private List<Vector3> _allHits = new List<Vector3>();
    private List<Vector3> _mainCluster = new List<Vector3>();
    private List<Vector2> _poly2D = new List<Vector2>();
    private List<Vector2Int> _tileIndices = new List<Vector2Int>();
    private List<Vector2Int> _lastGoodTiles = new List<Vector2Int>();

    // locked accumulated points in LOCKED 2D space
    private readonly List<Vector2> _lockedAccum2D = new List<Vector2>();

    // render pool
    private readonly List<GameObject> _tilePool = new List<GameObject>();
    private readonly Dictionary<Vector2Int, float> _tileHeights = new Dictionary<Vector2Int, float>();
    private readonly Dictionary<Vector2Int, float> _tileHeightsTarget = new Dictionary<Vector2Int, float>();

    // UI getters
    public float CurrentArea => _areaSmoothed;
    public int CurrentTileCount => _lastGoodTiles.Count;
    public bool IsStableNow => _stable;
    public bool IsLocked => _isLocked;

    private Vector3 RenderOrigin => _isLocked ? _lockedOrigin : _origin;
    private Vector3 RenderAxisX => _isLocked ? _lockedAxisX : _axisX;
    private Vector3 RenderAxisY => _isLocked ? _lockedAxisY : _axisY;
    private Vector3 RenderNormal => _isLocked ? _lockedNormal : _normal;

    void Start()
    {
        _startTime = Time.time;

        if (motionTrackerFrameSource == null)
        {
            Debug.LogError("[AutoPlaneAreaEstimator] motionTrackerFrameSource 未设置。");
            enabled = false;
            return;
        }

        if (tileRoot == null)
        {
            Debug.LogError("[AutoPlaneAreaEstimator] tileRoot 未设置。请手动创建 TileRoot 并拖拽。");
            enabled = false;
            return;
        }
        tileRoot.localScale = Vector3.one;

        if (tilePrefab == null)
        {
            Debug.LogError("[AutoPlaneAreaEstimator] tilePrefab 未设置。纯预制体模式下这是必填。");
            enabled = false;
            return;
        }
    }

    void Update()
    {
        if (Time.time - _startTime < warmupSeconds)
        {
            _stable = false;
            RenderTiles(_lastGoodTiles);
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
            _lastGoodTiles = new List<Vector2Int>(_tileIndices);

        UpdatePerTileHeights(_lastGoodTiles);
        RenderTiles(_lastGoodTiles);

        if (debugLog)
        {
            Debug.Log($"[AutoPlane] lock={_isLocked}, hits={_allHits.Count}, cluster={_mainCluster.Count}, " +
                      $"acc2D={_lockedAccum2D.Count}, tiles={_tileCount}, show={_lastGoodTiles.Count}, area={_areaSmoothed:F3}");
        }
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
                    nearHull.Add(pts[i]);
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

        RenderTiles(_lastGoodTiles);

        if (_missFrames > maxMissFrames)
        {
            _areaHistory.Clear();
            _emaInitialized = false;
            _emaArea = 0f;
            _areaSmoothed = 0f;
            _area = 0f;
            _tileCount = 0;

            _lastGoodTiles.Clear();
            _tileIndices.Clear();
            _tileHeights.Clear();
            _tileHeightsTarget.Clear();

            RenderTiles(_lastGoodTiles);
            _missFrames = 0;
        }

        if (debugLog) Debug.Log($"[AutoPlane] invalid: {reason}, miss={_missFrames}/{maxMissFrames}");
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

            float heightOffset = 0f;
            if (enablePerTileHeight && _tileHeights.TryGetValue(id, out float h)) heightOffset = h;
            Vector3 center = ToWorld(local) + RenderNormal * (heightOffset + tileThickness * 0.5f + tileLift);

            var go = _tilePool[i];
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(RenderAxisY, RenderNormal);
            go.transform.localScale = new Vector3(cellSize * 0.95f, tileThickness, cellSize * 0.95f);
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

            // 点击逻辑要用，确保有碰撞体
            if (tile.GetComponentInChildren<Collider>() == null)
            {
                tile.AddComponent<BoxCollider>();
            }

            // 关闭阴影（所有子渲染器）
            var renderers = tile.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            _tilePool.Add(tile);
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
