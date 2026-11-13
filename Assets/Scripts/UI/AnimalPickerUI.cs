using System;
using TMPro;
using UnityEngine;

public class AnimalPickerUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot;
    public bool autoHidePanelAfterSelect = true;
    public TMP_Text currentAnimalText;
    public TMP_Text currentAnimalTextShort;

    [Header("Labels")]
    public string noSelectionLabel = "\u5f53\u524d\uff1a\u672a\u9009\u62e9";
    public string selectionPrefix = "\u5f53\u524d\uff1a";

    [Header("Runtime (Read Only)")]
    [SerializeField] private string selectedAnimalId = "";

    public Action<string> OnAnimalSelected;
    public string SelectedAnimalId => selectedAnimalId;
    public bool HasSelection => !string.IsNullOrEmpty(selectedAnimalId);

    private void Start()
    {
        RefreshSelectionTexts();
    }

    public void OpenPanel()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
    }

    public void ClosePanel()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public void TogglePanel()
    {
        if (panelRoot == null)
        {
            return;
        }

        panelRoot.SetActive(!panelRoot.activeSelf);
    }

    public void ClearSelection()
    {
        selectedAnimalId = "";
        RefreshSelectionTexts();
        OnAnimalSelected?.Invoke(selectedAnimalId);
    }

    public void Show(Vector3 worldPos)
    {
        OpenPanel();
    }

    public void Hide()
    {
        ClosePanel();
    }

    public void SelectAnimal(string animalId)
    {
        selectedAnimalId = string.IsNullOrWhiteSpace(animalId) ? "" : animalId.Trim();
        RefreshSelectionTexts();
        OnAnimalSelected?.Invoke(selectedAnimalId);

        if (autoHidePanelAfterSelect)
        {
            ClosePanel();
        }
    }

    private void RefreshSelectionTexts()
    {
        string text = HasSelection ? selectionPrefix + selectedAnimalId : noSelectionLabel;

        if (currentAnimalText != null)
        {
            currentAnimalText.text = text;
        }

        if (currentAnimalTextShort != null)
        {
            currentAnimalTextShort.text = text;
        }
    }
}
