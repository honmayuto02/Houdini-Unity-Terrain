"""height レイヤーを 16bit RAW (.r16) + JSON に変換して Unity 用に書き出す。"""
# <このファイルの役割> 
# HoudiniのHeightFieldからheightレイヤーを取り出し、Unityが読み込める2つのファイルに書き出す。
#   1. <name>.r16  ... 高さの値そのもの（16bitの画像データのようなもの）
#   2. <name>.json ... 解像度やサイズ、位置など、Unity側で必要な設定値
# HDA（unity_hf_export.hdanc）のExportボタンからexport_from_parmsが呼ばれ、それがexportを呼びだす
#
# <出力の形式>
#   ・行 : Unityのzが小さい側から並ぶ（Houdiniのzとは向きが逆）
#   ・列 : Unityのxが小さい側から並ぶ（Houdiniのxと同じ向き）
#   ・値 : (h - hmin) / (hmax - hmin) * 65535 を、リトルエンディアンの符号なし16bit整数にしたもの

import os
import json
import numpy as np
import hou

# UnityのTerrainが扱える解像度。2^n + 1 のみ許可。
VALID_RES = {33, 65, 129, 257, 513, 1025, 2049, 4097}


def _find_height(geo):
    # ジオメトリの中から、名前が"height"のボリュームを探す。
    # HeightFieldは複数のレイヤー（height, maskなど）を持てるので、名前で目的のレイヤーを1つだけ選び出す必要がある。
    for p in geo.prims():
        if p.type() != hou.primType.Volume:
            continue
        try:
            if p.attribValue('name') == 'height':
                return p
        except hou.OperationFailed:
            pass
    # 見つからなければ、ここで処理を止めてエラーメッセージを呼び出し元に伝える。
    raise RuntimeError("'height' volume not found")


def _atomic_write(path, data):
    # ファイルを安全に書き込むための関数。
    # 直接pathに書き込むと、書き込みの途中の状態をUnityが読み込んでしまう恐れがある。
    # そこで、まず別名（末尾に "~"）で書き込み、書き終わってから本来の名前に置き換える。
    # os.replaceはほぼ一瞬で行われるので、Unityが中途半端なファイルを見ることがない。
    tmp = path + '~'          # "~" で終わるファイルはUnityが無視する
    with open(tmp, 'wb') as f:
        f.write(data)
    os.replace(tmp, path)


def export(node, out_dir, name):
    # 書き出し処理の本体。node = 書き出し対象のHeightFieldノード
    # out_dir = 書き出し先フォルダ、name = 出力ファイル名（拡張子なし）
    v = _find_height(node.geometry())
    nx, ny, _ = v.resolution()

    # 解像度が正方形でない、または Unityが扱える値でなければエラーにする
    if nx != ny or nx not in VALID_RES:
        raise RuntimeError(
            "Resolution {}x{} is not square 2^n+1 {}.\n"
            "Check HeightField size / grid spacing, and whether an Erode output is frozen."
            .format(nx, ny, sorted(VALID_RES)))

    # ボクセルの高さの値を、2次元の配列に変換
    # allVoxels()は1列に並んだ値を返すので、reshapeでny行×nx列の形に戻している
    h = np.array(v.allVoxels(), dtype=np.float64).reshape(ny, nx)

    # Houdiniのボクセルの並び順は、ノードの組み方によって変わることがある
    # 実際のワールド座標を調べて、Unityのx、zの向きに合わせて配列を並べ替える
    p0 = v.indexToPos((0, 0, 0))
    di = v.indexToPos((1, 0, 0)) - p0   # i を1つ進めたときの移動方向
    dj = v.indexToPos((0, 1, 0)) - p0   # j を1つ進めたときの移動方向
    a = h
    # iがワールドのZ方向に進んでいるなら、配列を転置して「行 = Z, 列 = X」に揃える
    if abs(di[0]) < abs(di[2]):
        a = a.T
        di, dj = dj, di
    # Xが減る向きに並んでいたら、列を左右反転してXが増える向きに揃える
    if di[0] < 0:
        a = a[:, ::-1]
    # Zが増える向きに並んでいたら、行を上下反転してUnityのZが増える向きに揃える
    if dj[2] > 0:
        a = a[::-1, :]

    # HeightFieldの四隅のワールド座標から、Terrainの配置に必要な最小X、最大Zを求める
    corners = [v.indexToPos((i, j, 0)) for i in (0, nx - 1) for j in (0, ny - 1)]
    xmin = min(c[0] for c in corners)
    zmax = max(c[2] for c in corners)

    # 高さの最小値・最大値を求め、0〜65535の範囲に収まるように変換
    # Unity側は、この最小値・最大値をJSONから受け取って元の高さに復元する
    hmin = float(a.min())
    hmax = float(a.max())
    rng = hmax - hmin
    if rng <= 0.0:
        # 高さの差がまったくない（真っ平ら）だと0で割ることになる
        raise RuntimeError("Heightfield is flat (hmax == hmin)")
    u16 = np.round((a - hmin) / rng * 65535.0).astype('<u2')

    # Unity側でTerrainDataを組み立てるために必要な情報をまとめる
    meta = {
        'resolution': nx,
        'hmin': hmin,
        'hmax': hmax,
        'sizeX': (nx - 1) * abs(di[0]),
        'sizeZ': (ny - 1) * abs(dj[2]),
        'posX': xmin,
        'posZ': -zmax,
        'source': hou.hipFile.path() + ':' + node.path(),
    }

    # 書き出し先フォルダがなければ作成し、.r16（高さデータ）と .json（設定値）を書き出す
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.join(out_dir, name)
    _atomic_write(base + '.r16', u16.tobytes())
    _atomic_write(base + '.json', json.dumps(meta, indent=2).encode('utf-8'))
    return base, meta


def export_from_parms(node):
    """出力ノードの Export ボタンから呼ばれるコールバック。"""
    # HDAのExportボタンから呼ばれる関数。ノードのパラメータから値を読み取り、exportを呼ぶ
    out_dir = node.evalParm('hf_out_dir')

    # 出力先が空、または$HIPのような変数が展開されずに残っている場合はここで止める
    if not out_dir or '$' in out_dir:
        hou.ui.displayMessage(
            "Output Dir is empty or contains an unexpanded variable: {!r}".format(out_dir),
            severity=hou.severityType.Error, title='Heightfield Export')
        return

    # 名前が未入力なら、ノード名をそのまま出力ファイル名に
    name = node.evalParm('hf_name') or node.name()
    try:
        base, meta = export(node, out_dir, name)
    except Exception as e:
        # exportの中で何かエラーが起きたら、ダイアログで内容を表示して処理を終える
        hou.ui.displayMessage(str(e), severity=hou.severityType.Error, title='Heightfield Export')
        return

    # 成功したら、書き出し先と結果をダイアログで知らせる
    hou.ui.displayMessage(
        "Exported: {}.r16\nresolution {}, range {:.3f}".format(
            base, meta['resolution'], meta['hmax'] - meta['hmin']),
        title='Heightfield Export')
