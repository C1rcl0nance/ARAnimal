using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildForestAnimalBundles
{
    private const string OutputFolderName = "AssetBundles";

    private static readonly (string bundleName, string folderPath)[] BundleFolders =
    {
        ("bear_bundle", "Assets/Realistic/Forest Animals/Bear_Family"),
        ("boar_bundle", "Assets/Realistic/Forest Animals/Boar_Family"),
        ("deer_bundle", "Assets/Realistic/Forest Animals/Deers"),
        ("fox_bundle", "Assets/Realistic/Forest Animals/Fox_Family"),
        ("hare_bundle", "Assets/Realistic/Forest Animals/Hare_Family"),
        ("moose_bundle", "Assets/Realistic/Forest Animals/Moose_Family"),
        ("wolf_bundle", "Assets/Realistic/Forest Animals/Wolf_Family"),
    };

    [MenuItem("Tools/AssetBundles/Build Forest Animals For Android")]
    public static void BuildForAndroid()
    {
        Build(BuildTarget.Android);
    }

    private static void Build(BuildTarget target)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = Path.Combine(projectRoot, OutputFolderName, target.ToString());
        Directory.CreateDirectory(outputPath);

        var bundleBuilds = BuildBundleDefinitions();
        if (bundleBuilds == null || bundleBuilds.Length == 0)
        {
            Debug.LogError("[BuildForestAnimalBundles] No valid bundle definitions found.");
            return;
        }

        var manifest = BuildPipeline.BuildAssetBundles(
            outputPath,
            bundleBuilds,
            BuildAssetBundleOptions.ChunkBasedCompression,
            target);

        if (manifest == null)
        {
            Debug.LogError("[BuildForestAnimalBundles] AssetBundle build failed.");
            return;
        }

        var builtBundles = bundleBuilds
            .Select(def => Path.Combine(outputPath, def.assetBundleName))
            .Where(File.Exists)
            .ToArray();

        if (builtBundles.Length > 0)
        {
            Debug.Log("[BuildForestAnimalBundles] Built bundles:\n" + string.Join("\n", builtBundles));
            return;
        }

        Debug.LogWarning("[BuildForestAnimalBundles] Build finished, but no configured bundles were found in output.");
    }

    private static AssetBundleBuild[] BuildBundleDefinitions()
    {
        var builds = new List<AssetBundleBuild>();

        foreach (var entry in BundleFolders)
        {
            string[] assetPaths = CollectLowQualityIpPrefabs(entry.folderPath);
            if (assetPaths.Length == 0)
            {
                Debug.LogError($"[BuildForestAnimalBundles] No low quality IP prefabs found for '{entry.bundleName}' in '{entry.folderPath}'.");
                continue;
            }

            builds.Add(new AssetBundleBuild
            {
                assetBundleName = entry.bundleName,
                assetNames = assetPaths,
            });

            Debug.Log($"[BuildForestAnimalBundles] {entry.bundleName} -> {assetPaths.Length} prefabs");
        }

        return builds.ToArray();
    }

    private static string[] CollectLowQualityIpPrefabs(string folderPath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        var selected = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.Contains("/prefabs/"))
            .Where(path => IsLowQualityIpPrefab(Path.GetFileNameWithoutExtension(path)))
            .OrderBy(path => path)
            .ToArray();

        return selected;
    }

    private static bool IsLowQualityIpPrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        bool isLowQuality = prefabName.IndexOf("LowPoly", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            prefabName.IndexOf("Lowpoly", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            prefabName.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool isInPlace = prefabName.IndexOf("_IP", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         prefabName.EndsWith("IP", System.StringComparison.OrdinalIgnoreCase);
        return isLowQuality && isInPlace;
    }
}
