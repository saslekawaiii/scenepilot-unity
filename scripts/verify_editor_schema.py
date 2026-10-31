#!/usr/bin/env python3
"""Check an exported WorldWeaver EditorCommand JSON Schema without running Unity.

Requires Python 3.9+ and jsonschema (``python3 -m pip install jsonschema``).
Example, from any working directory:
    python3 scripts/verify_editor_schema.py --schema results/editor-command-schema.json

Paths supplied on the command line are relative to the current working directory.
Defaults are relative to this script's repository. Source coverage is checked when
the local package sources exist; use --require-source-coverage to require them.
The verifier does not regenerate the export, compile C#, or call a model/provider.
Both singleton tool enums and string const branches are supported. With 1.2 sources,
shared helper schemas must be included by EditorToolSchema and reached by runtime
Validate dispatch; missing wiring or an unrecognized source representation fails.
An optional --generated-source-sha256 records the source hash captured at export;
when the local source is available, a mismatch fails the verification.
That single hash covers EditorToolSchema.cs, not all dependencies; helper source
fingerprints are reported separately and require an exporter/release binding.
Use --export-receipt for the C# export's generatorSources SHA-256 map; when local
sources exist, every required generator must be included and match its bytes.
Use --decoder-schema to also check the separately exported decoder variant. Only
removing maxLength >= 2000 from string nodes is allowed in this compatibility diff.
"""

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parent.parent
PACKAGE_RELATIVE = Path('Packages/com.worldweaver.toolkit')
SOURCE_RELATIVE = Path('Editor/Assistant/EditorToolSchema.cs')


def command(tool, args=None, target='', name='Test'):
    return dict(tool=tool, name=name, target=target, args={} if args is None else args)


def semantic_cases():
    """Positive and negative regressions, including failures seen in real inference."""
    return [
        ('numeric terrain', command('create_world', {'resolution': 129, 'scale': .8, 'width': 160}), True),
        ('reject string resolution', command('create_world', {'resolution': '129'}), False),
        ('reject string noise scale', command('create_world', {'scale': '.8'}), False),
        ('reject vector noise scale', command('create_world', {'scale': [1, 1, 1]}), False),
        ('vector transform', command('set_transform', {'scale': [1, .5, 1]}, 'id:1'), True),
        ('reject scalar transform', command('set_transform', {'scale': .8}, 'id:1'), False),
        ('reject empty transform', command('set_transform', {}, 'id:1'), False),
        ('reject terrain scale on NPC', command('update_npc', {'scale': [1, 1, 1]}, 'id:1'), False),
        ('reject decorations on update', command('update_world', {'trees': 12}, 'id:1'), False),
        ('valid scatter', command('scatter_objects', {'kind': 'tree', 'count': 12}, 'id:1'), True),
        ('scatter default count', command('scatter_objects', {'kind': 'rock'}, '@World'), True),
        ('reject scatter missing target', command('scatter_objects', {'kind': 'tree', 'count': 12}), False),
        ('reject scatter missing kind', command('scatter_objects', {'count': 12}, 'id:1'), False),
        ('reject unknown scatter arg', command('scatter_objects', {'kind': 'tree', 'color': '#ff9900'}, 'id:1'), False),
        ('patrol', command('attach_behavior', {'behavior': 'patrol', 'points': [[0, 0, 0], [1, 2, 3]]}, 'id:1'), True),
        ('reject patrol missing points', command('attach_behavior', {'behavior': 'patrol'}, 'id:1'), False),
        ('reject follow missing target', command('attach_behavior', {'behavior': 'followplayer'}, 'id:1'), False),
        ('follow', command('attach_behavior', {'behavior': 'followplayer', 'follow_target': 'selected'}, 'id:1'), True),
        ('RGB array', command('set_material', {'color': [1, .5, 0]}, 'id:1'), True),
        ('hex alpha', command('set_material', {'color': '#ff9900aa'}, 'id:1'), True),
        ('reject color unknown word', command('set_material', {'color': 'orange'}, 'id:1'), False),
        ('reject color out of bounds', command('set_material', {'color': [255, 128, 0]}, 'id:1'), False),
        ('reject material no color', command('set_material', {}, 'id:1'), False),
        ('light create', command('set_lighting', {'type': 'point'}, '', name='Lamp'), True),
        ('reject light missing name', command('set_lighting', {'type': 'point'}, '', name=''), False),
        ('light edit', command('set_lighting', {'range': 30}, 'id:3', name=''), True),
        ('NPC knowledge inventory', command('create_npc', {'knowledge': ['Факт'], 'inventory': {'coin': 3}, 'on_terrain': True}), True),
        ('reject inventory string', command('create_npc', {'inventory': {'coin': '3'}}), False),
        ('reject code missing', command('draft_script', {}), False),
        ('code arguments accepted', command('draft_script', {'code': 'using UnityEngine; public class Test:MonoBehaviour{}'}), True),
        ('reject unknown tool', command('invent_tool', {}), False),
        ('read path', command('read_script', {'path': 'Assets/Scripts/My.cs'}), True),
        ('reject bad read ext', command('read_script', {'path': 'Assets/Scripts/My.txt'}), False),
    ]


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fail_if(condition, message):
    if condition:
        raise ValueError(message)


