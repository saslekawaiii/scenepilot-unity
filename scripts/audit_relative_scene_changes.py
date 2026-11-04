"""Audit captured scene identities and properties without trusting model claims."""
import argparse
import json
import math
from pathlib import Path


def audit(row):
    before = {r['target']: r for r in row['beforeInventory']}
    after = {r['target']: r for r in row['afterInventory']}
    candidates = [r for r in before.values() if r['name'].lower() == 'ramp']
    if len(candidates) != 1:
        raise ValueError('Expected exactly one observed ramp for this acceptance request')
    ramp = candidates[0]
    identity = ramp['target']
    current = after.get(identity)
    if current is None:
        raise ValueError('The existing ramp was removed')
    ratios = [v / u for u, v in zip(ramp['localScale'], current['localScale'])]
    yaw = (current['rotation'][1] - ramp['rotation'][1]) % 360
    close = lambda x, y: math.isclose(x, y, rel_tol=1e-5, abs_tol=1e-4)
    same_ids = set(before) == set(after)
    other_nodes = same_ids and all(before[k] == after[k] for k in before if k != identity)
    allowed = {'localScale'}
    if row['case'] == 'modify_physics':
        allowed.add('rotation')
        # The original request does not name a local horizontal width axis.
        # Permit horizontal enlargement, preserve thickness; separately test X-only.
        scale_ok = close(ratios[1], 1) and all(close(r, 1) or close(r, 1.5) for r in (ratios[0], ratios[2])) and any(close(r, 1.5) for r in (ratios[0], ratios[2]))
        rotation_ok = close(yaw, 90) and all(close(ramp['rotation'][i], current['rotation'][i]) for i in (0, 2))
    elif row['case'] == 'modify_width_only':
        scale_ok = all(close(r, expected) for r, expected in zip(ratios, (1.2, 1, 1)))
        rotation_ok = ramp['rotation'] == current['rotation']
    else:
        raise ValueError('Unsupported relative-edit audit case: ' + row['case'])
    other_ramp = all(ramp[k] == current[k] for k in ramp if k not in allowed)
    return {'sameObjectIdentities': same_ids, 'addedObjects': [after[k]['name'] for k in sorted(after.keys()-before.keys())],
            'removedObjects': [before[k]['name'] for k in sorted(before.keys()-after.keys())],
            'allOtherCapturedObjectPropertiesUnchanged': other_nodes, 'otherRampPropertiesUnchanged': other_ramp,
            'rampBefore': ramp, 'rampAfter': current, 'observedScaleRatios': ratios, 'observedYawDelta': yaw,
            'requestedScaleSatisfied': scale_ok, 'requestedRotationSatisfied': rotation_ok,
            'passed': same_ids and other_nodes and other_ramp and scale_ok and rotation_ok}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Preserve historical evidence: ' + str(args.output))
    raw = json.loads(args.input.read_text())
    checks = {r['case']: audit(r) for r in raw['cases'] if r['case'] in ('modify_physics', 'modify_width_only')}
    if not checks:
        raise ValueError('No relative-edit observations')
    report = {'source': str(args.input), 'packageSourceCommit': raw.get('packageSourceCommit'),
              'archiveSha256': raw.get('packageArchiveSha256'), 'independentMetadataAudit': True,
              'scope': 'Observed object identities, transforms, masses, colors and component types; no gameplay repair',
              'checks': checks, 'allAuditedChecksPassed': all(c['passed'] for c in checks.values())}
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps({'passed': report['allAuditedChecksPassed'], 'cases': {k: c['passed'] for k, c in checks.items()}}))


if __name__ == '__main__':
    main()
