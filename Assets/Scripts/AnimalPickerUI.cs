using System;
using UnityEngine;

public class AnimalPickerUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot; // 面板根节点（默认隐藏）

    private Vector3 _lastTapWorld;
    public Action<string, Vector3> OnAnimalSelected;

    public void Show(Vector3 worldPos)
    {
        _lastTapWorld = worldPos;
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // 按钮事件调用：参数例如 "cat" / "dog" / "duck"
    public void SelectAnimal(string animalId)
    {
        OnAnimalSelected?.Invoke(animalId, _lastTapWorld);
        Hide();
    }
}