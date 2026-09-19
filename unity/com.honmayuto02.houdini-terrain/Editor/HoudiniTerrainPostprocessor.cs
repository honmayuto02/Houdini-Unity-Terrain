// Watches <name>.r16 + <name>.json pairs and builds/updates <name>.asset (TerrainData).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class HoudiniTerrainPostprocessor : AssetPostprocessor
{
    [System.Serializable]
    class Meta
    {
        public int resolution;
        public float hmin, hmax, sizeX, sizeZ, posX, posZ;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                       string[] moved, string[] movedFrom)
    {
        var targets = new HashSet<string>();
        foreach (var p in imported)
        {
            if (!p.EndsWith(".r16") && !p.EndsWith(".json")) continue;
            string b = p.Substring(0, p.LastIndexOf('.'));
            if (File.Exists(b + ".r16") && File.Exists(b + ".json")) targets.Add(b);
        }
        foreach (var b in targets)
        {
            string basePath = b;
            EditorApplication.delayCall += () => Build(basePath);
        }
    }

    static Meta LoadMeta(string basePath)
    {
        return JsonUtility.FromJson<Meta>(File.ReadAllText(basePath + ".json"));
    }

    static void Build(string basePath)
    {
        Meta m = LoadMeta(basePath);
        byte[] bytes = File.ReadAllBytes(basePath + ".r16");
        int r = m.resolution;
        if (bytes.Length != r * r * 2)
        {
            Debug.LogError($"[HoudiniTerrain] {basePath}.r16: size {bytes.Length} != {r}x{r}x2");
            return;
        }

        var h = new float[r, r];                       // h[z, x]
        for (int z = 0; z < r; z++)
            for (int x = 0; x < r; x++)
            {
                int i = (z * r + x) * 2;
                h[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
            }

        string assetPath = basePath + ".asset";
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
        bool isNew = td == null;
        if (isNew) td = new TerrainData();

        td.heightmapResolution = r;                    // resolution first,
        td.size = new Vector3(m.sizeX, m.hmax - m.hmin, m.sizeZ);   // then size
        td.SetHeights(0, 0, h);

        if (isNew) AssetDatabase.CreateAsset(td, assetPath);
        else EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        // Update terrains already placed in open scenes
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (t.terrainData != td) continue;
            t.transform.position = new Vector3(m.posX, m.hmin, m.posZ);
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
        Debug.Log($"[HoudiniTerrain] built {assetPath} (res {r}, range {m.hmax - m.hmin:F3})");
    }

    [MenuItem("Tools/Houdini Terrain/Rebuild All")]
    static void RebuildAll()
    {
        var targets = new HashSet<string>();
        foreach (var p in Directory.GetFiles("Assets", "*.r16", SearchOption.AllDirectories))
        {
            string b = p.Replace('\\', '/');
            b = b.Substring(0, b.LastIndexOf('.'));
            if (File.Exists(b + ".json")) targets.Add(b);
        }
        foreach (var b in targets) Build(b);
        Debug.Log($"[HoudiniTerrain] rebuilt {targets.Count} terrain(s)");
    }

    [MenuItem("Assets/Houdini Terrain/Place In Scene")]
    static void PlaceInScene()
    {
        var td = (TerrainData)Selection.activeObject;
        string path = AssetDatabase.GetAssetPath(td);
        string basePath = path.Substring(0, path.LastIndexOf('.'));
        Meta m = LoadMeta(basePath);

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = td.name;
        go.transform.position = new Vector3(m.posX, m.hmin, m.posZ);
        go.GetComponent<Terrain>().heightmapPixelError = 1f;
        Undo.RegisterCreatedObjectUndo(go, "Place Houdini Terrain");
        Selection.activeGameObject = go;
    }

    [MenuItem("Assets/Houdini Terrain/Place In Scene", true)]
    static bool PlaceInSceneValidate()
    {
        if (!(Selection.activeObject is TerrainData td)) return false;
        string path = AssetDatabase.GetAssetPath(td);
        return File.Exists(path.Substring(0, path.LastIndexOf('.')) + ".json");
    }
}