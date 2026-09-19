# Houdini-Unity-Terrain

Houdini で作ったハイトフィールド（高さの地形データ）を、ボタン1つで Unity の Terrain に取り込むためのツールです。Houdini Apprentice 版での利用を想定しています。

Houdini 側で Export ボタンを押すと、Unity プロジェクトの中に高さデータ（`.r16`）と設定ファイル（`.json`）が書き出されます。Unity に切り替えると、それを読んで TerrainData が自動で作られます。解像度、地形のサイズ、高さの範囲、配置位置はすべて Houdini 側から渡されるので、Unity の Import Raw ダイアログに数値を打ち込む必要はありません。

## 動作環境

| ソフト | バージョン |
|---|---|
| Houdini | Apprentice 版 22.0（動作確認済み） |
| Unity | 6.3 LTS（6000.3.15f1、動作確認済み）。パッケージの対応バージョンは 6000.3 以降 |

Houdini 側の HDA は `.hdanc` 形式なので、Apprentice 版と Non-Commercial 版でのみ使えます。

## リポジトリの構成

```text
Houdini-Unity-Terrain/
  README.md
  LICENSE                                   MIT License
  .gitignore
  houdini/                                  Houdini パッケージとして読み込むフォルダ
    otls/
      unity_hf_export.hdanc                 Export ボタン付きのノード（HDA）
    python/
      hf_export.py                          書き出し処理の本体
    unity_terrain.json.template             Houdini パッケージ設定のひな形
  unity/
    com.honmayuto02.houdini-terrain/        Unity パッケージ
      package.json                          パッケージの名前とバージョン
      Editor/
        HoudiniTerrain.Editor.asmdef        Editor 専用のアセンブリ定義
        HoudiniTerrainPostprocessor.cs      .r16 と .json から TerrainData を作る処理とメニュー
```

各ファイルの役割は次のとおりです。

| ファイル | 役割 |
|---|---|
| `unity_hf_export.hdanc` | Unity Heightfield Export ノード。Export ボタンで `hf_export.export_from_parms` を呼ぶ |
| `hf_export.py` | ハイトフィールドの `height` レイヤーを読み出し、`.r16` と `.json` に書き出す |
| `unity_terrain.json.template` | Houdini パッケージの設定。`hf_export.py` の場所を `PYTHONPATH` に、HDA の場所を Houdini のパスに加える |
| `package.json` | Unity パッケージの名前、バージョン、対応する Unity のバージョン |
| `HoudiniTerrain.Editor.asmdef` | パッケージのスクリプトを Editor 専用のアセンブリにする |
| `HoudiniTerrainPostprocessor.cs` | `.r16` と `.json` のペアを検知して TerrainData を作成、更新する。Place In Scene と Rebuild All のメニューもここにある |

Unity パッケージの中にある `.meta` ファイルは、Unity が生成したものです。フォルダやファイルの GUID を保持しているので、削除したり作り直したりしないでください。

## インストール

### Houdini

1. このリポジトリを好きな場所にクローン、またはダウンロードして展開します。

2. Houdini の Python Shell（Windows → Python Shell）で次を実行し、ユーザー設定フォルダの場所を調べます。

   ```python
   print(hou.getenv('HOUDINI_USER_PREF_DIR'))
   ```

   Windows なら、たいてい `C:\Users\<ユーザー名>\Documents\houdiniXX.X` が表示されます。

3. そのフォルダの中に `packages` フォルダがなければ作り、`houdini/unity_terrain.json.template` を `unity_terrain.json` という名前でコピーします。

4. コピーした `unity_terrain.json` を開き、`UNITY_TERRAIN_TOOLS` の値を、手順1で展開した場所の `houdini` フォルダのパスに書き換えます。パスの区切りは `/` を使ってください。

   ```json
   {
     "env": [
       { "UNITY_TERRAIN_TOOLS": "C:/tools/Houdini-Unity-Terrain/houdini" },
       { "PYTHONPATH": { "value": "$UNITY_TERRAIN_TOOLS/python", "method": "prepend" } }
     ],
     "path": "$UNITY_TERRAIN_TOOLS"
   }
   ```

5. Houdini を再起動します。Python Shell で次を実行し、手順4のフォルダの中のパスが表示されれば読み込めています。

   ```python
   import hf_export; print(hf_export.__file__)
   ```

書き出し先を毎回入力するのが面倒な場合は、`env` に `{ "UNITY_TERRAIN_OUT": "C:/UnityProjects/MyGame/Assets/Terrains" }` を追加しておくと、OUT_Directory の初期値になります。

### Unity

1. Window → Package Manager を開きます。
2. 左上の「+」から Add package from git URL… を選びます。
3. 次の URL を入力して Add を押します。

   ```text
   https://github.com/honmayuto02/Houdini-Unity-Terrain.git?path=/unity/com.honmayuto02.houdini-terrain
   ```

