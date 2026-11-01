#!/usr/bin/env python3
"""Generate WorldWeaver's original, periodic 256px PBR starter textures.

No source images or third-party art are used. The output is dedicated to CC0.
Requires numpy and Pillow; fields and periodic strokes are deterministic.
"""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

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


def isotropic_noise(seed, bandwidth):
    """Toroidal, rotationally balanced filtered noise without a preferred axis."""
    rng = np.random.default_rng(seed)
    frequency = np.fft.fftfreq(SIZE) * SIZE
    radius2 = frequency[:, None] ** 2 + frequency[None, :] ** 2
    spectrum = np.fft.fft2(rng.normal(size=(SIZE, SIZE)))
    field = np.fft.ifft2(spectrum * np.exp(-radius2 / (2 * bandwidth ** 2))).real
    return field / max(float(field.std()) * 3, 1e-8)


def grass_strokes(seed):
    """Random short curved blades, all orientations, anti-aliased across tile edges."""
    rng = np.random.default_rng(seed)
    scale = 4
    image = Image.new('L', (SIZE * scale, SIZE * scale), 128)
    draw = ImageDraw.Draw(image)
    for _ in range(1700):
        center = rng.uniform(0, SIZE, 2)
        angle, length = rng.uniform(0, 2 * np.pi), rng.uniform(3.5, 13)
        direction = np.array([np.cos(angle), np.sin(angle)])
        perpendicular = np.array([-direction[1], direction[0]])
        bend, width = rng.uniform(-2.2, 2.2), rng.uniform(.65, 1.6)
        brightness = int(rng.integers(77, 183))
        points = [center + direction * (t - .5) * length + perpendicular * bend * t ** 2 for t in np.linspace(0, 1, 6)]
        for offset_y in (-SIZE, 0, SIZE):
            for offset_x in (-SIZE, 0, SIZE):
                if not (-14 < center[0] + offset_x < SIZE + 14 and -14 < center[1] + offset_y < SIZE + 14):
                    continue
                shifted = [(int(round((p[0] + offset_x) * scale)), int(round((p[1] + offset_y) * scale))) for p in points]
                for segment in range(5):
                    tapered = max(1, int(round(width * scale * (1 - segment * .13))))
                    draw.line(shifted[segment:segment + 2], fill=brightness, width=tapered)
    # Pad with wrapped pixels before Lanczos filtering; edge filtering is periodic too.
    pad = 4 * scale
    padded = np.pad(np.asarray(image), pad, mode='wrap')
    filtered = Image.fromarray(padded).resize((SIZE + 8, SIZE + 8), Image.Resampling.LANCZOS)
    return (np.asarray(filtered, dtype=float)[4:-4, 4:-4] - 128) / 128


def textures(kind, seed, base, roughness):
    y, x = np.mgrid[0:SIZE, 0:SIZE] / SIZE
    noise, fine = periodic_noise(seed), periodic_noise(seed + 500, 8)
    height = .5 + noise * .15 + fine * .06
    shade = .88 + noise * .16 + fine * .10
    color_variation = np.ones((SIZE, SIZE, 3))
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
        coarse = isotropic_noise(seed + 1000, 3)
        detail = isotropic_noise(seed + 1100, 24)
        blades = grass_strokes(seed + 1200)
        shade = .91 + coarse * .075 + detail * .035 + blades * .30
        height = .5 + coarse * .018 + detail * .012 + blades * .035
        dry = np.clip(isotropic_noise(seed + 1300, 5) + .1, 0, 1) * .12
        color_variation[:, :, 0] += dry
        color_variation[:, :, 2] -= dry * .35
        noise = coarse
    elif kind == 'sand':
        coarse = isotropic_noise(seed + 1000, 3)
        grain = isotropic_noise(seed + 1100, 42)
        medium = isotropic_noise(seed + 1200, 14)
        shade = .94 + coarse * .035 + medium * .025 + grain * .045
        height = .5 + medium * .008 + grain * .009
        noise = coarse
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
    rgb = np.clip(np.array(base)[None, None, :] * shade[:, :, None] * color_variation, 0, 255).astype('uint8')
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
