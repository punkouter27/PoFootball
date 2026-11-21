"""Smoke tests for PoFootball repo configuration and Python tooling."""

from __future__ import annotations

import ast
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def test_tools_python_files_parse_cleanly() -> None:
    py_files = sorted((ROOT / "Tools").glob("*.py"))
    assert py_files, "Expected Python scripts under Tools/"
    for path in py_files:
        ast.parse(path.read_text(encoding="utf-8"), filename=str(path))


def test_webgl_json_has_required_fields() -> None:
    cfg = json.loads((ROOT / "Tools" / "webgl.json").read_text(encoding="utf-8"))
    for key in ("buildMethod", "staticWebApp", "resourceGroup", "subscription"):
        assert cfg.get(key), f"Missing {key} in Tools/webgl.json"


def test_unity_version_is_pinned() -> None:
    version_file = ROOT / "ProjectSettings" / "ProjectVersion.txt"
    text = version_file.read_text(encoding="utf-8")
    assert "m_EditorVersion: 6000.6.9f1" in text


def test_training_configs_exist() -> None:
    configs = sorted((ROOT / "Config").glob("FootballBase*.yaml"))
    assert configs, "Expected FootballBase*.yaml under Config/"
