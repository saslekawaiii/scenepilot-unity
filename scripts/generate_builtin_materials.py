#!/usr/bin/env python3
"""Generate WorldWeaver's original, periodic 256px PBR starter textures.

No source images or third-party art are used. The output is dedicated to CC0.
Requires numpy and Pillow; all pseudo-noise is deterministic Fourier noise.
"""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'Packages/com.worldweaver.toolkit/Runtime/Materials/Textures'
SIZE = 256
CATALOG = [
    ('grass', (74, 105, 51), .88), ('stone', (121, 126, 120), .87),
    ('plaster', (209, 191, 155), .88), ('wood', (137, 93, 53), .7),
    ('roof', (119, 60, 44), .8), ('metal', (133, 142, 146), .36),
    ('sand', (199, 163, 105), .92), ('snow', (224, 232, 232), .8),
    ('bark', (102, 74, 46), .96), ('cloth', (163, 163, 163), .94),
    ('leather', (112, 80, 52), .72), ('water', (57, 122, 143), .16),
    ('cobblestone', (139, 132, 113), .9),
]


def periodic_noise(seed, octaves=6):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:SIZE, 0:SIZE] / SIZE
    result = np.zeros((SIZE, SIZE))
    for octave in range(octaves):
        for _ in range(5):
            fx, fy = rng.integers(1, 2 ** (octave + 1) + 1, 2)
            result += np.sin(2 * np.pi * (x * fx + y * fy) + rng.random() * 2 * np.pi) / (2 ** octave * 5)
    return result


def textures(kind, seed, base, roughness):
    y, x = np.mgrid[0:SIZE, 0:SIZE] / SIZE
    noise, fine = periodic_noise(seed), periodic_noise(seed + 500, 8)
    height = .5 + noise * .15 + fine * .06
    shade = .88 + noise * .16 + fine * .10
    if kind in ('wood', 'bark'):
        warp = .016 * np.sin(2 * np.pi * x * 2) + .01 * np.sin(2 * np.pi * y * 3)
        grain = np.sin(2 * np.pi * (x * (26 if kind == 'wood' else 17) + warp * 12))
        rings = np.sin(2 * np.pi * (x * 3 + .06 * np.sin(2 * np.pi * y * 2)))
        seams = np.exp(-((np.sin(np.pi * x * (4 if kind == 'wood' else 10))) / .075) ** 2)
        shade += .11 * grain + .05 * rings - .32 * seams
        height += .05 * grain - .19 * seams
    elif kind in ('roof', 'cobblestone'):
        columns, rows = (6, 8) if kind == 'roof' else (5, 5) if kind == 'cobblestone' else (4, 3)
        row = np.floor(y * rows)
        fx = (x * columns + .5 * (row % 2)) % 1
        fy = (y * rows) % 1
        edge = np.minimum(np.minimum(fx, 1 - fx), np.minimum(fy, 1 - fy))
        mortar = np.clip(edge * (25 if kind == 'roof' else 17), 0, 1)
        height = .2 + .36 * mortar + noise * .045 + fine * .025
        shade = .52 + .4 * mortar + noise * .11 + fine * .07
        if kind == 'roof': shade += .08 * np.cos(np.pi * fx * 2)
    elif kind == 'stone':
        veins = np.exp(-((noise + .15 * np.sin(2 * np.pi * x * 3)) / .045) ** 2)
        height = .5 + noise * .14 + fine * .04 - veins * .025
        shade = .86 + noise * .17 + fine * .09 + veins * .08
    elif kind == 'cloth':
        weave = np.sin(2 * np.pi * x * 64) * np.sin(2 * np.pi * y * 64)
        shade += .065 * weave
        height += .06 * weave
    elif kind == 'grass':
        blades = np.sin(2 * np.pi * (x * 59 + .1 * np.sin(2 * np.pi * y * 7)))
        shade += .07 * blades
        height += .04 * blades
    elif kind == 'sand':
        ripple = np.sin(2 * np.pi * (x * 8 + .13 * np.sin(2 * np.pi * y * 2)))
        shade += ripple * .07
        height += ripple * .06
    elif kind == 'metal':
        shade = .92 + .03 * np.sin(2 * np.pi * x * 83) + fine * .035
        height = .5 + .005 * fine
    elif kind == 'water':
        waves = np.sin(2 * np.pi * (x * 4 + y * 3)) + .5 * np.sin(2 * np.pi * (x * 7 - y * 4))
        shade = .92 + .04 * waves
        height = .5 + .035 * waves
    elif kind == 'plaster':
        shade = .94 + noise * .045 + fine * .025
        height = .5 + .04 * noise + .03 * fine
    elif kind == 'snow':
        shade = .96 + noise * .025 + fine * .015
        height = .5 + .025 * noise
    rgb = np.clip(np.array(base)[None, None, :] * shade[:, :, None], 0, 255).astype('uint8')
    dx, dy = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 2.3, (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 2.3
    normals = np.stack((-dx, dy, np.ones_like(dx)), axis=2)
    normals /= np.linalg.norm(normals, axis=2)[:, :, None]
    normal = np.clip((normals * .5 + .5) * 255, 0, 255).astype('uint8')
    packed = np.zeros((SIZE, SIZE, 4), dtype='uint8')
    packed[:, :, 0] = 230 if kind == 'metal' else 0
    packed[:, :, 3] = np.clip((1 - roughness + noise * .025) * 255, 0, 255).astype('uint8')
    return [('albedo', rgb), ('normal', normal), ('metallic_smoothness', packed)]


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    files = []
    for seed, (kind, base, roughness) in enumerate(CATALOG, 1):
        for suffix, pixels in textures(kind, seed, base, roughness):
            name = f'{kind}_{suffix}.png'
            path = DEST / name
            Image.fromarray(pixels).save(path, optimize=True)
            files.append({'path': name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size})
    manifest = {'generator': 'scripts/generate_builtin_materials.py', 'origin': 'Original deterministic mathematical patterns; no source images',
                'license': 'CC0-1.0', 'size': [SIZE, SIZE], 'materials': len(CATALOG), 'files': files}
    (DEST.parent / 'builtin-provenance.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'materials': len(CATALOG), 'maps': len(files), 'bytes': sum(f['bytes'] for f in files)}))


if __name__ == '__main__':
    main()
