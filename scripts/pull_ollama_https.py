"""Install official Ollama blobs using Python's CA bundle when Go TLS times out.

Every blob is SHA256-verified before an atomic rename. Existing models are preserved.
"""
import argparse
import hashlib
import json
import ssl
import urllib.request
from pathlib import Path
import certifi


def pull(name):
    model, tag = name.split(':', 1) if ':' in name else (name, 'latest')
    if '/' in model or '..' in tag:
        raise ValueError('Only official library model tags are supported')
    ctx = ssl.create_default_context(cafile=certifi.where())
    base = f'https://ollama.com/v2/library/{model}'
    req = urllib.request.Request(f'{base}/manifests/{tag}', headers={'Accept': 'application/vnd.docker.distribution.manifest.v2+json'})
    with urllib.request.urlopen(req, context=ctx, timeout=90) as response:
        raw = response.read()
    manifest = json.loads(raw)
    root = Path.home() / '.ollama/models'
    blobs = root / 'blobs'
    blobs.mkdir(parents=True, exist_ok=True)
    for entry in [manifest['config'], *manifest['layers']]:
        digest = entry['digest']
        target = blobs / digest.replace(':', '-')
        if target.exists() and target.stat().st_size == entry['size']:
            continue
        temporary = target.with_suffix('.worldweaver-download')
        for attempt in range(3):
            try:
                sha = hashlib.sha256()
                with urllib.request.urlopen(f'{base}/blobs/{digest}', context=ctx, timeout=120) as response, temporary.open('wb') as out:
                    while True:
                        chunk = response.read(1024 * 1024)
                        if not chunk:
                            break
                        sha.update(chunk)
                        out.write(chunk)
                if sha.hexdigest() != digest.split(':', 1)[1] or temporary.stat().st_size != entry['size']:
                    raise ValueError('Blob verification failed')
                temporary.replace(target)
                print(name, 'verified', entry['size'], flush=True)
                break
            except Exception:
                if attempt == 2:
                    raise
    output = root / 'manifests/registry.ollama.ai/library' / model / tag
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(raw)
    print(name, 'installed', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('model')
    pull(parser.parse_args().model)
