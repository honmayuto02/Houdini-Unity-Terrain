# Houdini-Unity-Terrain

Houdiniで作成した地形を、ボタン1つでUnityのTerrainに取り込むためのツールです。Houdini Apprentice版での利用を想定しています。

Houdini側でExportボタンを押すと、Unityプロジェクトの中に高さデータ（`.r16`）と設定ファイル（`.json`）が書き出されます。Unityに切り替えると、それを読んでTerrainDataが自動で作られます。解像度、地形のサイズ、高さの範囲、配置位置はすべてHoudini側から渡されるので、UnityのImport Rawダイアログに数値を打ち込む必要はありません。

## 動作環境

| ソフト | バージョン |
|---|---|
| Houdini | Apprentice版 22.0（動作確認済み） |
| Unity | 6.3 LTS（6000.3.15f1、動作確認済み）。パッケージの対応バージョンは6000.3以降 |

Houdini側のHDAは`.hdanc`形式なので、Apprentice版とNon-Commercial版でのみ使えます。

## リポジトリの構成

```text
Houdini-Unity-Terrain/
  README.md
  LICENSE                                   MIT License
  .gitignore
  houdini/                                  Houdiniパッケージとして読み込むフォルダ
    otls/
      unity_hf_export.hdanc                 Exportボタン付きのノード（HDA）
    python/
      hf_export.py                          書き出し処理の本体
    unity_terrain.json.template             Houdiniパッケージ設定のひな形
  unity/
    com.honmayuto02.houdini-terrain/        Unityパッケージ
      package.json                          パッケージの名前とバージョン
      Editor/
        HoudiniTerrain.Editor.asmdef        Editor専用のアセンブリ定義
        HoudiniTerrainPostprocessor.cs      .r16 と .json からTerrainDataを作る処理とメニュー
```

各ファイルの役割は次のとおりです。

| ファイル | 役割 |
|---|---|
| `unity_hf_export.hdanc` | Unity Heightfield Exportノード。Exportボタンで`hf_export.export_from_parms`を呼ぶ |
| `hf_export.py` | HeightFieldの`height`レイヤーを読み出し、`.r16`と`.json`に書き出す |
| `unity_terrain.json.template` | Houdiniパッケージの設定。`hf_export.py`の場所を`PYTHONPATH`に、HDAの場所をHoudiniのパスに加える |
| `package.json` | Unityパッケージの名前、バージョン、対応する Unityのバージョン |
| `HoudiniTerrain.Editor.asmdef` | パッケージのスクリプトをEditor専用のアセンブリにする |
| `HoudiniTerrainPostprocessor.cs` | `.r16` と `.json` のペアを検知してTerrainDataを作成、更新する。Place In Sceneと Rebuild Allのメニューもここにある |

Unityパッケージの中にある `.meta` ファイルは、Unityが生成したものです。フォルダやファイルのGUIDを保持しているので、削除したり作り直したりしないでください。

## インストール

### Houdini

1. このリポジトリを好きな場所にクローン、またはダウンロードして展開します。

2. HoudiniのPython Shell（Windows → Python Shell）で次を実行し、ユーザー設定フォルダの場所を調べます。

   ```python
   print(hou.getenv('HOUDINI_USER_PREF_DIR'))
   ```

3. そのフォルダの中に`packages`フォルダがなければ作り、`houdini/unity_terrain.json.template`を`unity_terrain.json`という名前でコピーします。

4. コピーした`unity_terrain.json`を開き、`UNITY_TERRAIN_TOOLS`の値を、手順1で展開した場所の`houdini`フォルダのパスに書き換えます。

   ```json
   {
     "env": [
       { "UNITY_TERRAIN_TOOLS": "C:/tools/Houdini-Unity-Terrain/houdini" },
       { "PYTHONPATH": { "value": "$UNITY_TERRAIN_TOOLS/python", "method": "prepend" } }
     ],
     "path": "$UNITY_TERRAIN_TOOLS"
   }
   ```

5. Houdiniを再起動します。Python Shellで次を実行し、手順4のフォルダの中のパスが表示されれば読み込めています。

   ```python
   import hf_export; print(hf_export.__file__)
   ```

書き出し先を毎回入力するのが面倒な場合は、`env`に `{ "UNITY_TERRAIN_OUT": "C:/UnityProjects/MyGame/Assets/Terrains" }`を追加しておくと、OUT_Directoryの初期値になります。

### Unity

