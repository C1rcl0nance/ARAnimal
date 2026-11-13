using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlaneAreaUI : MonoBehaviour
{
    [Header("Data Source")]
    public AutoPlaneAreaEstimator estimator;

    [Header("Texts")]
    public TMP_Text statusText;
    public TMP_Text areaText;
    public TMP_Text sizeText;
    public TMP_Text resultText;
    public TMP_Text hintText;

    [Header("Colors")]
    public Color scanningColor = new Color(1f, 0.8f, 0.2f);
    public Color okColor = new Color(0.2f, 1f, 0.3f);
    public Color failColor = new Color(1f, 0.3f, 0.3f);

    [Header("Optional")]
    public Image resultBg; // 可空
    private bool _loggedMissingRefs;

    private void Start()
    {
        LogMissingRefsOnce();
    }

    void Update()
    {
        LogMissingRefsOnce();
        if (estimator == null) return;

        float area = estimator.CurrentArea;
        float length = estimator.CurrentPlaneLength;
        float width = estimator.CurrentPlaneWidth;
        bool stable = estimator.IsStableNow;

        // Fallback: some runtime states keep valid tile/size data while the smoothed area value
        // has been reset to 0. In that case, derive area from visible tile count instead of
        // showing a misleading zero on the UI.
        if (area <= 0f && estimator.CurrentTileCount > 0 && estimator.cellSize > 0f)
        {
            area = estimator.CurrentTileCount * estimator.cellSize * estimator.cellSize;
        }

        if (areaText != null)
        {
            areaText.text = $"可用面积: {area:F2} m²";
        }
        if (sizeText != null)
        {
            sizeText.text = $"最大平面包围尺寸: 长 {length:F2} m  宽 {width:F2} m";
        }

        if (!stable)
        {
            if (statusText != null)
            {
                statusText.text = "状态: 扫描中";
                statusText.color = scanningColor;
            }
            if (resultText != null)
            {
                resultText.text = "判定: 等待稳定";
                resultText.color = scanningColor;
            }
            if (hintText != null)
            {
                hintText.text = "请缓慢移动设备，保持画面有纹理的平面区域。";
            }
            SetBg(scanningColor);
            return;
        }

        if (statusText != null)
        {
            statusText.text = "状态: 已稳定";
        }
        if (resultText != null)
        {
            resultText.text = "判定: 可放置";
            resultText.color = okColor;
        }
        if (hintText != null)
        {
            hintText.text = "平面满足要求";
        }
        SetBg(okColor);
    }

    private void SetBg(Color c)
    {
        if (resultBg != null)
        {
            c.a = 0.25f;
            resultBg.color = c;
        }
    }

    private void LogMissingRefsOnce()
    {
        if (_loggedMissingRefs)
        {
            return;
        }

        bool missing = false;
        if (estimator == null) { Debug.LogWarning("[PlaneAreaUI] estimator 未绑定"); missing = true; }
        if (statusText == null) { Debug.LogWarning("[PlaneAreaUI] statusText 未绑定"); missing = true; }
        if (areaText == null) { Debug.LogWarning("[PlaneAreaUI] areaText 未绑定"); missing = true; }
        if (sizeText == null) { Debug.LogWarning("[PlaneAreaUI] sizeText 未绑定"); missing = true; }
        if (resultText == null) { Debug.LogWarning("[PlaneAreaUI] resultText 未绑定"); missing = true; }
        if (hintText == null) { Debug.LogWarning("[PlaneAreaUI] hintText 未绑定"); missing = true; }

        _loggedMissingRefs = missing;
    }
}