def object_leaves(schema):
    """Traverse the standalone unions intentionally emitted by CommandSchema()."""
    if 'anyOf' in schema:
        fail_if(any(k in schema for k in ('type', 'properties', 'required')),
                'Union nodes must have self-contained branches, without object siblings.')
        return [leaf for branch in schema['anyOf'] for leaf in object_leaves(branch)]
    fail_if(schema.get('type') != 'object', 'Expected a typed argument object.')
    return [schema]


def argument_coverage(schema):
    fail_if(set(schema) != {'anyOf'}, 'Expected CommandSchema export: a standalone anyOf command union.')
    fail_if(not schema['anyOf'], 'Command union is empty.')
    actual = {}
    for branch in schema['anyOf']:
        fail_if(branch.get('type') != 'object' or branch.get('additionalProperties') is not False,
                'Every command branch must be a closed object.')
        properties = branch.get('properties', {})
        fail_if(set(properties) != {'tool', 'name', 'target', 'args'}
                or set(branch.get('required', [])) != set(properties),
                'Every command branch must require exactly tool/name/target/args.')
        tool_schema = properties['tool']
        fail_if(tool_schema.get('type') != 'string', 'Tool must be a string.')
        choices = tool_schema.get('enum')
        constant = tool_schema.get('const')
        if choices is not None:
            fail_if(not isinstance(choices, list) or len(choices) != 1
                    or not isinstance(choices[0], str),
                    'Tool enum must contain exactly one string.')
        fail_if('const' in tool_schema and not isinstance(constant, str), 'Tool const must be a string.')
        fail_if(choices is None and constant is None, 'Tool needs a singleton enum or const.')
        fail_if(choices is not None and constant is not None and choices[0] != constant,
                'Tool enum and const disagree.')
        for key in ('name', 'target'):
            fail_if(properties[key].get('type') != 'string', key + ' must be a string.')
        tool = constant if constant is not None else choices[0]
        fields = set()
        for args in object_leaves(properties['args']):
            fail_if(args.get('additionalProperties') is not False,
                    tool + ' args branch must reject unknown argument fields.')
            fields.update(args.get('properties', {}))
        if tool in actual:
            fail_if(fields != actual[tool], tool + ' command branches disagree on their argument fields.')
        actual[tool] = fields
    return actual


def compare_decoder(full, decoder, path=''):
    """Reject every decoder change except the locally reproduced long-string bound."""
    removed = []
    if isinstance(full, dict) and isinstance(decoder, dict):
        extra = set(decoder) - set(full)
        fail_if(bool(extra), 'Decoder adds schema keys at ' + path + ': ' + ','.join(sorted(extra)))
        for key, value in full.items():
            child_path = path + '/' + key.replace('~', '~0').replace('/', '~1')
            if key not in decoder:
                allowed = (key == 'maxLength' and full.get('type') == 'string'
                           and isinstance(value, int) and value >= 2000)
                fail_if(not allowed, 'Unexpected decoder constraint removal: ' + child_path)
                removed.append({'json_pointer': child_path, 'keyword': key, 'full_value': value,
                                'reason': 'Ollama 0.32.6 rejected the full nested long-string grammar; runtime still checks this bound.'})
            else:
                removed.extend(compare_decoder(value, decoder[key], child_path))
    elif isinstance(full, list) and isinstance(decoder, list):
        fail_if(len(full) != len(decoder), 'Decoder changes array length at ' + path)
        for index, value in enumerate(full):
            removed.extend(compare_decoder(value, decoder[index], path + '/' + str(index)))
    else:
        fail_if(type(full) is not type(decoder) or full != decoder, 'Decoder changes a schema value at ' + path)
    return removed


