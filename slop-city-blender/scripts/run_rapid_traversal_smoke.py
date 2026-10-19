#!/usr/bin/env python3
"""Prepare an independent Unity project and optionally run the native Editor smoke."""

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid


SOURCE = Path(__file__).resolve().parents[1] / "Unity"
INPUTS = ("Assets", "Packages", "ProjectSettings")
SETTINGS = "ProjectSettings/ProjectSettings.asset"
MARKER = ".rapid-traversal-project.json"
VERSION = "6000.3.26f1"
LIMITATION = "Native Unity Editor test; this does not test live WebGL or establish iPhone stability."


def isolated_settings(data):
    for field, value in ((b"companyName", b"SlopCityValidation"),
                         (b"productName", b"RapidTraversalSmoke")):
        data, count = re.subn(rb"(?m)^(  " + field + rb":)[^\r\n]*",
                              rb"\1 " + value, data)
        if count != 1:
            raise ValueError("Expected exactly one PlayerSettings " + field.decode())
    return data


def inventory(project, isolate=False):
    """Hash every input, including meta files; reject links/shared writable files."""
    result = {}
    for name in INPUTS:
        root = project / name
        if root.is_symlink() or not root.is_dir():
            raise ValueError(f"Missing or linked input directory: {root}")
        for directory, dirs, files in os.walk(root, followlinks=False):
            for item in dirs + files:
                path = Path(directory) / item
                if path.is_symlink():
                    raise ValueError(f"Linked Unity input: {path}")
            for item in sorted(files):
                path = Path(directory) / item
                if not path.is_file():
                    raise ValueError(f"Non-regular Unity input: {path}")
                if project != SOURCE and path.stat().st_nlink != 1:
                    raise ValueError(f"Shared Unity input: {path}")
                relative = path.relative_to(project).as_posix()
                data = path.read_bytes()
                if isolate and relative == SETTINGS:
                    data = isolated_settings(data)
                result[relative] = hashlib.sha256(data).hexdigest()
    return result


def digest(values):
    return hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest()


def safe_project(path, workspace):
    # Only repository workspaces containing this script may supply a local area.
    repositories = {p for p in SOURCE.parents if (p / ".git").exists()}
    workspace = workspace.resolve()
    if workspace not in repositories:
        raise ValueError("--workspace must be a repository containing this script")
    path = Path(os.path.abspath(path))
    for candidate in (path, *path.parents):
        if candidate.is_symlink():
            raise ValueError(f"Linked project path: {candidate}")
    allowed = (workspace / ".local", Path("/private/tmp"))
    if not any(path != root and path.is_relative_to(root) for root in allowed):
        raise ValueError("--project must be below workspace/.local or /private/tmp")
    if path == SOURCE or path.is_relative_to(SOURCE) or SOURCE.is_relative_to(path):
        raise ValueError("Validation project overlaps source")
    return path


@contextmanager
def project_lock(project):
    project.parent.mkdir(parents=True, exist_ok=True)
    lock = project.parent / ("." + project.name + ".rapid-smoke.lock")
    try:
        fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError:
        raise ValueError(f"Another run or interrupted preparation owns {lock}; inspect it before removal")
    try:
        with os.fdopen(fd, "w") as output:
            output.write(str(os.getpid()) + "\n")
        yield
    finally:
        lock.unlink()


def prepare(project):
    source = inventory(SOURCE)
    expected = dict(source)
    expected[SETTINGS] = hashlib.sha256(isolated_settings((SOURCE / SETTINGS).read_bytes())).hexdigest()
    identity = {"source": str(SOURCE), "sourceSha256": digest(source),
                "isolatedSha256": digest(expected), "unityVersion": VERSION}
    if project.exists():
        marker = project / MARKER
        if marker.is_symlink() or not marker.is_file() or json.loads(marker.read_text()) != identity:
            raise ValueError("Existing project does not match this source; choose a fresh path (nothing was cleared)")
        if (project / ".git").exists():
            raise ValueError("Validation project must not share Git state")
        for name in ("Library", "Temp", "Logs", "UserSettings", "obj", "RapidTraversalRuns"):
            if (project / name).is_symlink():
                raise ValueError(f"Linked validation cache/output directory: {project / name}")
        if inventory(project) != expected:
            raise ValueError("Existing project inputs changed; choose a fresh path (cache was preserved)")
        print("Reusing matching independent project and its Library.", flush=True)
    else:
        source_bytes = sum((SOURCE / name).stat().st_size for name in source)
        if shutil.disk_usage(project.parent).free < source_bytes + 2 * 1024 ** 3:
            raise ValueError("Insufficient free space for input copy plus 2 GiB import headroom")
        temporary = Path(tempfile.mkdtemp(prefix=project.name + ".preparing-", dir=project.parent))
        try:
            for name in INPUTS:
                shutil.copytree(SOURCE / name, temporary / name, copy_function=shutil.copy2)
            (temporary / SETTINGS).write_bytes(isolated_settings((temporary / SETTINGS).read_bytes()))
            if inventory(temporary) != expected or inventory(SOURCE) != source:
                raise ValueError("Source or isolated copy changed during preparation")
            (temporary / MARKER).write_text(json.dumps(identity, indent=2) + "\n")
            temporary.rename(project)
        except BaseException:
            shutil.rmtree(temporary)
            raise
        print(f"Prepared {len(source)} source/meta inputs with isolated save identity.", flush=True)
    if inventory(SOURCE) != source:
        raise ValueError("Source changed during preparation")
    return source, expected


def stop_editor(process):
    os.killpg(process.pid, signal.SIGTERM)
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait()


def capture_orphan_metas(project, expected):
    return {name: (project / name).read_bytes() for name in expected
            if name.endswith(".meta") and not (project / name[:-5]).exists()
            and not (project / name[:-5]).is_symlink()}


