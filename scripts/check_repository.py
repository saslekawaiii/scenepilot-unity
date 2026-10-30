"""Offline repository invariants and secret-pattern scan; prints locations, never values."""
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATTERNS = [re.compile(rb'gh[pousr]_[A-Za-z0-9]{30,}'), re.compile(rb'github_pat_[A-Za-z0-9_]{40,}'), re.compile(rb'sk-[A-Za-z0-9_-]{32,}'), re.compile(rb'gsk_[A-Za-z0-9]{30,}'), re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')]


def main():
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    issues = []
    checked = 0
    for name in sorted(set(filter(None, names))):
        p = ROOT/name
        if not p.is_file():
            continue
        if p.name == '.env' or (p.name.startswith('.env.') and p.name != '.env.example'):
            issues.append((name, 'environment file included'))
        if p.suffix.lower() in {'.json', '.asmdef'}:
            try:
                json.loads(p.read_text())
            except (ValueError, UnicodeError):
                issues.append((name, 'invalid JSON'))
        data = p.read_bytes()
        if any(pattern.search(data) for pattern in PATTERNS):
            issues.append((name, 'credential pattern'))
        if p.suffix == '.cs' and re.search(rb'\b(?:TODO|FIXME|NotImplementedException)\b', data):
            issues.append((name, 'unfinished implementation marker'))
        checked += 1
    package = json.loads((ROOT/'Packages/com.worldweaver.toolkit/package.json').read_text())
    for sample in package.get('samples', []):
        if not (ROOT/'Packages/com.worldweaver.toolkit'/sample['path']).is_dir():
            issues.append((sample['path'], 'sample directory missing'))
    for name in ['Runtime', 'Editor', 'Tests', 'Documentation~']:
        if not (ROOT/'Packages/com.worldweaver.toolkit'/name).is_dir():
            issues.append((name, 'package directory missing'))
    for path, reason in issues:
        print(path+': '+reason)
    print(f'Checked {checked} files; issues={len(issues)}')
    return 1 if issues else 0


if __name__ == '__main__':
    raise SystemExit(main())
