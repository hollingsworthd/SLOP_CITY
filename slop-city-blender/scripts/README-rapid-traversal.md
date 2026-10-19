# Rapid traversal smoke

This test runs the saved city in a dedicated native Unity Editor, walking the
production character rapidly around the city. It does not test the live WebGL
release or reproduce iPhone Safari's memory and graphics environment. A pass is
not physical iPhone acceptance.

Run from the containing repository workspace, with Unity **6000.3.26f1** installed
at `.tools/Unity-6000.3.26f1/Unity.app/Contents/MacOS/Unity`, or supply `--unity`.
If an operator has separately started an owned local licensing helper, optional
`--licensing-ipc NAME` forwards that existing IPC name as Unity's `-licensingIpc`.
The runner does not start or alter licensing helpers or licenses.
For a nested game checkout, use the runner's actual relative path. For example:

```sh
python3 slop-city-blender/scripts/run_rapid_traversal_smoke.py \
  --project "$PWD/.local/validation/rapid-traversal/project"
```

The default only prints a plan. Add `--prepare` to copy and verify inputs without
starting Unity, or `--execute` to prepare and run. `--workspace` defaults to the
current directory, which must be a containing repository. `--project` must be
under that workspace's `.local` or `/private/tmp` and cannot contain symlinks.

The runner copies only `Assets`, `Packages`, and `ProjectSettings`, including all
`.meta` bytes. Before Unity can start it changes only the copied PlayerSettings
company/product to **SlopCityValidation / RapidTraversalSmoke**. This keeps saves
separate even if the test crashes. It never opens the source project in Unity.
It verifies source and copied inputs, rejects symlinks/hardlinked copied inputs,
and reuses a Library only when its marked project still matches the exact inputs.
Unity may remove pre-existing orphan `.meta` files during import. After the Editor
exits, the runner restores their exact captured bytes only if they were removed
and still have no target; changed metadata or newly created targets fail the check.
It prints the restored count and still requires the complete input inventory to match.
If inputs differ, choose a fresh project path; the runner never clears a cache.
Do not open the validation project manually or run another Unity process against
it while the runner is active.

Defaults are two laps requesting **55 m/s** (ten times the ordinary 5.5 m/s sprint), with a
480-second in-Editor limit. Use `--laps 1..10`, `--speed 5.5..110`, or
`--seconds 60..600` to adjust them. `--timeout` bounds the whole Editor process,
including cold import/compilation; its default is 2,280 seconds. The command uses
graphics-enabled `-batchmode`, `StandaloneOSX`, and the self-exiting
`SlopCity.Editor.SlopCityRapidTraversalSmokeTest.Run`; it omits `-quit` and
`-nographics`.

One declared setup teleport places the player on a street checkpoint. Each lap
then visits all nine districts and returns through adjacent districts using the
production walking controller. Movement pauses while the bounded route solver
plans a connecting segment; simulation time, crowd and native media stay enabled.
Commanded movement is capped at 0.75 metres per frame, so the achieved speed can
be lower than the request. The mean-speed calculation includes commanded frames
that stall and excludes route-planning pauses.

After a short stall, the fixture may request an ordinary production jump only
when a capsule sweep identifies a nearby low prop, the character is grounded,
and no door is involved. Collision and gravity stay enabled. Jump requests have
a 1.5-second cooldown, a four-per-segment limit, and a 64-per-run limit; each
request and its obstacle are recorded. A six-second movement stall still fails;
the fixture never teleports to recover a traversal.

Each attempt writes a fresh directory under `PROJECT/RapidTraversalRuns/`,
containing `command.json`, `Editor.log`, and `report.json` checkpoints. The runner
prints progress every 30 seconds. Success requires Editor exit zero, a successful
fresh report from the pinned Unity version, and unchanged source/copied inputs.
The fixture's pass gates include a native `VideoPlayer.isPlaying` observation on
commanded-movement frames during every lap, and measured mean moving speed of at
least **11 m/s** for every lap. Video frame/time snapshots and memory peaks are
recorded for inspection; the pass does not establish decoded frame advancement,
audibility, visibility of every screen, or a memory plateau.
Timeouts and failures retain logs and any partial report for inspection. An
interrupted preparation can leave a lock; inspect that process and project before
removing the exact lock named in the error.

Check the runner's isolation safeguards without launching Unity:

```sh
python3 slop-city-blender/scripts/test_rapid_traversal_runner.py
```