def verify_semantics(schema, validator_type):
    validator = validator_type(schema)
    results = []
    for label, value, expected in semantic_cases():
        actual_valid = validator.is_valid(value)
        case = {'name': label, 'input': value, 'expected_valid': expected,
                'actual_valid': actual_valid, 'passed': actual_valid == expected}
        if not case['passed']:
            case['validation_errors'] = [error.message for error in validator.iter_errors(value)]
        results.append(case)
    return results


CS_NON_CODE = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.S)


def masked_csharp(source):
    """Preserve offsets/braces in code, while excluding strings and comments."""
    return CS_NON_CODE.sub(lambda m: ''.join('\n' if c == '\n' else ' ' for c in m[0]), source)


def uncomment_csharp(source):
    """Commented-out validation calls are not evidence of runtime wiring."""
    return CS_NON_CODE.sub(lambda m: (''.join('\n' if c == '\n' else ' ' for c in m[0])
                                     if m[0].startswith(('//', '/*')) else m[0]), source)


def balanced_csharp(source, opening, left='{', right='}'):
    masked = masked_csharp(source)
    fail_if(opening >= len(source) or masked[opening] != left, 'Expected C# ' + left + ' boundary.')
    depth = 0
    for pos in range(opening, len(source)):
        if masked[pos] == left:
            depth += 1
        elif masked[pos] == right:
            depth -= 1
            if depth == 0:
                return source[opening:pos + 1]
    raise ValueError('Unterminated C# source boundary.')


def initializer_entries(block):
    """Read only direct ["key"] = value entries, never nested schema properties."""
    fail_if(not block.startswith('{') or not block.endswith('}'), 'Expected initializer block.')
    masked = masked_csharp(block)
    depth, depths = 0, []
    for char in masked:
        depths.append(depth)
        if char == '{':
            depth += 1
        elif char == '}':
            depth -= 1
    matches = [m for m in re.finditer(r'\[\s*"([A-Za-z_][A-Za-z_0-9]*)"\s*\]\s*=', block)
               if masked[m.start()] == '[' and depths[m.start()] == 1]
    result = {}
    for index, match in enumerate(matches):
        name = match[1]
        fail_if(name in result, 'Duplicate C# initializer key: ' + name)
        end = matches[index + 1].start() if index + 1 < len(matches) else len(block) - 1
        result[name] = block[match.end():end].strip().rstrip(',').strip()
    return result


def jobject_entries(expression):
    match = re.search(r'new\s+JObject\s*(?:\(\s*\))?\s*', expression)
    fail_if(match is None, 'Unrecognized JObject schema source.')
    if match.end() == len(expression) or expression[match.end()] != '{':
        fail_if(not expression[match.start():match.end()].strip().endswith('()'),
                'Unrecognized empty JObject source.')
        return {}
    return initializer_entries(balanced_csharp(expression, match.end()))


def method_block(source, signature):
    fail_if(signature not in source, 'Cannot locate C# method: ' + signature)
    start = source.index(signature) + len(signature)
    opening = masked_csharp(source).find('{', start)
    fail_if(opening < 0, 'Cannot locate method body: ' + signature)
    return balanced_csharp(source, opening)


