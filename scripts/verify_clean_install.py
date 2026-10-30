"""Import the package into a fresh project and validate scene preservation and regeneration."""
import argparse
import gzip
import hashlib
import json
import os
import shutil
import subprocess
import tarfile
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', default='/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity')
    parser.add_argument('--git-url', help='Optional private/public UPM Git URL; Git authorization must already be configured')
    args = parser.parse_args()
    out = ROOT/'deliverables'
    out.mkdir(exist_ok=True)
    archive = out/'com.worldweaver.toolkit-1.0.0.tgz'
    package = ROOT/'Packages/com.worldweaver.toolkit'
    # Stable timestamps and owner metadata keep delivered bytes reproducible.
    with archive.open('wb') as raw, gzip.GzipFile(filename='', fileobj=raw, mode='wb', mtime=0) as compressed, tarfile.open(fileobj=compressed, mode='w') as tf:
        for path in [package]+sorted(package.rglob('*')):
            if path.name == '.DS_Store':
                continue
            info = tf.gettarinfo(str(path), 'package' if path == package else 'package/'+path.relative_to(package).as_posix())
            info.uid = info.gid = 0
            info.uname = info.gname = ''
            info.mtime = 0
            info.mode = 0o755 if path.is_dir() else 0o644
            if path.is_file():
                with path.open('rb') as payload:
                    tf.addfile(info, payload)
            else:
                tf.addfile(info)
    project = Path(tempfile.mkdtemp(prefix='clean-install-', dir=ROOT/'.private'))
    (project/'Packages').mkdir(); (project/'Assets/Editor').mkdir(parents=True); (project/'ProjectSettings').mkdir()
    manifest = json.loads((ROOT/'UnityProject/Packages/manifest.json').read_text())
    manifest['dependencies']['com.worldweaver.toolkit'] = args.git_url or 'file:'+str(archive)
    manifest['dependencies'].pop('com.unity.multiplayer.center', None)
    (project/'Packages/manifest.json').write_text(json.dumps(manifest, indent=2))
    shutil.copy(ROOT/'UnityProject/ProjectSettings/ProjectVersion.txt', project/'ProjectSettings/ProjectVersion.txt')
    shutil.copy(ROOT/'scripts/InstallAudit.cs', project/'Assets/Editor/InstallAudit.cs')
    report = ROOT/'results'/('clean-git-install.json' if args.git_url else 'clean-install.json')
    log = project/'audit.log'
    scratch_report = project/'result.json'
    env = os.environ.copy(); env['WW_AUDIT_OUTPUT'] = str(scratch_report)
    code = subprocess.call([args.unity, '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'InstallAudit.Run', '-logFile', str(log)], env=env)
    result = json.loads(scratch_report.read_text()) if scratch_report.exists() else {'success': False}
    result.update(archive='deliverables/'+archive.name, sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), batch_exit_code=code, source='git' if args.git_url else 'local-tarball', fresh_project=True, compiler_error_count=sum('error CS' in line for line in log.read_text(errors='replace').splitlines()))
    if args.git_url:
        result['git_url'] = args.git_url
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return code if code else (0 if result.get('success') else 1)


if __name__ == '__main__':
    raise SystemExit(main())
