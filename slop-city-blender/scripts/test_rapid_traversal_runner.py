#!/usr/bin/env python3
"""Isolation and input-preservation checks; these never launch Unity."""

import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

import run_rapid_traversal_smoke as runner


class RunnerSafety(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(dir="/private/tmp")
        self.root = Path(self.temporary.name)
        self.source = self.root / "repository" / "Unity"
        (self.source.parent / ".git").mkdir(parents=True)
        for folder in runner.INPUTS:
            (self.source / folder).mkdir(parents=True)
        self.settings = b"PlayerSettings:\r\n  companyName: Real Company\r\n  productName: Real Game\r\n  unrelated: true\r\n"
        (self.source / runner.SETTINGS).write_bytes(self.settings)
        (self.source / "Assets/Scene.unity").write_bytes(b"authored scene\x00\xff")
        (self.source / "Assets/Scene.unity.meta").write_bytes(b"guid: unchanged\n")
        (self.source / "Packages/manifest.json").write_text("{}\n")
        (self.source / "Library").mkdir()
        (self.source / "Library/do-not-copy").write_text("cache")
        self.project = self.root / "copy"
        self.source_patch = patch.object(runner, "SOURCE", self.source)
        self.source_patch.start()

    def tearDown(self):
        self.source_patch.stop()
        self.temporary.cleanup()

    def prepare(self):
        with contextlib.redirect_stdout(io.StringIO()):
            return runner.prepare(self.project)

    def test_exact_copy_except_save_identity_and_cache_reuse(self):
        original, expected = self.prepare()
        self.assertEqual((self.source / runner.SETTINGS).read_bytes(), self.settings)
        self.assertEqual((self.project / runner.SETTINGS).read_bytes(),
                         self.settings.replace(b"Real Company", b"SlopCityValidation")
                         .replace(b"Real Game", b"RapidTraversalSmoke"))
        self.assertEqual(runner.inventory(self.source), original)
        self.assertEqual(runner.inventory(self.project), expected)
        self.assertFalse((self.project / "Library").exists())
        (self.project / "Library").mkdir()
        retained = self.project / "Library/retained"
        retained.write_text("leave this cache")
        self.prepare()
        self.assertEqual(retained.read_text(), "leave this cache")

    def test_linked_cache_cannot_be_reused(self):
        self.prepare()
        (self.project / "Library").symlink_to(self.source / "Library", target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Linked validation cache"):
            self.prepare()
        self.assertEqual((self.source / "Library/do-not-copy").read_text(), "cache")

    def test_changed_source_or_copy_refuses_without_clearing(self):
        self.prepare()
        asset = self.project / "Assets/Scene.unity"
        asset.write_bytes(b"local edit")
        with self.assertRaisesRegex(ValueError, "inputs changed"):
            self.prepare()
        self.assertEqual(asset.read_bytes(), b"local edit")
        (self.source / "Assets/Scene.unity").write_bytes(b"new source")
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.prepare()
        self.assertEqual(asset.read_bytes(), b"local edit")

    def test_unknown_project_and_linked_or_shared_inputs_are_rejected(self):
        self.project.mkdir()
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.prepare()
        self.project.rmdir()
        (self.source / "Assets/linked").symlink_to(self.source / "Assets/Scene.unity")
        with self.assertRaisesRegex(ValueError, "Linked Unity input"):
            self.prepare()
        (self.source / "Assets/linked").unlink()
        self.prepare()
        os.link(self.project / "Assets/Scene.unity", self.project / "Assets/shared")
        with self.assertRaisesRegex(ValueError, "Shared Unity input"):
            runner.inventory(self.project)

    def test_bad_settings_rejected_before_any_save_access(self):
        for data in (b"no identities", self.settings + b"  productName: duplicate\n"):
            with self.assertRaisesRegex(ValueError, "exactly one"):
                runner.isolated_settings(data)

    def test_only_missing_unchanged_orphan_metas_can_be_restored(self):
        meta = "Assets/Absent.asset.meta"
        data = b"guid: preserved orphan\r\n"
        (self.source / meta).write_bytes(data)
        source, expected = self.prepare()
        orphans = runner.capture_orphan_metas(self.project, expected)
        self.assertEqual(orphans, {meta: data})
        path = self.project / meta
        self.assertEqual(runner.restore_orphan_metas(self.project, orphans), 0)
        path.unlink()
        self.assertEqual(runner.restore_orphan_metas(self.project, orphans), 1)
        self.assertEqual(path.read_bytes(), data)
        self.assertEqual(runner.inventory(self.project), expected)
        self.assertEqual(runner.inventory(self.source), source)
        path.write_bytes(b"changed existing metadata")
        with self.assertRaisesRegex(ValueError, "Orphan meta changed"):
            runner.restore_orphan_metas(self.project, orphans)
        self.assertEqual(path.read_bytes(), b"changed existing metadata")
        path.unlink()
        target = self.project / meta[:-5]
        target.write_bytes(b"new target")
        with self.assertRaisesRegex(ValueError, "acquired a target"):
            runner.restore_orphan_metas(self.project, orphans)
        self.assertFalse(path.exists())
        self.assertEqual(target.read_bytes(), b"new target")

    def test_destination_boundaries_and_parent_links(self):
        workspace = self.source.parent
        destination = workspace / ".local/check/project"
        self.assertEqual(runner.safe_project(destination, workspace), destination)
        with self.assertRaisesRegex(ValueError, "below workspace"):
            runner.safe_project(Path("/Users/outside-validation"), workspace)
        (workspace / ".local").symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Linked project path"):
            runner.safe_project(destination, workspace)
        with self.assertRaisesRegex(ValueError, "repository containing"):
            runner.safe_project(self.root / "elsewhere", self.root)

    def test_lock_serializes_runs_and_does_not_erase_another_lock(self):
        with runner.project_lock(self.project):
            lock = self.project.parent / ("." + self.project.name + ".rapid-smoke.lock")
            before = lock.read_text()
            with self.assertRaisesRegex(ValueError, "Another run"):
                with runner.project_lock(self.project):
                    self.fail("overlapping run admitted")
            self.assertEqual(lock.read_text(), before)
        self.assertFalse(lock.exists())

    def test_graphics_command_and_fresh_success_report_are_required(self):
        source, expected = self.prepare()
        args = SimpleNamespace(laps=2, speed=55, seconds=480, timeout=2280, licensing_ipc=None)
        commands = []
        def fake_editor(command, **kwargs):
            commands.append(command)
            self.assertTrue(kwargs["start_new_session"])
            report_path = Path(command[command.index("-slopCityRapidReport") + 1])
            self.assertFalse(report_path.exists())
            report_path.write_text(json.dumps(receipt))
            process = Mock(returncode=0)
            process.poll.return_value = 0
            return process
        for receipt in ({"success": False, "unityVersion": runner.VERSION},
                        {"success": True, "unityVersion": "wrong version"},
                        {"success": True, "unityVersion": runner.VERSION}):
            with self.subTest(receipt=receipt), patch.object(runner.subprocess, "Popen", fake_editor), \
                    contextlib.redirect_stdout(io.StringIO()):
                if receipt["success"] and receipt["unityVersion"] == runner.VERSION:
                    args.licensing_ipc = "OwnedLocalTestClient"
                    runner.execute(args, self.project, Path("/not-launched/Unity"), source, expected)
                else:
                    with self.assertRaisesRegex(ValueError, "successful fresh smoke report"):
                        runner.execute(args, self.project, Path("/not-launched/Unity"), source, expected)
        for command in commands:
            self.assertIn("-batchmode", command)
            self.assertIn("StandaloneOSX", command)
            self.assertNotIn("-nographics", command)
            self.assertNotIn("-quit", command)
        self.assertNotIn("-licensingIpc", commands[0])
        self.assertNotIn("-licensingIpc", commands[1])
        self.assertEqual(commands[2][commands[2].index("-licensingIpc") + 1], "OwnedLocalTestClient")


if __name__ == "__main__":
    unittest.main()
