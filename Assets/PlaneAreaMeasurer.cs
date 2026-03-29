using easyar;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlaneAreaMeasurer : MonoBehaviour
{
    [Header("组件引用")]
    public MotionTrackerFrameSource motionTracker;
    public Camera arCamera;

    [Header("UI")]
    public TextMeshProUGUI areaText;           // 显示面积的UI
    public Button startMeasureBtn;   // 开始测量按钮
    public Button clearBtn;          // 清除按钮

    [Header("可视化")]
    public GameObject markerPrefab;  // 标记点预制体
    public LineRenderer boundaryLine; // 边界线

    [Header("自动采样设置")]
    public float autoSampleInterval = 0.5f;     // 自动采样间隔（秒）
    public float sampleStep = 0.1f;             // 屏幕采样步长（0.1 = 10%屏幕宽度）
    public float minX = 0.2f;                   // 屏幕左边界（归一化坐标）
    public float maxX = 0.8f;                   // 屏幕右边界
    public float minY = 0.4f;                   // 屏幕上边界（地面通常在屏幕下半部分）
    public float maxY = 0.8f;                   // 屏幕下边界
    public float duplicateThreshold = 0.2f;     // 重复点判断阈值（米）

    private List<Vector3> samplePoints = new List<Vector3>();
    private bool isMeasuring = false;
    private float lastAutoSampleTime = 0f;

    void Start()
    {
        // 自动获取组件
        if (motionTracker == null)
            motionTracker = FindObjectOfType<MotionTrackerFrameSource>();

        if (arCamera == null)
            arCamera = Camera.main;

        // 绑定按钮事件
        if (startMeasureBtn != null)
            startMeasureBtn.onClick.AddListener(StartMeasuring);

        if (clearBtn != null)
            clearBtn.onClick.AddListener(ClearPoints);

        // 初始化边界线
        if (boundaryLine != null)
            boundaryLine.positionCount = 0;
    }

    void Update()
    {
        // 自动采样模式：每隔一段时间自动采样
        if (isMeasuring && Time.time - lastAutoSampleTime >= autoSampleInterval)
        {
            lastAutoSampleTime = Time.time;
            AutoSampleCurrentView();
        }
    }

    /// <summary>
    /// 自动在当前视野内采样（网格采样）
    /// </summary>
    void AutoSampleCurrentView()
    {
        if (motionTracker == null || !motionTracker.enabled)
        {
            Debug.LogWarning("Motion Tracker 未运行");
            return;
        }

        int newPointsCount = 0;

        // 在屏幕指定区域内进行网格采样
        for (float x = minX; x <= maxX; x += sampleStep)
        {
            for (float y = minY; y <= maxY; y += sampleStep)
            {
                Vector2 viewPoint = new Vector2(x, y);

                // 进行水平面命中测试
                List<Vector3> hitPoints = motionTracker.HitTestAgainstHorizontalPlane(viewPoint);

                if (hitPoints != null && hitPoints.Count > 0)
                {
                    Vector3 hitPoint = hitPoints[0];

                    // 检查是否已经存在附近的点（去重）
                    if (!IsPointAlreadyAdded(hitPoint, duplicateThreshold))
                    {
                        AddSamplePoint(hitPoint);
                        newPointsCount++;
                    }
                }
            }
        }

        if (newPointsCount > 0)
        {
            // 更新边界线可视化
            UpdateBoundaryLine();

            // 更新面积显示
            float area = CalculateBoundingBoxArea(samplePoints);
            if (areaText != null)
                areaText.text = $"面积: {area:F2} 平方米\n点数: {samplePoints.Count}";

            Debug.Log($"自动采样完成，新增 {newPointsCount} 个点，当前共 {samplePoints.Count} 个点，面积: {area:F2} 平方米");
        }
    }

    /// <summary>
    /// 检查某个位置是否已经存在采样点
    /// </summary>
    bool IsPointAlreadyAdded(Vector3 newPoint, float threshold)
    {
        foreach (Vector3 p in samplePoints)
        {
            if (Vector3.Distance(p, newPoint) < threshold)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 添加采样点并生成可视化标记
    /// </summary>
    void AddSamplePoint(Vector3 point)
    {
        samplePoints.Add(point);

        // 可视化：生成标记点
        if (markerPrefab != null)
        {
            GameObject marker = Instantiate(markerPrefab, point, Quaternion.identity);
            marker.transform.localScale = Vector3.one * 0.05f;

            // 标记点的文本显示序号
            var text = marker.GetComponentInChildren<TextMesh>();
            if (text != null)
                text.text = samplePoints.Count.ToString();
        }
    }

    /// <summary>
    /// 计算包围盒面积（AABB - 轴对齐包围盒）
    /// </summary>
    float CalculateBoundingBoxArea(List<Vector3> points)
    {
        if (points.Count < 2) return 0;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Vector3 p in points)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float width = maxX - minX;
        float length = maxZ - minZ;

        return width * length;
    }

    /// <summary>
    /// 更新边界线可视化
    /// </summary>
    void UpdateBoundaryLine()
    {
        if (boundaryLine == null || samplePoints.Count < 2) return;

        boundaryLine.positionCount = samplePoints.Count + 1;

        for (int i = 0; i < samplePoints.Count; i++)
        {
            boundaryLine.SetPosition(i, samplePoints[i]);
        }

        // 闭合多边形
        boundaryLine.SetPosition(samplePoints.Count, samplePoints[0]);
    }

    /// <summary>
    /// 开始测量模式
    /// </summary>
    public void StartMeasuring()
    {
        isMeasuring = true;
        ClearPoints();
        lastAutoSampleTime = Time.time;
        Debug.Log("开始自动测量：请缓慢移动手机扫描地面");

        if (areaText != null)
            areaText.text = "自动扫描中...";
    }

    /// <summary>
    /// 清除所有采样点
    /// </summary>
    public void ClearPoints()
    {
        samplePoints.Clear();

        // 删除所有标记点
        GameObject[] markers = GameObject.FindGameObjectsWithTag("SampleMarker");
        foreach (GameObject marker in markers)
        {
            Destroy(marker);
        }

        if (boundaryLine != null)
            boundaryLine.positionCount = 0;

        if (areaText != null)
            areaText.text = "面积: --";
    }

    /// <summary>
    /// 结束测量模式
    /// </summary>
    public void StopMeasuring()
    {
        isMeasuring = false;
        Debug.Log("测量结束");
    }

    /// <summary>
    /// 用于动物放置的简单命中测试
    /// </summary>
    public bool TryGetPlacementPoint(Vector2 screenPos, out Vector3 placementPoint)
    {
        placementPoint = Vector3.zero;

        if (motionTracker == null || !motionTracker.enabled)
            return false;

        Vector2 viewPoint = new Vector2(
            screenPos.x / Screen.width,
            screenPos.y / Screen.height
        );

        List<Vector3> hitPoints = motionTracker.HitTestAgainstHorizontalPlane(viewPoint);

        if (hitPoints != null && hitPoints.Count > 0)
        {
            placementPoint = hitPoints[0];
            return true;
        }

        return false;
    }
}