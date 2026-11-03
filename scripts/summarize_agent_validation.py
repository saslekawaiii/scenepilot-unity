"""Export compact test evidence without model prompts, opaque state or credentials."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def evidence(path):
    p = ROOT / path
    return {'path': path, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}


def tests(path):
    node = ET.parse(ROOT / path).getroot()
    attrs = node.attrib
    return {**evidence(path), **{k: int(attrs.get(k, 0)) for k in ('total', 'passed', 'failed', 'skipped', 'inconclusive')}, 'result': attrs.get('result')}


def load(path):
    return json.loads((ROOT / path).read_text())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('proof', 'compound-proof', 'editor', 'play', 'regressions', 'compound', 'native', 'input', 'python', 'output'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument("--acceptance-review")
    args = parser.parse_args()
    proof = load(args.proof)
    compound_proof = load(args.compound_proof)
    candidate_files = {x['path']: x['sha256'] for x in proof['files']}
    compound_files = {x['path']: x['sha256'] for x in compound_proof['files']}
    differences = sorted(p for p in set(candidate_files) | set(compound_files) if candidate_files.get(p) != compound_files.get(p))
    assert not any(not p.startswith('Tests/') for p in differences), 'Live compound runtime differs from release runtime'
    regression = load(args.regressions)
    compound = load(args.compound)
    native_outer = load(args.native)
    native = native_outer.get('audit', native_outer)
    acceptance_review = load(args.acceptance_review) if args.acceptance_review else None
    if acceptance_review:
        assert acceptance_review['rawReportSha256'] == evidence(args.compound)['sha256']
        assert acceptance_review['sceneRepairByHarness'] is False

    answer = next((m for m in reversed(compound.get('messages', [])) if m.get('Role') == 'assistant'), {})
    data = {
        'version': '1.7.0', 'status': 'preview', 'sourceCommit': proof['sourceCommit'],
        'package': {k: proof[k] for k in ('archive', 'sha256', 'fileCount', 'treeSha256')},
        'editorTests': tests(args.editor), 'playTests': tests(args.play), 'savedSceneInputTests': tests(args.input),
        'pythonTests': {**evidence(args.python), **load(args.python)},
        'liveRegressions': {**evidence(args.regressions), 'completed': regression.get('completed'), 'passed': regression.get('passed'),
            'cases': [{'name': row.get('case'), 'passed': row.get('passed'), 'checks': row.get('checks')} for row in regression.get('cases', [])]},
        'originalCompoundRequest': {**evidence(args.compound), 'completed': compound.get('completed'), 'passed': acceptance_review['passed'] if acceptance_review else compound.get('passed'), 'checks': acceptance_review['checks'] if acceptance_review else compound.get('checks'), 'originalRawPassed': compound.get('passed'), 'acceptanceReview': acceptance_review,
            'sourceCommit': compound_proof['sourceCommit'], 'releaseRuntimeAndEditorByteIdentical': True, 'onlyPackageDifferences': differences,
            'providerInjected': compound.get('providerInjected'), 'sceneRepairByHarness': compound.get('sceneRepairByHarness'), 'paidApiUsed': compound.get('paidApiUsed'),
            'modelCalls': len(answer.get('Attempts', [])), 'toolResults': len(answer.get('Steps', [])), 'status': answer.get('Status')},
        'nativeInstallation': {**evidence(args.native), **{k: native.get(k) for k in ('passed', 'version', 'verifiedFiles', 'differentFiles', 'skills', 'recipes', 'sceneMutations', 'uiVisuallyVerified', 'sourceCommit')}},
        'limits': ['A bounded tool agent, not a trained replacement for every developer task.', 'Live services and model output are variable; free services can be unavailable.',
            'A global planetary DEM fitted to a small Unity Terrain is not a geographically scaled reconstruction.', 'Procedural humanoid locomotion is used when compatible authored clips are unavailable.',
            'This validation contains no new interactive desktop UI inspection.']
    }
    # The raw files stay in the audit workspace. Only this compact export belongs in releases.
    output = ROOT / args.output
    output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'output': args.output, 'editorPassed': data['editorTests']['passed'], 'regressionCases': len(data['liveRegressions']['cases']), 'compoundPassed': data['originalCompoundRequest']['passed']}))


if __name__ == '__main__':
    main()
