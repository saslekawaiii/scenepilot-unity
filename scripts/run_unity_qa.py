#!/usr/bin/env python3
"""Run one owned Unity QA Editor with disk and memory pressure guards.

Reuses two existing consumers. Never creates projects, clones Library, stops a
foreign Editor, prints process arguments, or deletes checkpoint history.
"""
import argparse
import datetime as dt
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
PRIVATE = ROOT / '.private'
UNITY = Path('/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity')
PROJECTS = {
    'tools': PRIVATE / 'runtime-probe-qa',
    'agent': PRIVATE / 'warehouse-consumer-1.8-rc11',
}
GIB = 1024 ** 3


def active_qa_editors():
    result = []
    for row in subprocess.check_output(['ps', '-axo', 'pid=,args='], text=True).splitlines():
        fields = row.strip().split(None, 1)
        if len(fields) != 2 or not re.match(r'^/Applications/Unity/Hub/Editor/[^/]+/Unity\.app/Contents/MacOS/Unity(?:\s|$)', fields[1]):
            continue
        match = re.search(r'-projectPath\s+(.+?)(?:\s+-|$)', fields[1])
        if match:
            project = Path(match.group(1)).resolve()
            if PRIVATE in project.parents:
                result.append({'pid': int(fields[0]), 'project': str(project.relative_to(ROOT))})
    return result


def pressure():
    if sys.platform != 'darwin':
        return None
    try:
        return int(subprocess.check_output(['sysctl', '-n', 'kern.memorystatus_vm_pressure_level'], text=True, stderr=subprocess.DEVNULL).strip())
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', choices=PROJECTS, required=True)
    parser.add_argument('--log', type=Path, required=True)
    parser.add_argument('--graphics', action='store_true')
    parser.add_argument('--inactivity-seconds', type=int, default=300,
                        help='Stop only this owned Editor after this many seconds without log or acceptance-report activity.')
    parser.add_argument('unity_args', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if not 60 <= args.inactivity_seconds <= 3600:
        parser.error('Inactivity limit must be between 60 and 3600 seconds.')
    extra = args.unity_args[1:] if args.unity_args[:1] == ['--'] else args.unity_args
    if any(flag in extra for flag in ['-projectPath', '-batchmode', '-logFile', '-nographics']):
        parser.error('Project, mode and log are controlled by this runner.')
    project = PROJECTS[args.project]
    if not UNITY.is_file() or not (project / 'Assets').is_dir() or not (project / 'ProjectSettings').is_dir():
        parser.error('The configured Editor and existing consumer must already exist.')
    log = args.log.resolve()
    if ROOT not in log.parents or log.exists():
        parser.error('Use a fresh log path inside this workspace.')
    if '-testResults' in extra:
        report = Path(extra[extra.index('-testResults') + 1]).resolve()
        if ROOT not in report.parents or report.exists():
            parser.error('Use a fresh test report inside this workspace.')
    PRIVATE.mkdir(exist_ok=True)
    with (PRIVATE / 'unity-qa.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            parser.error('Another owned QA run holds the lock. No Editor started.')
        active = active_qa_editors()
        if active:
            parser.error('An owned QA Editor is still active: ' + json.dumps(active, ensure_ascii=False))
        free_before = shutil.disk_usage(ROOT).free
        if free_before < 20 * GIB or pressure() == 4:
            parser.error('Need at least 20 GiB free and no critical memory pressure. No Editor started.')
        log.parent.mkdir(parents=True, exist_ok=True)
        command = [str(UNITY), '-batchmode', '-projectPath', str(project), '-logFile', str(log)]
        if not args.graphics:
            command.append('-nographics')
        command += extra
        started = dt.datetime.now(dt.timezone.utc).isoformat()
        child = subprocess.Popen(command, cwd=ROOT, env=os.environ.copy(), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print(json.dumps({'event': 'started', 'role': args.project, 'pid': child.pid, 'freeGiB': round(free_before / GIB, 2)}, ensure_ascii=False), flush=True)
        stop_reason = None
        critical_since = None
        stopping_since = None
        watched = [log]
        for name in ('WW_REGRESSION_OUTPUT', 'WW_RUNTIME_PROBE_OUTPUT'):
            value = os.environ.get(name)
            if value:
                path = Path(value).resolve()
                if ROOT in path.parents:
                    watched.append(path)
        last_activity = time.monotonic()
        last_signature = None
        def forward(sig, _frame):
            nonlocal stop_reason, stopping_since
            stop_reason = 'QA runner interrupted'
            stopping_since = stopping_since or time.monotonic()
            if child.poll() is None:
                child.send_signal(sig)
        signal.signal(signal.SIGINT, forward)
        signal.signal(signal.SIGTERM, forward)
        while child.poll() is None:
            signature = []
            for path in watched:
                try:
                    stat = path.stat()
                    signature.append((stat.st_size, stat.st_mtime_ns))
                except FileNotFoundError:
                    signature.append(None)
            if signature != last_signature:
                last_signature, last_activity = signature, time.monotonic()
            if stopping_since is not None and time.monotonic() - stopping_since >= 15:
                child.kill()
                break
            if stopping_since is None and time.monotonic() - last_activity >= args.inactivity_seconds:
                stop_reason = 'No log or acceptance-report activity for ' + str(args.inactivity_seconds) + ' seconds'
                stopping_since = time.monotonic()
                child.terminate()
            free = shutil.disk_usage(ROOT).free
            level = pressure()
            critical_since = (critical_since or time.monotonic()) if level == 4 else None
            if free < 10 * GIB or critical_since is not None and time.monotonic() - critical_since >= 10:
                stop_reason = 'Free disk below 10 GiB' if free < 10 * GIB else 'Critical system memory pressure for 10 seconds'
                child.terminate()
                try:
                    child.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    child.kill()
                break
            time.sleep(1)
        code = child.wait()
        record = {'startedUtc': started, 'finishedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                  'role': args.project, 'project': str(project.relative_to(ROOT)), 'pid': child.pid,
                  'freeBeforeBytes': free_before, 'freeAfterBytes': shutil.disk_usage(ROOT).free,
                  'exitCode': code, 'resourceStopReason': stop_reason, 'log': str(log.relative_to(ROOT)),
                  'inactivityLimitSeconds': args.inactivity_seconds,
                  'newProjectCreated': False, 'libraryCloned': False}
        with (PRIVATE / 'unity-qa-runs.jsonl').open('a') as output:
            output.write(json.dumps(record, ensure_ascii=False) + '\n')
        print(json.dumps({'event': 'finished', 'exitCode': code, 'resourceStopReason': stop_reason,
                          'freeGiB': round(record['freeAfterBytes'] / GIB, 2)}, ensure_ascii=False), flush=True)
        return code if code >= 0 else 128 - code


if __name__ == '__main__':
    raise SystemExit(main())