特定のバージョンに固定したいときは、URL の末尾に `#v0.1.0` のようにタグ名を付けてください。

## 使い方

### Houdini で書き出す

1. ハイトフィールドのネットワークの最後で Tab キーを押し、Unity Heightfield Export を置いて、最後のノードの出力をつなぎます。
2. OUT_Directory に、Unity プロジェクトの `Assets` フォルダの中の場所を指定します。例 `C:/UnityProjects/MyGame/Assets/Terrains`
3. OUT_Name に出力ファイル名を入れます。初期値は hip ファイル名です。
4. Export を押します。

成功すると、書き出したファイルのパスと高さの範囲がダイアログに表示され、`<OUT_Name>.r16` と `<OUT_Name>.json` の2つのファイルが作られます。

### Unity で取り込む

Unity のウィンドウに切り替えると、Unity がファイルを取り込み、同じフォルダに `<OUT_Name>.asset`（TerrainData）が作られます。

初回だけ、Project ウィンドウでこの `.asset` を右クリックし、Houdini Terrain → Place In Scene を選んでください。Houdini と同じ位置に Terrain が配置されます。

2回目以降は、Houdini で Export を押して Unity に切り替えるだけで、シーンに置いた Terrain の形と位置が更新されます。

## 地形を作るときの注意

### 解像度は 2ⁿ+1 にする

Unity の Terrain は、一辺のサンプル数が 33, 65, 129, 257, 513, 1025, 2049, 4097 のどれかでないと扱えません。HeightField ノードで Sampling を Corner にし、Size を Grid Spacing の 2ⁿ 倍にするのが簡単です。例えば Grid Spacing 1、Size 1024 で 1025 × 1025 になります。条件を満たさない場合、Export はエラーで止まります。

### HeightField Erode の出力凍結に気をつける

HeightField Erode は計算結果を凍結（Output Frozen）できます。凍結したまま上流の解像度やサイズを変えると、Erode は古い結果を出し続けます。上流のパラメータを変えたら、Erode の Reset Simulation を押して計算し直してください。解像度が合わないというエラーが出たときは、まずここを疑ってください。

### Unity で手を加えた地形は上書きされる

Houdini のデータが正、という前提で作っています。Unity 側で Terrain を彫刻しても、次に Export すると上書きされます。

## うまくいかないとき

### Export しても Unity が反応しない

書き出し先が Unity プロジェクトの `Assets` の中になっているか確認してください。Unity は `Assets` の外のファイルを監視しません。

それでも反応しない場合は、Unity の Console に赤いエラーが出ていないか見てください。プロジェクト内のどこかにコンパイルエラーがあると、このツールも動きません。

パッケージを入れる前から存在していた `.r16` には反応しません。メニューの Tools → Houdini Terrain → Rebuild All を実行すると、`Assets` の中にあるすべての `.r16` と `.json` のペアから TerrainData を作り直せます。もう一度 Export するか、`.r16` を右クリックして Reimport しても取り込めます。

### 地形が Houdini より角ばって見える

Terrain の Pixel Error が大きいと、Unity はメッシュを間引いて表示します。Terrain Settings で Pixel Error を 1 にしてください。Place In Scene で配置した Terrain は、最初から 1 になっています。

## 出力ファイルの仕様

別のツールから同じ形式で書き出したい人向けの情報です。

`.r16` は、符号なし16bit 整数（リトルエンディアン）を N × N 個並べただけのファイルで、ヘッダはありません。サイズはちょうど N² × 2 バイトです。1行が N 個のサンプルで、行は Unity の z の小さい側から、列は Unity の x の小さい側から並んでいます。

値は、地形の最低点を 0、最高点を 65535 として正規化したものです。元の高さ h との関係は次のとおりです。

```text
値 = round((h − hmin) / (hmax − hmin) × 65535)
```

`.json` には次のキーがあります。

| キー | 意味 |
|---|---|
| `resolution` | 一辺のサンプル数 N |
| `hmin`, `hmax` | 高さの最小値と最大値（m） |
| `sizeX`, `sizeZ` | Terrain の幅と奥行き（m）。サンプル間隔 × (N − 1) |
| `posX`, `posZ` | Terrain を置く位置。Terrain の Transform の X と Z |
| `source` | 書き出し元の hip ファイルとノードのパス（記録用） |

Houdini は右手系、Unity は左手系なので、Unity の z は Houdini の z の符号を反転させたものとして扱っています。こうすることで、Houdini で見たとおりの形（鏡像にならない形）で Unity に取り込まれます。

## ライセンス

MIT License。詳しくは `LICENSE` を見てください。