def helper_coverage(source, helper, scene_validation, schema_source):
    """Helper allowlists are shared with Validate; assert both wiring directions.

    This is deliberately representation-specific, fail-closed source inspection,
    not a C# compiler or proof of all geometry/serialized-property validation.
    """
    body = method_block(source, 'IEnumerable<JObject> CommandSchemas()')
    dictionary = re.search(r'new\s+Dictionary\s*<\s*string\s*,\s*JObject\s*>\s*\{', body)
    fail_if(dictionary is None, 'Cannot locate shared schema dictionary for ' + helper)
    rows = initializer_entries(balanced_csharp(body, dictionary.end() - 1))
    fields = {tool: set(jobject_entries(value)) for tool, value in rows.items()}
    names = re.search(r'string\[\]\s+Names\s*=\s*\{([^}]+)\}', source)
    fail_if(names is None or set(re.findall(r'"([a-z_]+)"', names[1])) != set(fields),
            helper + '.Handles names disagree with its shared schema dictionary.')
    validate = method_block(source, 'void Validate(EditorCommand c,Func<string,GameObject> resolve)')
    fail_if(not re.search(r'CommandSchemas\(\)\s*\.First\(', validate)
            or not re.search(r'\["properties"\]\s*\["args"\]', validate)
            or 'c.args.Properties()' not in validate or '.Property(p.Name)==null' not in validate,
            helper + '.Validate no longer reads/rejects fields from its shared schema.')
    dispatch = re.search(r'if\s*\(\s*' + re.escape(helper) + r'\.Handles\(command\.tool\)\s*\)', scene_validation)
    fail_if(dispatch is None, helper + ' is not dispatched by SceneToolExecutor.Validate.')
    opening = masked_csharp(scene_validation).find('{', dispatch.end())
    dispatch_body = balanced_csharp(scene_validation, opening)
    fail_if(not re.search(re.escape(helper) + r'\.Validate\(\s*command\s*,', dispatch_body)
            or 'continue;' not in dispatch_body,
            helper + ' dispatch bypasses its runtime validator.')
    fail_if(not re.search(r'foreach\s*\([^)]*\bin\s+' + re.escape(helper)
                         + r'\.CommandSchemas\(\)\s*\)\s*commands\.Add\(', schema_source),
            'EditorToolSchema no longer includes ' + helper + '.CommandSchemas().')
    return fields


