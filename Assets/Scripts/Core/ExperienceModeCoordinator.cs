using System;
using System.Collections.Generic;
using UnityEngine;

public class ExperienceModeCoordinator : MonoBehaviour
{
    public enum ExperienceMode
    {
        FreePlacement = 0,
        AnchoredExperience = 1,
    }

    [Serializable]
    public class ModeBinding
    {
        public List<GameObject> gameObjects = new List<GameObject>();
        public List<Behaviour> behaviours = new List<Behaviour>();
    }

    [Header("Refs")]
    public WorldAnchorManager worldAnchorManager;

    [Header("Mode Bindings")]
    public ModeBinding freePlacementMode = new ModeBinding();
    public ModeBinding anchoredExperienceMode = new ModeBinding();

    [Header("Startup")]
    public bool applyOnStart = true;
    public ExperienceMode fallbackModeWhenNoAnchorManager = ExperienceMode.FreePlacement;

    [Header("Debug")]
    public bool logModeSwitch = true;

    [SerializeField] private ExperienceMode currentMode = ExperienceMode.FreePlacement;

    public ExperienceMode CurrentMode => currentMode;
    public bool IsFreePlacementMode => currentMode == ExperienceMode.FreePlacement;
    public bool IsAnchoredExperienceMode => currentMode == ExperienceMode.AnchoredExperience;

    public event Action<ExperienceMode> ModeChanged;

    private void Start()
    {
        if (applyOnStart)
        {
            RefreshMode(forceApply: true);
        }
    }

    private void Update()
    {
        RefreshMode(forceApply: false);
    }

    public void RefreshMode(bool forceApply = false)
    {
        ExperienceMode desiredMode = ResolveModeFromRuntime();
        if (!forceApply && desiredMode == currentMode)
        {
            return;
        }

        currentMode = desiredMode;
        ApplyMode(currentMode);
        ModeChanged?.Invoke(currentMode);

        if (logModeSwitch)
        {
            Debug.Log($"[ExperienceModeCoordinator] Mode -> {currentMode}");
        }
    }

    public void ForceSetMode(ExperienceMode mode)
    {
        currentMode = mode;
        ApplyMode(currentMode);
        ModeChanged?.Invoke(currentMode);

        if (logModeSwitch)
        {
            Debug.Log($"[ExperienceModeCoordinator] Force mode -> {currentMode}");
        }
    }

    private ExperienceMode ResolveModeFromRuntime()
    {
        if (worldAnchorManager == null)
        {
            return fallbackModeWhenNoAnchorManager;
        }

        return worldAnchorManager.IsLocalized
            ? ExperienceMode.AnchoredExperience
            : ExperienceMode.FreePlacement;
    }

    private void ApplyMode(ExperienceMode mode)
    {
        bool freeMode = mode == ExperienceMode.FreePlacement;
        SetBindingState(freePlacementMode, freeMode);
        SetBindingState(anchoredExperienceMode, !freeMode);
    }

    private void SetBindingState(ModeBinding binding, bool enabled)
    {
        if (binding == null)
        {
            return;
        }

        for (int i = 0; i < binding.gameObjects.Count; i++)
        {
            GameObject target = binding.gameObjects[i];
            if (target == null || target.activeSelf == enabled)
            {
                continue;
            }
            target.SetActive(enabled);
        }

        for (int i = 0; i < binding.behaviours.Count; i++)
        {
            Behaviour target = binding.behaviours[i];
            if (target == null || target.enabled == enabled)
            {
                continue;
            }
            target.enabled = enabled;
        }
    }
}
