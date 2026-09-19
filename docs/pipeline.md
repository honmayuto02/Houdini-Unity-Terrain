# Houdini から Unity へハイトフィールドを渡す仕組み

Houdini のハイトフィールドを Unity の Terrain にするまでの流れと、各段階で何をしているかをまとめます。使い方は README にあります。ここでは、なぜそうなっているのかを説明します。

## 全体の流れ

```text
Houdini
  HeightField の height レイヤー
    -> hf_export.py が読み出して正規化
    -> <name>.r16 と <name>.json を Unity の Assets に書き出す

Unity
  AssetPostprocessor が .r16 と .json のペアを検知
    -> <name>.asset (TerrainData) を作成または更新
    -> シーン上の Terrain の位置を更新
```

Unity の Import Raw ダイアログに数値を打ち込む代わりに、解像度、サイズ、高さの範囲、配置位置を JSON に入れて Houdini 側から渡します。Unity 側は人が判断する項目を持たないので、取り込みのたびに同じ結果になります。

## 書き出し側

書き出しの本体は `hf_export.py` の `export` 関数です。

### height レイヤーの取り出し

ジオメトリの中から、名前が `height` のボリュームプリミティブを探します。見つからなければエラーにします。

解像度は正方形で、かつ 33, 65, 129, 257, 513, 1025, 2049, 4097 のどれかでなければなりません。Unity の Terrain がこの解像度しか扱えないためです。合わないときは、サイズと Grid Spacing の設定に加えて、HeightField Erode の出力が凍結されたままになっていないかを確認するよう、エラーメッセージで案内します。

### 軸の対応

ボクセルの並びは決め打ちにしません。 `indexToPos` で先頭のボクセルと隣のボクセルの実際のワールド座標を調べ、その向きから並べ替えを決めます。

- i が Z 方向に進んでいれば転置する
- X が減る向きなら列を反転する
- Z が増える向きなら行を反転する

結果として、行は Unity の z が小さい側から、列は Unity の x が小さい側から並びます。Houdini は右手系、Unity は左手系なので、Unity の z は Houdini の z の符号を反転したものとして扱います。これで、Houdini で見たとおりの形が鏡像にならずに取り込まれます。

### 正規化

高さの最小値 hmin と最大値 hmax を求め、次の式で 16bit 整数にします。

```text
値 = round((h - hmin) / (hmax - hmin) * 65535)
```

hmax と hmin が等しい平らな地形は、割り算ができないのでエラーにします。値はリトルエンディアンの符号なし 16bit 整数で、ヘッダなしで並べたものが `.r16` です。

### メタデータ

`.json` には次の値を入れます。

| キー | 意味 |
|---|---|
| resolution | 一辺のサンプル数 |
| hmin, hmax | 高さの最小値と最大値 |
| sizeX, sizeZ | Terrain の幅と奥行き |
| posX, posZ | Terrain の配置位置 |
| source | 書き出し元の hip ファイルとノードのパス |

Unity 側は、Terrain の高さ方向のサイズを hmax - hmin、Y 座標を hmin にします。正規化で失った高さの絶対値は、これで元に戻ります。

### 書き込み方

ファイルは、末尾に `~` を付けた一時ファイルに書いてから、目的のファイル名に置き換えます。Unity は `~` で終わるファイルを無視するので、書き込みの途中を取り込んでしまうことがありません。

## 読み込み側

Unity 側の処理は `HoudiniTerrainPostprocessor` にあります。取り込まれたファイルのうち `.r16` か `.json` を見つけたら、同じ名前のもう一方があるかを確認し、そろっていれば TerrainData を作ります。

TerrainData では、先に `heightmapResolution` を設定してから `size` を設定し、そのあとで `SetHeights` に高さを渡します。この順番は変えないでください。

既に `.asset` があれば、作り直さずに中身を更新します。これで、シーンに置いた Terrain や、そこに塗ったテクスチャなどの参照が切れません。シーン上に同じ TerrainData を使う Terrain があれば、位置も更新します。

パッケージを入れる前からあったファイルは、取り込みの通知が来ないので反応しません。メニューの Tools → Houdini Terrain → Rebuild All で、Assets の中にあるすべてのペアを作り直せます。

## hf_export.py の読み込み方

以前は `hf_export.py` をユーザー設定フォルダの `python3.xxlibs` に直接置いていました。Houdini のバージョンが変わるとフォルダ名も変わり、コピーし直す手間がかかります。

今は、リポジトリの `houdini/python` を `PYTHONPATH` に加える方法にしています。Houdini パッケージの JSON で環境変数を設定します。

```json
{
  "env": [
    { "UNITY_TERRAIN_TOOLS": "C:/tools/Houdini-Unity-Terrain/houdini" },
    { "PYTHONPATH": { "value": "$UNITY_TERRAIN_TOOLS/python", "method": "prepend" } }
  ],
  "path": "$UNITY_TERRAIN_TOOLS"
}
```

`path` の指定により、同じフォルダの `otls` にある HDA も自動で読み込まれます。読み込めたかどうかは、Houdini の Python Shell で次を実行して確認します。

```python
import hf_export
print(hf_export.__file__)
```

## Export ボタンを HDA にした経緯と配布構成

最初は、出力ノードのパラメータに `hf_out_dir` と `hf_name` を追加し、Export ボタンのコールバックから `hf_export.export_from_parms` を呼んでいました。この方法では、書き出したいノードごとにパラメータとボタンを手で足す必要があります。

そこで、パラメータとボタンを HDA の Unity Heightfield Export にまとめました。ボタンのコールバックは、次の 1 行です。

```python
hf_export.export_from_parms(kwargs['node'])
```

利用者は、ハイトフィールドの最後にこのノードをつなぎ、Output Dir と Name を入れて Export を押すだけになります。HDA は `.hdanc` 形式で保存しているので、Apprentice 版と Non-Commercial 版で使えます。

配布は、次の 2 つに分けています。

| 対象 | 形式 | 中身 |
|---|---|---|
| Houdini | Houdini パッケージ | `houdini` フォルダ。HDA と Python を `unity_terrain.json` の設定で読み込む |
| Unity | UPM パッケージ | Git URL で Package Manager から追加する。場所は `unity/com.honmayuto02.houdini-terrain` |

Houdini 側と Unity 側は、ファイルの形式でしかつながっていないので、片方だけ更新することもできます。