1. Window → Package Managerを開きます。
2. 左上の「+」から Add package from git URL…を選びます。
3. 次のURLを入力してAddを押します。

   ```text
   https://github.com/honmayuto02/Houdini-Unity-Terrain.git?path=/unity/com.honmayuto02.houdini-terrain
   ```

特定のバージョンに固定したいときは、URLの末尾に `#v0.1.0`のようにタグ名を付けてください。

## 使い方

### Houdiniで書き出す

1. HeightFieldのネットワークの最後でUnity Heightfield Exportを置いて、最後のノードの出力を接続します。
2. OUT_Directoryに、Unityプロジェクトの`Assets`フォルダの中の場所を指定します。例 `C:/UnityProjects/MyGame/Assets/Terrains`
3. OUT_Nameに出力ファイル名を入れます。初期値はhipファイル名です。
4. Exportを押します。

成功すると、書き出したファイルのパスと高さの範囲がダイアログに表示され、`<OUT_Name>.r16`と`<OUT_Name>.json`の2つのファイルが作られます。

### Unityで取り込む

Unityのウィンドウに切り替えると、Unityがファイルを取り込み、同じフォルダに`<OUT_Name>.asset`（TerrainData）が作られます。

初回だけ、Projectウィンドウでこの`.asset`を右クリックし、Houdini Terrain → Place In Sceneを選んでください。Houdiniと同じ位置にTerrainが配置されます。

2回目以降は、HoudiniでExportを押してUnityに切り替えるだけで、シーンに置いたTerrainの形と位置が更新されます。

## 地形を作るときの注意

### 解像度は 2ⁿ+1 にする

UnityのTerrainは、一辺のサンプル数が 33, 65, 129, 257, 513, 1025, 2049, 4097 のどれかでないと扱えません。HeightFieldノードで SamplingをCornerにし、SizeをGrid Spacingの2ⁿ倍にするのが簡単です。例えばGrid Spacing 1、Size 1024で1025 × 1025になります。条件を満たさない場合、Exportはエラーで止まります。

### Unityで手を加えた地形は上書きされる

Unity側でTerrainを彫刻しても、次にExportすると上書きされます。

## うまくいかないとき

### ExportしてもUnityが反応しない

書き出し先がUnityプロジェクトの`Assets`の中になっているか確認してください。Unityは`Assets`の外のファイルを監視しません。

それでも反応しない場合は、UnityのConsoleに赤いエラーが出ていないか見てください。プロジェクト内のどこかにコンパイルエラーがあると、このツールも動きません。

パッケージを入れる前から存在していた`.r16`には反応しません。メニューのTools → Houdini Terrain → Rebuild Allを実行すると、`Assets`の中にあるすべての`.r16` と `.json`のペアからTerrainDataを作り直せます。もう一度Exportするか、`.r16`を右クリックして Reimportしても取り込めます。

### 地形がHoudiniより角ばって見える

TerrainのPixel Errorが大きいと、Unityはメッシュを間引いて表示します。Terrain SettingsでPixel Errorを 1 にしてください。Place In Sceneで配置したTerrainは、最初から1になっています。

## 出力ファイルの仕様

別のツールから同じ形式で書き出したい人向けの情報です。

`.r16`は、符号なし16bit整数（リトルエンディアン）を N × N個並べただけのファイルで、サイズはちょうど N² × 2 バイトです。1行がN個のサンプルで、行はUnityのzの小さい側から、列はUnityのxの小さい側から並んでいます。

値は、地形の最低点を0、最高点を65535として正規化したものです。元の高さ h との関係は次のとおりです。

```text
値 = round((h − hmin) / (hmax − hmin) × 65535)
```

`.json` には次のキーがあります。

| キー | 意味 |
|---|---|
| `resolution` | 一辺のサンプル数 N |
| `hmin`, `hmax` | 高さの最小値と最大値（m） |
| `sizeX`, `sizeZ` | Terrainの幅と奥行き（m）。サンプル間隔 × (N − 1) |
| `posX`, `posZ` | Terrainを置く位置。TerrainのTransformのXとZ|
| `source` | 書き出し元のhipファイルとノードのパス（記録用） |

Houdiniは右手系、Unityは左手系なので、Unityのzは Houdiniのzの符号を反転させたものとして扱っています。こうすることで、Houdiniで見たとおりの形（鏡像にならない形）でUnityに取り込まれます。

## ライセンス

MIT License。詳しくは `LICENSE` を見てください。