def runtime_coverage(package):
    """Read literal allowlists and shared helper schemas, without executing C#.

    If the validator source changes to a different representation, this parser
    deliberately fails so a skipped comparison cannot silently become a PASS.
    """
    paths = {
        'scene': package / 'Editor/Assistant/SceneToolExecutor.cs',
        'terrain': package / 'Runtime/Terrain/TerrainSpec.cs',
        'scripts': package / 'Editor/Assistant/ScriptDrafts.cs',
    }
    helper_paths = {
        'project': package / 'Editor/Assistant/AgentProjectTools.cs',
        'gameplay': package / 'Editor/Assistant/WorldGameplayTools.cs',
        'patches': package / 'Editor/Assistant/AgentSourcePatches.cs',
    }
    if any(path.exists() for path in helper_paths.values()):
        paths.update(helper_paths)
        paths['schema'] = package / SOURCE_RELATIVE
        paths['controller'] = package / 'Editor/Assistant/ChatController.cs'
    present = {name: path.exists() for name, path in paths.items()}
    if not any(present.values()):
        return None, {'status': 'SKIPPED', 'reason': 'Local runtime validator sources are unavailable.'}
    fail_if(not all(present.values()), 'Partial local sources; missing: ' + ', '.join(
        str(paths[name]) for name, exists in present.items() if not exists))
    source = {name: uncomment_csharp(path.read_text(encoding='utf-8')) for name, path in paths.items()}
    start = 'public static void Validate(EditorPlan plan)'
    fail_if(start not in source['scene'] or 'private static void NpcArgs' not in source['scene'],
            'Cannot locate SceneToolExecutor.Validate source boundaries.')
    validation = source['scene'].split(start, 1)[1].split('private static void NpcArgs', 1)[0]
    terrain_fields = set()
    for declaration in re.findall(r'public\s+(?:int|float)\s+([^;]+);', source['terrain']):
        terrain_fields.update(re.findall(r'(?:^|,)\s*(\w+)\s*=', declaration))
    fail_if(not terrain_fields, 'No TerrainSpec numeric fields found.')
    expected = {}
    for tool, body in re.findall(
            r'case "([a-z_]+)":(.*?)(?=\n\s*case |\n\s*default:)', validation, re.S):
        expression = re.search(r'Keys\(a(?:,([^;]+))?\);', body)
        fail_if(expression is None, 'No recognized runtime argument allowlist for ' + tool)
        args_expression = expression.group(1) or ''
        fields = set(re.findall(r'"([A-Za-z_]+)"', args_expression))
        if 'TerrainKeys' in args_expression:
            fields |= terrain_fields
        fail_if(tool in expected, 'Duplicate validator switch case: ' + tool)
        expected[tool] = fields
    fail_if(not expected, 'No scene-tool validators found.')
    script_start = 'public static void Validate(EditorCommand action)'
    script_end = 'public static List<EditorCommandResult> Apply'
    fail_if(script_start not in source['scripts'] or script_end not in source['scripts'],
            'Cannot locate ScriptDrafts.Validate source boundaries.')
    code_validation = source['scripts'].split(script_start, 1)[1].split(script_end, 1)[0]
    code_fields = set(re.findall(r'p\.Name\s*!=\s*"([A-Za-z_]+)"', code_validation))
    fail_if(not code_fields, 'Cannot extract draft_script argument allowlist.')
    fail_if(not re.search(r'if\s*\(action\.tool\s*==\s*"attach_script"\)\s*\{\s*'
                         r'if\s*\(action\.args\.Count\s*!=\s*0\)', code_validation),
            'Cannot verify attach_script empty-argument guard.')
    expected['draft_script'] = code_fields
    expected['attach_script'] = set()
    helper_wiring = []
    if 'project' in source:
        for key, helper in (('project', 'AgentProjectTools'), ('gameplay', 'WorldGameplayTools')):
            additions = helper_coverage(source[key], helper, validation, source['schema'])
            fail_if(bool(expected.keys() & additions.keys()), helper + ' shadows a previous tool.')
            expected.update(additions)
            helper_wiring.append({'helper': helper, 'passed': True,
                                  'proof': 'CommandSchema inclusion, Handles names, Validate shared schema lookup and SceneToolExecutor.Validate dispatch are present.'})
        patch_source = source['patches']
        patch_schema = jobject_entries(patch_source.split('JObject CommandSchema()=>', 1)[-1])
        fail_if('properties' not in patch_schema, 'Cannot locate patch_script CommandSchema properties.')
        patch_command = jobject_entries(patch_schema['properties'])
        patch_args = jobject_entries(patch_command.get('args', ''))
        patch_fields = set(jobject_entries(patch_args.get('properties', '')))
        patch_validation = method_block(patch_source, 'void Validate(EditorCommand command)')
        allowlist = re.search(r'command\.args\.Properties\(\)\.Any\(p\s*=>\s*!new\[\]\s*\{([^}]+)\}\.Contains\(p\.Name\)\)', patch_validation)
        fail_if(allowlist is None, 'Cannot extract patch_script runtime allowlist.')
        patch_runtime_fields = set(re.findall(r'"([A-Za-z_][A-Za-z_0-9]*)"', allowlist[1]))
        fail_if(patch_fields != patch_runtime_fields,
                'patch_script shared schema/runtime argument allowlists differ.')
        fail_if('commands.Add(AgentSourcePatches.CommandSchema())' not in source['schema']
                or not re.search(r'if\s*\(action\.tool\s*==\s*"patch_script"\)\s*AgentSourcePatches\.Validate\(action\)', source['controller']),
                'patch_script schema/ChatController runtime validation wiring is missing.')
        expected['patch_script'] = patch_runtime_fields
        helper_wiring.append({'helper': 'AgentSourcePatches', 'passed': True,
                              'proof': 'CommandSchema fields equal independent Validate allowlist; EditorToolSchema inclusion and ChatController validator call are present.'})
    return expected, {
        'status': 'CHECKED', 'method': 'Fail-closed read-only extraction of literal allowlists and helper shared schemas; helper inclusion/validator dispatch are asserted.',
        'source_files': {str(path.relative_to(package)): sha256(path) for path in paths.values()},
        'helper_runtime_validation_wiring': helper_wiring,
    }


def parse_hash(value):
    if not re.fullmatch('[0-9a-fA-F]{64}', value):
        raise argparse.ArgumentTypeError('Expected exactly 64 hexadecimal SHA-256 characters.')
    return value.lower()


def reject_constant(value):
    raise ValueError('Non-finite JSON number is forbidden: ' + value)


