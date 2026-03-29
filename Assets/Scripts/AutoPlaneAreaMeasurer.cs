using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using easyar;

public class AutoPlaneAreaMeasurer : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Camera arCamera;
    [SerializeField] private TextMeshProUGUI areaText;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private GameObject pointPrefab;

    // EasyAR Motion Tracker Frame Source（从 Inspector 拖拽）
    [Header("EasyAR FrameSource")]
    [SerializeField] private MotionTrackerFrameSource motionFrameSource;

    [Header("扫描参数")]
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private float maxRayDistance = 10f;
    [SerializeField] private int raysPerScan = 12;
    [SerializeField] private float mergeDistance = 0.05f;
    [SerializeField] private float groundY = 0f; // 仅在回退计算时使用

    [Header("可视化")]
    [SerializeField] private bool showPoints = true;
    [SerializeField] private float pointScale = 0.015f;

    private List<Vector3> collectedPoints = new List<Vector3>();
    private List<GameObject> pointMarkers = new List<GameObject>();
    private bool isScanning = true;

    void Start()
    {
        if (arCamera == null)
            arCamera = Camera.main;

        // 如果没有在 Inspector 指定，尝试自动找到场景中的 MotionTrackerFrameSource
        if (motionFrameSource == null)
            motionFrameSource = FindObjectOfType<MotionTrackerFrameSource>();

        SetupLineRenderer();
        StartCoroutine(AutoScanCoroutine());
    }

    IEnumerator AutoScanCoroutine()
    {
        yield return new WaitForSeconds(1.5f);

        while (isScanning)
        {
            string debug = $"相机位置: ({arCamera.transform.position.x:F3}, {arCamera.transform.position.y:F3}, {arCamera.transform.position.z:F3})\n";

            CastMultipleRays();

            if (collectedPoints.Count >= 3)
            {
                List<Vector2> flatPoints = new List<Vector2>();
                foreach (var p in collectedPoints)
                    flatPoints.Add(new Vector2(p.x, p.z));

                List<Vector2> hull = ConvexHull(flatPoints);
                float area = CalculatePolygonArea(hull);
                float bboxArea = CalculateBoundingBoxArea(flatPoints);

                debug += $"平面面积: {area:F4} m²\n";
                debug += $"包围盒面积: {bboxArea:F4} m²\n";
                debug += $"采集点数: {collectedPoints.Count}";

                DrawHullBoundary(hull);
            }
            else
            {
                debug += $"正在扫描平面... ({collectedPoints.Count} 个点)";
            }

            areaText.text = debug;

            yield return new WaitForSeconds(scanInterval);
        }
    }

    void CastMultipleRays()
    {
        int gridSize = Mathf.CeilToInt(Mathf.Sqrt(raysPerScan));
        float marginX = Screen.width * 0.15f;
        float marginY = Screen.height * 0.15f;
        float stepX = (Screen.width - 2 * marginX) / gridSize;
        float stepY = (Screen.height - 2 * marginY) / gridSize;

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                float x = marginX + stepX * i + Random.Range(-stepX * 0.3f, stepX * 0.3f);
                float y = marginY + stepY * j + Random.Range(-stepY * 0.3f, stepY * 0.3f);
                TryCollectPoint(new Vector2(x, y));
            }
        }
    }

    void TryCollectPoint(Vector2 screenPos)
    {
        // 首先尝试使用 EasyAR 的 HitTest（优选：水平面，再回退到点云）
        Vector2 norm = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);

        // 先尝试水平面 Hit Test（返回在平面上距离射线最近的点列表）
        if (motionFrameSource != null)
        {
            try
            {
                List<Vector3> hits = motionFrameSource.HitTestAgainstHorizontalPlane(norm);
                if (hits != null && hits.Count > 0)
                {
                    // 取第一个命中点（最近的）
                    Vector3 point = hits[0];

                    if (!IsTooCloseToExisting(point))
                        AddCollectedPoint(point);

                    return;
                }

                // 如果没有水平面命中，再尝试点云命中（得到点云中最接近射线的一点）
                List<Vector3> pcHits = motionFrameSource.HitTestAgainstPointCloud(norm);
                if (pcHits != null && pcHits.Count > 0)
                {
                    Vector3 point = pcHits[0];
                    if (!IsTooCloseToExisting(point))
                        AddCollectedPoint(point);

                    return;
                }
            }
            catch (System.Exception e)
            {
                // 防御性编码：如果 MotionTrackerFrameSource 在某些平台上抛异常，回退到数学方法
                Debug.LogWarning("MotionTrackerFrameSource HitTest 调用失败，回退到几何射线计算: " + e.Message);
            }
        }

        // 最后回退：数学计算（射线与平面 Y=groundY 的交点）
        Ray ray = arCamera.ScreenPointToRay(screenPos);

        // 射线必须朝下才能打到地面（这是对水平地面的简单假设）
        if (ray.direction.y >= 0)
            return;

        float t = (groundY - ray.origin.y) / ray.direction.y;
        if (t < 0.1f || t > maxRayDistance)
            return;

        Vector3 fallbackPoint = ray.origin + ray.direction * t;
        fallbackPoint.y = groundY;

        if (!IsTooCloseToExisting(fallbackPoint))
            AddCollectedPoint(fallbackPoint);
    }

    void AddCollectedPoint(Vector3 point)
    {
        collectedPoints.Add(point);

        if (showPoints && pointPrefab != null)
        {
            GameObject marker = Instantiate(pointPrefab, point, Quaternion.identity);
            marker.transform.localScale = Vector3.one * pointScale;
            pointMarkers.Add(marker);
        }
    }

    bool IsTooCloseToExisting(Vector3 newPoint)
    {
        foreach (var p in collectedPoints)
        {
            float dist = Vector2.Distance(
                new Vector2(newPoint.x, newPoint.z),
                new Vector2(p.x, p.z)
            );
            if (dist < mergeDistance)
                return true;
        }
        return false;
    }

    float CalculateBoundingBoxArea(List<Vector2> points)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in points)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        return (maxX - minX) * (maxY - minY);
    }

    float CalculatePolygonArea(List<Vector2> points)
    {
        int n = points.Count;
        if (n < 3) return 0f;

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += points[i].x * points[j].y;
            area -= points[j].x * points[i].y;
        }
        return Mathf.Abs(area) / 2f;
    }

    List<Vector2> ConvexHull(List<Vector2> points)
    {
        List<Vector2> pts = new List<Vector2>(points);
        int n = pts.Count;
        if (n < 3) return pts;

        int minIdx = 0;
        for (int i = 1; i < n; i++)
        {
            if (pts[i].y < pts[minIdx].y ||
               (pts[i].y == pts[minIdx].y && pts[i].x < pts[minIdx].x))
                minIdx = i;
        }

        Vector2 tmp = pts[0];
        pts[0] = pts[minIdx];
        pts[minIdx] = tmp;

        Vector2 pivot = pts[0];

        pts.Sort(1, n - 1, Comparer<Vector2>.Create((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - pivot.y, a.x - pivot.x);
            float angleB = Mathf.Atan2(b.y - pivot.y, b.x - pivot.x);
            int cmp = angleA.CompareTo(angleB);
            if (cmp != 0) return cmp;
            float distA = (a - pivot).sqrMagnitude;
            float distB = (b - pivot).sqrMagnitude;
            return distA.CompareTo(distB);
        }));

        Stack<Vector2> stack = new Stack<Vector2>();
        stack.Push(pts[0]);
        stack.Push(pts[1]);

        for (int i = 2; i < n; i++)
        {
            while (stack.Count > 1)
            {
                Vector2 top = stack.Pop();
                Vector2 second = stack.Peek();
                if (Cross(second, top, pts[i]) > 0)
                {
                    stack.Push(top);
                    break;
                }
            }
            stack.Push(pts[i]);
        }

        return new List<Vector2>(stack);
    }

    float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    void DrawHullBoundary(List<Vector2> hull)
    {
        if (lineRenderer == null || hull == null || hull.Count == 0) return;

        // 计算一个合理的 Y 用来显示边界（取采集点的平均高度）
        float avgY = 0f;
        foreach (var p in collectedPoints) avgY += p.y;
        avgY = collectedPoints.Count > 0 ? avgY / collectedPoints.Count : groundY;

        lineRenderer.positionCount = hull.Count + 1;
        for (int i = 0; i < hull.Count; i++)
        {
            lineRenderer.SetPosition(i, new Vector3(hull[i].x, avgY, hull[i].y));
        }
        lineRenderer.SetPosition(hull.Count, new Vector3(hull[0].x, avgY, hull[0].y));
    }

    void SetupLineRenderer()
    {
        if (lineRenderer == null) return;

        lineRenderer.startWidth = 0.005f;
        lineRenderer.endWidth = 0.005f;
        lineRenderer.positionCount = 0;
        lineRenderer.loop = false;
        lineRenderer.useWorldSpace = true;
    }

    public void ResetMeasurement()
    {
        collectedPoints.Clear();

        foreach (var m in pointMarkers)
            Destroy(m);
        pointMarkers.Clear();

        if (lineRenderer != null)
            lineRenderer.positionCount = 0;

        areaText.text = "正在扫描平面...";
    }
}