# Resource-conscious Unity checks

Run one owned QA Editor at a time and reuse existing test consumers. Keep generated Unity projects, `Library` caches, local task histories and raw model reports out of Git.

For a normal checkout, start with the portable [contributor test instructions](../CONTRIBUTING.md#verify-the-behavior-you-change). The maintainer runner below uses preconfigured consumers that are not included in the repository.

## Maintainer runner

[`scripts/run_unity_qa.py`](../scripts/run_unity_qa.py) runs an existing configured `tools` or `agent` consumer. It does not create a project or clone a Library. Use a new log and test-result path for each run:

```sh
python3 scripts/run_unity_qa.py --project tools --log results/qa-run.log -- \
  -runTests -testPlatform EditMode -testResults results/qa-run.xml
```

The runner requires at least 20 GiB free to start. It refuses a concurrent owned QA run and stops only its own child Editor if free disk falls below 10 GiB or critical memory pressure persists for 10 seconds. It never stops the user's Editor or unrelated applications.

The default inactivity limit is 300 seconds without a changing log or acceptance report; it can be set to 60–3600 seconds. A requested stop receives a 15-second grace period. For render-dependent checks, use `--graphics`.

## Checkpoint storage

Task checkpoints share identical content under `UserSettings/WorldWeaverCheckpoints/shared-content`, addressed by SHA-256. Existing per-task storage remains readable for compatibility. Integrity and conflicting-manual-edit checks still apply.

Cleanup removes only unreferenced checkpoint data. Keep recovery journals with their referenced content; do not delete live checkpoints to make a test pass or to free disk space. See [architecture](architecture.md) and [maintaining releases](MAINTAINING.md).