def reject_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        fail_if(key in result, 'Duplicate JSON property: ' + key)
        result[key] = value
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--schema', type=Path, default=REPOSITORY / 'results/editor-command-schema.json')
    parser.add_argument('--decoder-schema', type=Path,
                        help='Optional export of DecoderCommandSchema(), checked separately against the full schema.')
    parser.add_argument('--report', '--output', dest='report', type=Path,
                        default=REPOSITORY / 'results/editor-schema-verification.json')
    parser.add_argument('--source-root', type=Path, default=REPOSITORY,
                        help='Repository containing Packages/com.worldweaver.toolkit; defaults to the script repository.')
    parser.add_argument('--require-source-coverage', action='store_true')
    parser.add_argument('--generated-source-sha256', type=parse_hash,
                        help='Source SHA-256 recorded when this schema was generated; checked against local source if available.')
    parser.add_argument('--export-receipt', type=Path,
                        help='C# export JSON containing generatorSources hashes, optionally toolCount; bind all current helper generators to local bytes.')
    args = parser.parse_args()
    schema_path = args.schema.resolve()
    report_path = args.report.resolve()
    if schema_path == report_path:
        parser.error('--schema and --report must be different files.')
    if args.decoder_schema and args.decoder_schema.resolve() == report_path:
        parser.error('--decoder-schema and --report must be different files.')
    if args.export_receipt and args.export_receipt.resolve() == report_path:
        parser.error('--export-receipt and --report must be different files.')
    package = args.source_root.resolve() / PACKAGE_RELATIVE
    report = {
        'created_utc': datetime.now(timezone.utc).isoformat(), 'passed': False,
        'schema': str(schema_path), 'checks': [], 'semantic_cases': [],
        'generated_source_sha256': args.generated_source_sha256,
        'method': 'Read-only JSON Schema verification; no C# compilation, Unity execution or model inference.',
        'limitations': [
            'Does not prove that any inference backend enforces all schema keywords.',
            'Does not execute runtime scene, geometry, ownership, file or plan-order checks.',
            'A supplied generation-source hash is provenance supplied by the exporter, not regeneration by this script.',
            'The generation-source hash covers EditorToolSchema.cs only; helper source hashes are observations and must also be bound to the export by the release process.',
        ],
    }
    checks = report['checks']
    try:
        try:
            import jsonschema
        except ImportError as error:
            raise ValueError('Missing dependency jsonschema. Install with: python3 -m pip install jsonschema') from error
        report['schema_sha256'] = sha256(schema_path)
        report['schema_bytes'] = schema_path.stat().st_size
        schema = json.loads(schema_path.read_text(encoding='utf-8'), parse_constant=reject_constant,
                            object_pairs_hook=reject_duplicate_keys)
        jsonschema.Draft7Validator.check_schema(schema)
        checks.append({'name': 'valid_draft7_schema', 'passed': True})
        actual = argument_coverage(schema)
        report['unique_tools'] = len(actual)
        report['command_branches'] = len(schema['anyOf'])
        checks.append({'name': 'closed_command_and_argument_branches', 'passed': True})
        report['semantic_cases'] = verify_semantics(schema, jsonschema.Draft7Validator)
        report['semantic_total'] = len(report['semantic_cases'])
        report['semantic_passed'] = sum(case['passed'] for case in report['semantic_cases'])
        checks.append({'name': 'semantic_regressions', 'passed': report['semantic_total'] == report['semantic_passed']})
        if args.decoder_schema:
            decoder_path = args.decoder_schema.resolve()
            decoder = json.loads(decoder_path.read_text(encoding='utf-8'), parse_constant=reject_constant,
                                 object_pairs_hook=reject_duplicate_keys)
            jsonschema.Draft7Validator.check_schema(decoder)
            removed = compare_decoder(schema, decoder)
            decoder_fields = argument_coverage(decoder)
            fail_if(decoder_fields != actual, 'Decoder changes tool/argument allowlists.')
            decoder_cases = verify_semantics(decoder, jsonschema.Draft7Validator)
            decoder_passed = sum(case['passed'] for case in decoder_cases)
            report['decoder'] = {
                'schema': str(decoder_path), 'schema_sha256': sha256(decoder_path),
                'schema_bytes': decoder_path.stat().st_size,
                'semantic_total': len(decoder_cases), 'semantic_passed': decoder_passed,
                'semantic_cases': decoder_cases, 'removed_constraints': removed,
                'removed_keywords': sorted({entry['keyword'] for entry in removed}),
                'preserved': 'All other schema nodes are byte-value equivalent after JSON parsing, including types, enums, required fields and closed argument objects.',
                'primary_sources': [
                    'https://docs.ollama.com/capabilities/structured-outputs',
                    'https://github.com/ggml-org/llama.cpp/issues/25746',
                    'https://github.com/ggml-org/llama.cpp/blob/505b1ed15/common/json-schema-to-grammar.cpp#L921-L925',
                    'https://github.com/ggml-org/llama.cpp/blob/505b1ed15/src/llama-grammar.cpp#L453-L461',
                    'https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md#json-schemas--gbnf',
                ],
                'note': 'This structural check does not itself call Ollama or prove backend enforcement; actual probes are recorded separately.',
            }
            checks.append({'name': 'decoder_compatibility_diff', 'passed': True})
            checks.append({'name': 'decoder_semantic_regressions', 'passed': decoder_passed == len(decoder_cases)})
        expected, coverage = runtime_coverage(package)
        report['runtime_argument_coverage'] = coverage
        if expected is None:
            checks.append({'name': 'runtime_argument_coverage', 'passed': not args.require_source_coverage,
                           'skipped': True})
        else:
            differences = {
                tool: {'schema_fields': sorted(actual.get(tool, set())),
                       'runtime_fields': sorted(expected.get(tool, set())),
                       'missing_from_schema': sorted(expected.get(tool, set()) - actual.get(tool, set())),
                       'extra_in_schema': sorted(actual.get(tool, set()) - expected.get(tool, set()))}
                for tool in actual.keys() | expected.keys() if actual.get(tool) != expected.get(tool)
            }
            coverage.update(status='PASS' if not differences else 'FAIL', differences=differences,
                            tools={tool: sorted(fields) for tool, fields in sorted(expected.items())})
            checks.append({'name': 'runtime_argument_coverage', 'passed': not differences})
        source_path = package / SOURCE_RELATIVE
        local_source_hash = sha256(source_path) if source_path.is_file() else None
        report['local_schema_source_sha256'] = local_source_hash
        if args.generated_source_sha256 is not None and local_source_hash is not None:
            checks.append({'name': 'generation_source_matches_local',
                           'passed': local_source_hash == args.generated_source_sha256})
            report['generation_source_verification'] = 'Compared supplied generation hash with local source bytes.'
        else:
            report['generation_source_verification'] = 'Unverified: generation hash or local schema source unavailable.'
        if args.export_receipt:
            receipt_path = args.export_receipt.resolve()
            receipt = json.loads(receipt_path.read_text(encoding='utf-8'), parse_constant=reject_constant,
                                 object_pairs_hook=reject_duplicate_keys)
            generators = receipt.get('generatorSources')
            fail_if(not isinstance(generators, dict) or not generators, 'Export receipt needs a nonempty generatorSources map.')
            required = {SOURCE_RELATIVE.name}
            required.update(path.name for path in (package / 'Editor/Assistant').glob('*.cs')
                            if path.name in {'WorldGameplayTools.cs', 'AgentProjectTools.cs', 'AgentSourcePatches.cs'})
            fail_if(not required.issubset(generators), 'Export receipt omits current generators: ' + ','.join(sorted(required - set(generators))))
            bindings = {}
            for name, exported_hash in generators.items():
                fail_if(not re.fullmatch(r'[A-Za-z_][A-Za-z_0-9]*\.cs', name)
                        or not isinstance(exported_hash, str) or not re.fullmatch('[a-f0-9]{64}', exported_hash),
                        'Invalid generator filename/hash in export receipt.')
                generator = package / 'Editor/Assistant' / name
                observed = sha256(generator) if generator.is_file() else None
                bindings[name] = {'exported_sha256': exported_hash, 'local_sha256': observed,
                                  'matches': observed == exported_hash if observed is not None else None}
            source_available = local_source_hash is not None
            binding_passed = (all(row['matches'] is True for row in bindings.values()) if source_available
                              else not args.require_source_coverage)
            declared_count = receipt.get('toolCount')
            fail_if(declared_count is not None and (type(declared_count) is not int or declared_count != len(actual)),
                    'Export receipt toolCount differs from the actual schema.')
            report['export_receipt'] = {'path': str(receipt_path), 'sha256': sha256(receipt_path),
                                        'generator_sources': bindings, 'source_binding_passed': binding_passed,
                                        'tool_count': declared_count, 'unity': receipt.get('unity'),
                                        'package_version': receipt.get('packageVersion'),
                                        'note': 'Hashes bind supplied receipt metadata to observed source bytes; this verifier does not regenerate or authenticate the C# export.'}
            checks.append({'name': 'export_generator_source_binding', 'passed': binding_passed,
                           'skipped': not source_available})
        report['passed'] = all(check['passed'] for check in checks)
    except Exception as error:
        report['error'] = type(error).__name__ + ': ' + str(error)
        report['passed'] = False
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({
        'passed': report['passed'], 'semantic_passed': report.get('semantic_passed', 0),
        'semantic_total': report.get('semantic_total', 0), 'report': str(report_path),
        'error': report.get('error'),
    }, ensure_ascii=False))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
