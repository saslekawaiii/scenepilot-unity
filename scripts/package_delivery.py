"""Package a clean committed repository, with checksums and no local caches."""
import datetime as dt
import hashlib
import json
import subprocess
import tarfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'deliverables'


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    if git('status', '--porcelain').strip():
        raise SystemExit('Commit all nonignored changes before packaging.')
    subprocess.run(['python3', 'scripts/check_repository.py'], cwd=ROOT, check=True)
    sha = git('rev-parse', 'HEAD').decode().strip()
    names = sorted(filter(None, git('ls-files', '-z').decode().split('\0')))
    for name in names:
        parts = Path(name).parts
        if '.private' in parts or '.git' in parts or Path(name).name == '.env':
            raise SystemExit('Excluded local material is tracked: ' + name)
    # The supplied package must match the reviewed source, byte for byte.
    with tarfile.open(OUT / 'com.worldweaver.toolkit-1.0.0.tgz') as package:
        for member in package.getmembers():
            if member.isfile():
                source = ROOT / 'Packages/com.worldweaver.toolkit' / Path(member.name).relative_to('package')
                if package.extractfile(member).read() != source.read_bytes():
                    raise SystemExit('Package differs from source: ' + str(source))
    files = [{'path': name, 'bytes': (ROOT / name).stat().st_size,
              'sha256': digest(ROOT / name)} for name in names]
    timestamp = int(git('show', '-s', '--format=%ct', 'HEAD').decode().strip())
    manifest = {'source_commit': sha, 'files': files, 'file_count': len(files),
                'note': 'Source archive excludes .private, model weights, caches, secrets and third-party thesis PDFs.'}
    bundle = OUT / 'WorldWeaver-repository.bundle'
    subprocess.run(['git', 'bundle', 'create', str(bundle), '--all'], cwd=ROOT, check=True)
    subprocess.run(['git', 'bundle', 'verify', str(bundle)], cwd=ROOT, check=True,
                   stdout=subprocess.DEVNULL)
    archive = OUT / 'WorldWeaver_исходники.zip'
    stamp = dt.datetime.fromtimestamp(timestamp, dt.timezone.utc).timetuple()[:6]
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for item in files:
            info = zipfile.ZipInfo('WorldWeaver/' + item['path'], date_time=stamp)
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(info, (ROOT / item['path']).read_bytes())
        info = zipfile.ZipInfo('WorldWeaver/FILE_MANIFEST.json', date_time=stamp)
        info.compress_type = zipfile.ZIP_DEFLATED
        output.writestr(info, json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    with zipfile.ZipFile(archive) as output:
        if output.testzip() is not None:
            raise SystemExit('ZIP integrity failed.')
    outputs = [{'path': 'deliverables/' + path.name, 'bytes': path.stat().st_size,
                'sha256': digest(path)} for path in sorted(OUT.iterdir())
               if path.is_file() and path.name != 'Состав_поставки.json']
    manifest['outputs'] = outputs
    manifest['verified'] = {'zip_crc': True, 'git_bundle': True, 'package_matches_source': True}
    (OUT / 'Состав_поставки.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'source_commit': sha, 'tracked_files': len(files),
                      'outputs': outputs, 'verified': manifest['verified']}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
