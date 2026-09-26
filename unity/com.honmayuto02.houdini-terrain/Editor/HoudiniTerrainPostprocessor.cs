// <このファイルの役割>
// Houdiniから書き出された <name>.r16（高さデータ）と <name>.json（設定値）を検知し、UnityのTerrainDataを自動で作成・更新する
// Unityプロジェクトにファイルが取り込まれるたびに OnPostprocessAllAssets が
// Unity本体から自動的に呼ばれ、そこから Build にたどり着く、という流れ

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class HoudiniTerrainPostprocessor : AssetPostprocessor
{
    // hf_export.pyが書き出す.jsonの中身と対応するクラス
    // JsonUtilityでそのまま読み込めるように、キー名をJSON側と揃えている
    [System.Serializable]
    class Meta
    {
        public int resolution;
        public float hmin, hmax, sizeX, sizeZ, posX, posZ;
    }

    // Unityがアセットを取り込んだ直後に自動で呼び出すコールバック
    // importedには、今回のインポートで新しく取り込まれた（変更された）ファイルのパスが入る
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        // 取り込まれたファイルの中から、.r16 と .json が両方そろっているものだけを集める
        var targets = new HashSet<string>();
        foreach (var p in imported)
        {
            if (!p.EndsWith(".r16") && !p.EndsWith(".json")) continue;
            string b = p.Substring(0, p.LastIndexOf('.'));   // 拡張子を除いた共通部分
            if (File.Exists(b + ".r16") && File.Exists(b + ".json")) targets.Add(b);
        }
        foreach (var b in targets)
        {
            // Buildを直接呼ばずdelayCallを使うのは、インポート処理の最中にAssetDatabaseを操作すると不安定になることがあるため。
            // 1フレーム後（インポートが落ち着いたあと）に実行されるようにしている
            string basePath = b;
            EditorApplication.delayCall += () => Build(basePath);
        }
    }

    // <name>.jsonを読み込んでMetaに変換
    static Meta LoadMeta(string basePath)
    {
        return JsonUtility.FromJson<Meta>(File.ReadAllText(basePath + ".json"));
    }

    // .r16 + .json の組からTerrainDataを作る（すでにあれば中身を更新する）処理
    static void Build(string basePath)
    {
        Meta m = LoadMeta(basePath);
        byte[] bytes = File.ReadAllBytes(basePath + ".r16");
        int r = m.resolution;

        // ファイルサイズが「解像度 × 解像度 × 2バイト」と一致しなければ、
        // ファイルが壊れているかJSONのresolutionと食い違っているとみなして中断する。
        if (bytes.Length != r * r * 2)
        {
            Debug.LogError($"[HoudiniTerrain] {basePath}.r16: size {bytes.Length} != {r}x{r}x2");
            return;
        }

        // .r16は、リトルエンディアンの符号なし16bit整数をr×r個並べたバイナリデータ。
        // 1マスごとに2バイト読み、0〜65535を0〜1の範囲のfloatに変換してUnityのSetHeightsが要求する[z, x]の2次元配列に格納
        var h = new float[r, r];    // 添字の並びは [z, x]
        for (int z = 0; z < r; z++)
            for (int x = 0; x < r; x++)
            {
                int i = (z * r + x) * 2;
                // bytes[i]が下位バイト、bytes[i+1]が上位バイト
                h[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
            }

        // 同名のTerrainDataがすでにあれば読み込んで更新し、なければ新規に作る
        string assetPath = basePath + ".asset";
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
        bool isNew = td == null;
        if (isNew) td = new TerrainData();

        // 解像度を先に設定してからsizeを設定する。順番を逆にすると、
        // Unity内部でsizeが意図しない値に補正されてしまうことがあるため、この順番を守る
        td.heightmapResolution = r;
        td.size = new Vector3(m.sizeX, m.hmax - m.hmin, m.sizeZ);
        td.SetHeights(0, 0, h);

        if (isNew) AssetDatabase.CreateAsset(td, assetPath);
        else EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        // すでにシーン上に配置されているTerrainのうち、このデータを使っているものがあれば
        // Houdini側で計算した位置（posX, hmin, posZ）に合わせて位置を更新
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (t.terrainData != td) continue;
            t.transform.position = new Vector3(m.posX, m.hmin, m.posZ);
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);   // シーンの変更を保存対象にする
        }
        Debug.Log($"[HoudiniTerrain] built {assetPath} (res {r}, range {m.hmax - m.hmin:F3})");
    }

    // パッケージを導入する前からAssets内にあった.r16 / .jsonは、
    // OnPostprocessAllAssetsの通知を受け取れないため自動では処理されない
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

    // 初めてそのアセットをシーンに置くときに1回だけ使うことを想定
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
        go.GetComponent<Terrain>().heightmapPixelError = 1f;           // 表示をカクつかせないための初期値
        Undo.RegisterCreatedObjectUndo(go, "Place Houdini Terrain");   // Ctrl+Z で取り消せるようにする
        Selection.activeGameObject = go;
    }

    // 上のメニューを、対応する.jsonがあるときだけ有効にするための検証関数
    // Unityはメニュー名の末尾にtrueを付けたメソッドを「有効/無効の判定」として自動で呼ぶ
    [MenuItem("Assets/Houdini Terrain/Place In Scene", true)]
    static bool PlaceInSceneValidate()
    {
        if (!(Selection.activeObject is TerrainData td)) return false;
        string path = AssetDatabase.GetAssetPath(td);
        return File.Exists(path.Substring(0, path.LastIndexOf('.')) + ".json");
    }
}
