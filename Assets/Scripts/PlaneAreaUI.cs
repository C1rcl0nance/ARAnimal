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
    public TMP_Text tilesText;
    public TMP_Text resultText;
    public TMP_Text hintText;

    [Header("Colors")]
    public Color scanningColor = new Color(1f, 0.8f, 0.2f);
    public Color okColor = new Color(0.2f, 1f, 0.3f);
    public Color failColor = new Color(1f, 0.3f, 0.3f);

    [Header("Optional")]
    public Image resultBg; // 可空

    void Update()
    {
        if (estimator == null) return;

        float area = estimator.CurrentArea;
        int tiles = estimator.CurrentTileCount;
        bool stable = estimator.IsStableNow;

        areaText.text = $"可用面积: {area:F2} m²";
        tilesText.text = $"可用砖数: {tiles}";

        if (!stable)
        {
            statusText.text = "状态: 扫描中";
            statusText.color = scanningColor;
            resultText.text = "判定: 等待稳定";
            resultText.color = scanningColor;
            hintText.text = "请缓慢移动设备，保持画面有纹理的平面区域。";
            SetBg(scanningColor);
            return;
        }

        statusText.text = "状态: 已稳定";
        resultText.text = "判定: 可放置";
        resultText.color = okColor;
        hintText.text = "平面满足要求";
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
}