def restore_orphan_metas(project, orphans):
    restored = 0
    for name, data in orphans.items():
        path = project / name
        target = project / name[:-5]
        if target.exists() or target.is_symlink():
            raise ValueError(f"Former orphan meta acquired a target: {target}")
        for parent in path.parents:
            if parent == project:
                break
            if parent.is_symlink():
                raise ValueError(f"Linked orphan meta parent: {parent}")
        if path.is_symlink() or (path.exists() and path.read_bytes() != data):
            raise ValueError(f"Orphan meta changed during Editor run: {path}")
        if not path.exists():
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as output:
                output.write(data)
            restored += 1
    return restored


def execute(args, project, unity, source, expected):
    run = project / "RapidTraversalRuns" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8])
    run.mkdir(parents=True)
    report = run / "report.json"
    log = run / "Editor.log"
    command = [str(unity), "-batchmode", "-buildTarget", "StandaloneOSX", "-projectPath", str(project),
               "-executeMethod", "SlopCity.Editor.SlopCityRapidTraversalSmokeTest.Run",
               "-logFile", str(log), "-slopCityRapidReport", str(report),
               "-slopCityRapidLaps", str(args.laps), "-slopCityRapidSpeed", str(args.speed),
               "-slopCityRapidSeconds", str(args.seconds)]
    if args.licensing_ipc is not None:
        command.extend(["-licensingIpc", args.licensing_ipc])
    print("Editor log: " + str(log), flush=True)
    print("Checkpoint report: " + str(report), flush=True)
    print(LIMITATION, flush=True)
    (run / "command.json").write_text(json.dumps(command, indent=2) + "\n")
    orphans = capture_orphan_metas(project, expected)
    started = time.monotonic()
    process = subprocess.Popen(command, start_new_session=True)
    timed_out = False
    try:
        while process.poll() is None:
            if time.monotonic() - started >= args.timeout:
                timed_out = True
                stop_editor(process)
                break
            try:
                process.wait(timeout=min(30, args.timeout - (time.monotonic() - started)))
            except subprocess.TimeoutExpired:
                print(f"Editor running {int(time.monotonic() - started)}s; log {log.stat().st_size if log.exists() else 0} bytes; "
                      f"checkpoint {'available' if report.exists() else 'not yet written (startup/import)'}.", flush=True)
    except BaseException:
        if process.poll() is None:
            stop_editor(process)
        raise
    finally:
        unchanged = inventory(SOURCE) == source
        print("Source/meta bytes unchanged: " + str(unchanged), flush=True)
        if not unchanged:
            raise ValueError("Public source inputs changed during Editor run")
        restored = restore_orphan_metas(project, orphans)
        print(f"Restored removed, still-orphan meta files: {restored}", flush=True)
        if inventory(project) != expected:
            raise ValueError("Editor changed isolated input files; preserve logs and use a fresh project next time")
    if timed_out:
        raise ValueError(f"Editor exceeded {args.timeout}s; partial report and log remain at {run}")
    if process.returncode != 0:
        raise ValueError(f"Editor exited {process.returncode}; inspect {log}")
    receipt = json.loads(report.read_text()) if report.is_file() else {}
    if receipt.get("success") is not True or receipt.get("unityVersion") != VERSION:
        raise ValueError(f"No successful fresh smoke report; inspect {report} and {log}")
    print("Rapid traversal smoke passed. " + LIMITATION, flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__ + " Default is a dry plan with no writes or Unity launch.")
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--workspace", type=Path, default=Path.cwd(), help="Containing repository; defaults to current directory")
    parser.add_argument("--unity", type=Path, help="Pinned Unity 6000.3.26f1 executable")
    parser.add_argument("--licensing-ipc", help="Optional existing local licensing IPC name; passed to Unity unchanged")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--prepare", action="store_true", help="Copy/verify only; never start Unity")
    mode.add_argument("--execute", action="store_true", help="Copy/verify then run the graphics-enabled native Editor smoke")
    parser.add_argument("--laps", type=int, choices=range(1, 11), default=2, metavar="1..10")
    parser.add_argument("--speed", type=float, default=55, help="Traversal speed in metres/second, 5.5..110 (default 55)")
    parser.add_argument("--seconds", type=int, default=480, help="In-Editor test limit, 60..600 (default 480)")
    parser.add_argument("--timeout", type=int, default=2280, help="Whole Editor process limit including first import (default 2280 seconds)")
    args = parser.parse_args()
    if not 5.5 <= args.speed <= 110 or not 60 <= args.seconds <= 600 or args.timeout < args.seconds:
        parser.error("speed must be 5.5..110, seconds 60..600, and timeout at least seconds")
    project = safe_project(args.project, args.workspace)
    unity = (args.unity or args.workspace / f".tools/Unity-{VERSION}/Unity.app/Contents/MacOS/Unity").resolve()
    version = (SOURCE / "ProjectSettings/ProjectVersion.txt").read_text()
    if f"m_EditorVersion: {VERSION}\n" not in version:
        raise ValueError("Source Unity version differs from pinned runner version")
    print(f"Source: {SOURCE}\nIndependent project: {project}\nUnity: {unity}\n{LIMITATION}", flush=True)
    if not args.prepare and not args.execute:
        print("Dry plan only. Add --prepare to copy inputs or --execute to run.")
        return
    if args.execute and not unity.is_file():
        raise ValueError(f"Pinned Unity executable missing: {unity}")
    with project_lock(project):
        source, expected = prepare(project)
        if args.execute:
            execute(args, project, unity, source, expected)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, json.JSONDecodeError) as error:
        print("Rapid traversal: " + str(error), file=sys.stderr)
        sys.exit(1)
