"""Export the 'height' layer of a Houdini heightfield as 16-bit RAW (.r16) + JSON for Unity.

Output convention
  - rows    : Unity z ascending  (= Houdini z descending, Unity z = -Houdini z)
  - columns : Unity x ascending  (= Houdini x ascending)
  - values  : (h - hmin) / (hmax - hmin) * 65535, little endian uint16
"""
import os
import json
import numpy as np
import hou

VALID_RES = {33, 65, 129, 257, 513, 1025, 2049, 4097}


def _find_height(geo):
    for p in geo.prims():
        if p.type() != hou.primType.Volume:
            continue
        try:
            if p.attribValue('name') == 'height':
                return p
        except hou.OperationFailed:
            pass
    raise RuntimeError("'height' volume not found")


def _atomic_write(path, data):
    tmp = path + '~'          # Unity ignores files ending with '~'
    with open(tmp, 'wb') as f:
        f.write(data)
    os.replace(tmp, path)


def export(node, out_dir, name):
    v = _find_height(node.geometry())
    nx, ny, _ = v.resolution()
    if nx != ny or nx not in VALID_RES:
        raise RuntimeError(
            "Resolution {}x{} is not square 2^n+1 {}.\n"
            "Check HeightField size / grid spacing, and whether an Erode output is frozen."
            .format(nx, ny, sorted(VALID_RES)))

    h = np.array(v.allVoxels(), dtype=np.float64).reshape(ny, nx)   # h[j][i]

    # decide axis mapping from actual world positions
    p0 = v.indexToPos((0, 0, 0))
    di = v.indexToPos((1, 0, 0)) - p0
    dj = v.indexToPos((0, 1, 0)) - p0
    a = h
    if abs(di[0]) < abs(di[2]):   # i runs along Z -> transpose to [row][col=X]
        a = a.T
        di, dj = dj, di
    if di[0] < 0:
        a = a[:, ::-1]
    if dj[2] > 0:
        a = a[::-1, :]

    corners = [v.indexToPos((i, j, 0)) for i in (0, nx - 1) for j in (0, ny - 1)]
    xmin = min(c[0] for c in corners)
    zmax = max(c[2] for c in corners)

    hmin = float(a.min())
    hmax = float(a.max())
    rng = hmax - hmin
    if rng <= 0.0:
        raise RuntimeError("Heightfield is flat (hmax == hmin)")
    u16 = np.round((a - hmin) / rng * 65535.0).astype('<u2')

    meta = {
        'resolution': nx,
        'hmin': hmin,
        'hmax': hmax,
        'sizeX': (nx - 1) * abs(di[0]),
        'sizeZ': (ny - 1) * abs(dj[2]),
        'posX': xmin,
        'posZ': -zmax,            # Unity z = -Houdini z
        'source': hou.hipFile.path() + ':' + node.path(),
    }

    os.makedirs(out_dir, exist_ok=True)
    base = os.path.join(out_dir, name)
    _atomic_write(base + '.r16', u16.tobytes())
    _atomic_write(base + '.json', json.dumps(meta, indent=2).encode('utf-8'))
    return base, meta


def export_from_parms(node):
    """Callback for the Export button on the output node."""
    out_dir = node.evalParm('hf_out_dir')
    name = node.evalParm('hf_name') or node.name()
    try:
        base, meta = export(node, out_dir, name)
    except Exception as e:
        hou.ui.displayMessage(str(e), severity=hou.severityType.Error,
                              title='Heightfield Export')
        return
    hou.ui.displayMessage(
        "Exported: {}.r16\nresolution {}, range {:.3f}".format(
            base, meta['resolution'], meta['hmax'] - meta['hmin']),
        title='Heightfield Export')