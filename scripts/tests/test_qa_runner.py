"""Exercise the QA process boundary without starting Unity or creating a project."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('qa_runner', Path(__file__).parents[1] / 'run_unity_qa.py')
QA = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(QA)


class ProcessBoundaryTests(unittest.TestCase):
    def run_fixture(self, *, ignore_terminate=False, report_progress=False):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder).resolve()
            clock = [0.0]
            progress = root / 'acceptance.json'

            class Child:
                pid = 424242
                code = None
                terminations = 0
                kills = 0

                def poll(self):
                    if report_progress and clock[0] >= 130 and self.code is None:
                        self.code = 0
                    return self.code

                def terminate(self):
                    self.terminations += 1
                    if not ignore_terminate:
                        self.code = -15

                def kill(self):
                    self.kills += 1
                    self.code = -9

                def wait(self, **_kwargs):
                    return self.code

            child = Child()

            def sleep(seconds):
                clock[0] += seconds
                if report_progress and int(clock[0]) % 25 == 0:
                    progress.write_text(json.dumps({'frame': clock[0]}))

            # Directory checks describe an existing consumer; no Assets or project
            # folders are created. Popen is the only process boundary and is mocked.
            with patch.object(QA, 'ROOT', root), patch.object(QA, 'PRIVATE', root / '.private'), \
                 patch.object(QA, 'UNITY', Path(sys.executable)), \
                 patch.object(QA, 'PROJECTS', {'tools': root / 'existing-consumer'}), \
                 patch.object(Path, 'is_dir', return_value=True), \
                 patch.object(QA, 'active_qa_editors', return_value=[]), \
                 patch.object(QA, 'pressure', return_value=None), \
                 patch.object(QA.shutil, 'disk_usage', return_value=shutil._ntuple_diskusage(40 * QA.GIB, 10 * QA.GIB, 30 * QA.GIB)), \
                 patch.object(QA.time, 'monotonic', side_effect=lambda: clock[0]), \
                 patch.object(QA.time, 'sleep', side_effect=sleep), \
                 patch.object(QA.signal, 'signal'), \
                 patch.object(QA.subprocess, 'Popen', return_value=child) as spawn, \
                 patch.dict(os.environ, {'WW_REGRESSION_OUTPUT': str(progress), 'WW_RUNTIME_PROBE_OUTPUT': ''}), \
                 patch.object(sys, 'argv', ['run_unity_qa.py', '--project', 'tools', '--log', str(root / 'editor.log'), '--inactivity-seconds', '60']), \
                 contextlib.redirect_stdout(io.StringIO()):
                code = QA.main()
            record = json.loads((root / '.private/unity-qa-runs.jsonl').read_text())
            self.assertEqual(spawn.call_count, 1)
            self.assertFalse(record['newProjectCreated'])
            self.assertFalse(record['libraryCloned'])
            return code, child, record, clock[0]

    def test_inactive_owned_child_is_terminated(self):
        code, child, record, _ = self.run_fixture()
        self.assertEqual(code, 143)
        self.assertEqual(child.terminations, 1)
        self.assertEqual(child.kills, 0)
        self.assertIn('60 seconds', record['resourceStopReason'])

    def test_unresponsive_owned_child_is_killed_after_grace(self):
        code, child, record, elapsed = self.run_fixture(ignore_terminate=True)
        self.assertEqual(code, 137)
        self.assertEqual(child.terminations, 1)
        self.assertEqual(child.kills, 1)
        self.assertGreaterEqual(elapsed, 75)
        self.assertLess(elapsed, 77)
        self.assertEqual(record['pid'], child.pid)

    def test_acceptance_progress_keeps_static_editor_log_alive(self):
        code, child, record, elapsed = self.run_fixture(report_progress=True)
        self.assertEqual(code, 0)
        self.assertGreaterEqual(elapsed, 130)
        self.assertEqual(child.terminations + child.kills, 0)
        self.assertIsNone(record['resourceStopReason'])


if __name__ == '__main__':
    unittest.main()
