#!/usr/bin/env python3
"""Read-only audit of original image bytes and native crop in an exported PPTX.

Run after export/finalization, never as a substitute for viewing every slide.
The manifest is emitted by build_presentation.mjs and records each image view.
"""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import posixpath
import xml.etree.ElementTree as ET
import zipfile

NS = {
    'p': 'http://schemas.openxmlformats.org/presentationml/2006/main',
    'a': 'http://schemas.openxmlformats.org/drawingml/2006/main',
    'r': 'http://schemas.openxmlformats.org/officeDocument/2006/relationships',
    'rel': 'http://schemas.openxmlformats.org/package/2006/relationships',
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def close(actual, expected, tolerance=3):
    return abs(actual - expected) <= tolerance


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pptx', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--workspace', type=Path, default=Path.cwd())
    parser.add_argument('--receipt', type=Path, required=True)
    args = parser.parse_args()
    deck = args.pptx.read_bytes()
    manifest_bytes = args.manifest.read_bytes()
    manifest = json.loads(manifest_bytes)
    views = manifest['evidence_views']
    if not views:
        raise ValueError('No evidence image views in manifest.')
    checked, errors = [], []
    with zipfile.ZipFile(args.pptx) as package:
        for view in views:
            source = (args.workspace / view['path']).read_bytes()
            expected_sha = view['source_sha256']
            if sha(source) != expected_sha:
                errors.append('Source changed: ' + view['path'])
                continue
            slide_path = f"ppt/slides/slide{view['slide']}.xml"
            rel_path = f"ppt/slides/_rels/slide{view['slide']}.xml.rels"
            slide = ET.fromstring(package.read(slide_path))
            relationships = ET.fromstring(package.read(rel_path))
            targets = {
                rel.attrib['Id']: (rel.attrib['Target'].lstrip('/') if rel.attrib['Target'].startswith('/')
                                  else posixpath.normpath(posixpath.join('ppt/slides', rel.attrib['Target'])))
                for rel in relationships
                if rel.attrib.get('TargetMode') != 'External'
            }
            matches = []
            for picture in slide.findall('.//p:pic', NS):
                blip = picture.find('.//a:blip', NS)
                if blip is None:
                    continue
                target = targets.get(blip.attrib.get('{' + NS['r'] + '}embed'))
                if not target or sha(package.read(target)) != expected_sha:
                    continue
                transform = picture.find('./p:spPr/a:xfrm', NS)
                if transform is None:
                    continue
                offset, extent = transform.find('a:off', NS), transform.find('a:ext', NS)
                frame = view['frame']
                actual = [int(offset.attrib['x']), int(offset.attrib['y']), int(extent.attrib['cx']), int(extent.attrib['cy'])]
                expected = [frame[k] * 9525 for k in ['left', 'top', 'width', 'height']]
                if not all(close(a, e) for a, e in zip(actual, expected)):
                    continue
                src_rect = picture.find('./p:blipFill/a:srcRect', NS)
                native_crop = {k: int(src_rect.attrib.get(k, 0)) if src_rect is not None else 0 for k in ['l', 't', 'r', 'b']}
                wanted = view.get('crop')
                expected_crop = {short: round(wanted[long] * 100000) if wanted else 0 for short, long in [('l', 'left'), ('t', 'top'), ('r', 'right'), ('b', 'bottom')]}
                if not all(close(native_crop[k], expected_crop[k], 2) for k in native_crop):
                    continue
                if wanted and src_rect is None:
                    continue
                matches.append({'media_part': target, 'native_srcRect': native_crop, 'frame_emu': actual})
            if len(matches) != 1:
                errors.append(f"Slide{view['slide']} {view['key']}: expected one unchanged image with matching native frame/crop, found{len(matches)}.")
            else:
                checked.append({'slide': view['slide'], 'key': view['key'], 'source': view['path'], 'source_sha256': expected_sha, 'original_embedded_bytes_equal': True, **matches[0]})
    receipt = {
        'kind': 'native-pptx-evidence-image-integrity',
        'checked_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'pptx': str(args.pptx.resolve()), 'pptx_sha256': sha(deck),
        'manifest': str(args.manifest.resolve()), 'manifest_sha256': sha(manifest_bytes),
        'method': 'Read-only ZIP/XML: media bytes match original JPEG/PNG SHA; picture frame and native a:srcRect match each planned viewport.',
        'limits': 'Does not replace personal final-size visual readability/overlap review of every slide.',
        'views': checked, 'errors': errors, 'passed': not errors and len(checked) == len(views),
    }
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'passed': receipt['passed'], 'views': len(checked), 'errors': errors}, ensure_ascii=False))
    raise SystemExit(0 if receipt['passed'] else 1)


if __name__ == '__main__':
    main()
