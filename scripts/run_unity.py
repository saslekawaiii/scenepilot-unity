"""Repeatable Unity entry points; credentials never appear in arguments or output."""
import argparse
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_UNITY = '/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity'


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['edit', 'play', 'real', 'evaluate', 'demo'])
    p.add_argument('--unity', default=DEFAULT_UNITY)
    p.add_argument('--project', type=Path, default=ROOT/'UnityProject')
    p.add_argument('--provider', choices=['local', 'cloud'], default='local')
    p.add_argument('--env-file', type=Path, help='Optional external .env; never committed')
    a = p.parse_args()
    env = os.environ.copy()
    if a.env_file:
        for line in a.env_file.read_text().splitlines():
            line = line.strip()
            if not line or line.startswith('#') or '=' not in line:
                continue
            name, value = line.split('=', 1)
            if name.strip() in {'OPENAI_API_KEY', 'GROQ_API_KEY', 'LLM_MODEL'}:
                env[name.strip()] = value.strip().strip('\"\'')
    args = [a.unity, '-projectPath', str(a.project.resolve()), '-logFile', str(ROOT/'results'/('unity-'+a.mode+'.log'))]
    if a.mode != 'demo':
        args += ['-batchmode', '-nographics']
    if a.mode in ['edit', 'play', 'real']:
        args += ['-runTests', '-testPlatform', 'EditMode' if a.mode == 'edit' else 'PlayMode', '-testResults', str(ROOT/'results'/(a.mode+'-rerun.xml'))]
        if a.mode == 'real':
            env['WW_REAL_TESTS'] = '1'
            args += ['-testFilter', 'WorldWeaver.Tests.RealProviderIntegration']
        elif a.mode == 'play':
            args += ['-testFilter', 'WorldWeaver.Tests.NavigationTests;WorldWeaver.Tests.AssistantBehaviorTests']
    elif a.mode == 'evaluate':
        env['WW_EVAL_PROVIDER'] = a.provider
        args += ['-executeMethod', 'WorldWeaver.Editor.ResearchRunner.Run']
    else:
        args += ['-executeMethod', 'WorldWeaver.Editor.DemoBuilder.Create']
    raise SystemExit(subprocess.call(args, env=env))


if __name__ == '__main__':
    main()
