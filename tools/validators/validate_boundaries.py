#!/usr/bin/env python3
"""Static architectural boundary & compiler strictness validator for MopedGame.

Enforces AGENTS.md Golden Rules and PROJECT_GUIDE.md §4.2:
1. src/MopedSim.Core & src/MopedSim.Data:
   - .asmdef has "noEngineReferences": true and zero Unity/presentation refs
   - .csproj targets netstandard2.1, LangVersion 9.0, Nullable enable, TreatWarningsAsErrors true
   - .cs files have ZERO UnityEngine / UnityEditor usage and NO C# 10+ syntax
2. tests/*.csproj (when present):
   - Targets modern runnable .NET (e.g. net8.0 .. net11.0), LangVersion 9.0,
     Nullable enable, TreatWarningsAsErrors true, zero Unity refs
3. unity/Assets/Scripts/MopedSim.*/*.asmdef & .cs:
   - Strictly enforces §4.2 assembly dependency matrix (with GUID resolution)
   - Enforces C# 9.0 syntax compatibility and no unguarded UnityEditor in runtime scripts
   - Checks unity/Packages/manifest.json local UPM package links when present
"""

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple


IGNORED_DIR_NAMES = {"bin", "obj", "Library", "Temp", "Obj", "Build", "Builds", "Logs", ".git"}
COMPILED_BINARY_SUFFIXES = {".dll", ".pdb", ".mdb"}

# PROJECT_GUIDE.md §4.2 Assembly Dependency Matrix
UNITY_ASMDEF_RULES: Dict[str, Dict[str, object]] = {
    "MopedSim.Ride": {
        "allowed_moped": {"MopedSim.Core"},
        "allowed_external": {"Unity.InputSystem", "Unity.Mathematics"},
        "forbidden": {
            "MopedSim.Data",
            "MopedSim.Garage",
            "MopedSim.World",
            "MopedSim.UI",
            "MopedSim.Audio",
            "MopedSim.App",
            "MopedSim.Editor",
            "glTFast",
            "Newtonsoft.Json",
            "UnityEditor",
        },
        "require_editor_only": False,
        "forbid_editor_only": True,
    },
    "MopedSim.Garage": {
        "allowed_moped": {"MopedSim.Core", "MopedSim.Data"},
        "allowed_external": {"glTFast", "Unity.InputSystem", "Newtonsoft.Json"},
        "forbidden": {
            "MopedSim.Ride",
            "MopedSim.World",
            "MopedSim.UI",
            "MopedSim.Audio",
            "MopedSim.App",
            "MopedSim.Editor",
            "UnityEditor",
        },
        "require_editor_only": False,
        "forbid_editor_only": True,
    },
    "MopedSim.World": {
        "allowed_moped": {"MopedSim.Core", "MopedSim.Data"},
        "allowed_external": {
            "Unity.InputSystem",
            "Newtonsoft.Json",
            "Unity.RenderPipelines.Universal.Runtime",
            "Unity.RenderPipelines.Core.Runtime",
        },
        "forbidden": {
            "MopedSim.Garage",
            "MopedSim.UI",
            "MopedSim.Ride",
            "MopedSim.Audio",
            "MopedSim.App",
            "MopedSim.Editor",
            "glTFast",
            "UnityEditor",
        },
        "require_editor_only": False,
        "forbid_editor_only": True,
    },
    "MopedSim.Audio": {
        "allowed_moped": {"MopedSim.Core"},
        "allowed_external": {"Unity.Mathematics"},
        "forbidden": {
            "MopedSim.Garage",
            "MopedSim.World",
            "MopedSim.Data",
            "MopedSim.Ride",
            "MopedSim.UI",
            "MopedSim.App",
            "MopedSim.Editor",
            "glTFast",
            "Newtonsoft.Json",
            "UnityEditor",
        },
        "require_editor_only": False,
        "forbid_editor_only": True,
    },
    "MopedSim.UI": {
        "allowed_moped": {"MopedSim.Core", "MopedSim.Data"},
        "allowed_external": {
            "Unity.InputSystem",
            "UnityEngine.UI",
            "Unity.TextMeshPro",
            "Newtonsoft.Json",
        },
        "forbidden": {
            "MopedSim.Ride",
            "MopedSim.World",
            "MopedSim.Garage",
            "MopedSim.Audio",
            "MopedSim.App",
            "MopedSim.Editor",
            "glTFast",
            "UnityEditor",
        },
        "require_editor_only": False,
        "forbid_editor_only": True,
    },
    "MopedSim.App": {
        "allowed_moped": {
            "MopedSim.Core",
            "MopedSim.Data",
            "MopedSim.Ride",
            "MopedSim.Garage",
            "MopedSim.World",
            "MopedSim.UI",
            "MopedSim.Audio",
        },
        "allowed_external": {
            "Unity.InputSystem",
            "glTFast",
            "Newtonsoft.Json",
            "UnityEngine.UI",
            "Unity.TextMeshPro",
            "Unity.RenderPipelines.Universal.Runtime",
            "Unity.RenderPipelines.Core.Runtime",
        },
        "forbidden": {
            "MopedSim.Editor",
            "UnityEditor",
        },
        "require_editor_only": False,
        "forbid_editor_only": True,
    },
    "MopedSim.Editor": {
        "allowed_moped": {
            "MopedSim.Core",
            "MopedSim.Data",
            "MopedSim.Ride",
            "MopedSim.Garage",
            "MopedSim.World",
            "MopedSim.UI",
            "MopedSim.Audio",
            "MopedSim.App",
        },
        "allowed_external": {
            "UnityEditor",
            "Unity.InputSystem",
            "glTFast",
            "Newtonsoft.Json",
            "UnityEngine.UI",
            "Unity.TextMeshPro",
            "Unity.RenderPipelines.Universal.Runtime",
            "Unity.RenderPipelines.Core.Runtime",
            "UnityEngine.TestRunner",
            "UnityEditor.TestRunner",
        },
        "forbidden": set(),
        "require_editor_only": True,
        "forbid_editor_only": False,
    },
}

PURE_CSHARP_PACKAGES: Dict[str, Set[str]] = {
    "MopedSim.Core": set(),
    "MopedSim.Data": {"MopedSim.Core", "Newtonsoft.Json"},
}

FILE_SCOPED_NS_RE = re.compile(r"^\s*namespace\s+[A-Za-z_][A-Za-z0-9_.]*\s*;")
GLOBAL_USING_RE = re.compile(r"^\s*global\s+using\b")
REQUIRED_MEMBER_RE = re.compile(
    r"^\s*(?:(?:public|private|protected|internal|new|static|virtual|override|readonly)\s+)+required\s+\S"
)
COLLECTION_EXPR_RE = re.compile(r"(?:=|return|=>)\s*\[\s*\]")
USING_UNITY_ENGINE_RE = re.compile(r"\busing\s+(?:static\s+)?UnityEngine(?:\s*[.;]|$)")
USING_UNITY_EDITOR_RE = re.compile(r"\busing\s+(?:static\s+)?UnityEditor(?:\s*[.;]|$)")
QUALIFIED_UNITY_ENGINE_RE = re.compile(r"\bUnityEngine\.")
QUALIFIED_UNITY_EDITOR_RE = re.compile(r"\bUnityEditor\.")


@dataclass
class ValidationReport:
    errors: List[str] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)
    passes: List[str] = field(default_factory=list)
    skips: List[str] = field(default_factory=list)

    def error(self, msg: str) -> None:
        self.errors.append(msg)

    def warn(self, msg: str) -> None:
        self.warnings.append(msg)

    def passed(self, msg: str) -> None:
        self.passes.append(msg)

    def skip(self, msg: str) -> None:
        self.skips.append(msg)


def iter_files(root: Path, suffix: str) -> List[Path]:
    if not root.exists():
        return []
    matched: List[Path] = []
    for path in sorted(root.rglob(f"*{suffix}")):
        if any(part in IGNORED_DIR_NAMES for part in path.parts):
            continue
        matched.append(path)
    return matched


def strip_cs_comments_and_strings(source: str) -> str:
    """Replace C# comments and string/char literals with spaces while preserving newlines."""
    out: List[str] = []
    n = len(source)
    i = 0
    state = "CODE"
    while i < n:
        ch = source[i]
        nxt = source[i + 1] if i + 1 < n else ""
        if state == "CODE":
            if ch == "/" and nxt == "/":
                state = "LINE_COMMENT"
                out.append("  ")
                i += 2
                continue
            if ch == "/" and nxt == "*":
                state = "BLOCK_COMMENT"
                out.append("  ")
                i += 2
                continue
            if ch == "@" and nxt == '"':
                state = "VERBATIM_STR"
                out.append('""')
                i += 2
                continue
            if ch == '"':
                state = "STR"
                out.append('"')
                i += 1
                continue
            if ch == "'":
                state = "CHAR"
                out.append("'")
                i += 1
                continue
            out.append(ch)
            i += 1
        elif state == "LINE_COMMENT":
            if ch == "\n":
                state = "CODE"
                out.append("\n")
            else:
                out.append(" ")
            i += 1
        elif state == "BLOCK_COMMENT":
            if ch == "*" and nxt == "/":
                state = "CODE"
                out.append("  ")
                i += 2
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
        elif state == "VERBATIM_STR":
            if ch == '"' and nxt == '"':
                out.append("  ")
                i += 2
            elif ch == '"':
                state = "CODE"
                out.append('"')
                i += 1
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
        elif state == "STR":
            if ch == "\\":
                out.append("  " if nxt else " ")
                i += 2 if nxt else 1
            elif ch == '"':
                state = "CODE"
                out.append('"')
                i += 1
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
        elif state == "CHAR":
            if ch == "\\":
                out.append("  " if nxt else " ")
                i += 2 if nxt else 1
            elif ch == "'":
                state = "CODE"
                out.append("'")
                i += 1
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
    return "".join(out)


def load_json(path: Path, report: ValidationReport) -> Optional[object]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        report.error(f"{path}: invalid JSON ({exc})")
        return None


def collect_asmdef_guid_map(repo_root: Path) -> Dict[str, str]:
    """Map Unity .asmdef GUIDs (from .asmdef.meta) to assembly names."""
    guid_map: Dict[str, str] = {}
    for search_root in (repo_root / "src", repo_root / "unity"):
        for asmdef_path in iter_files(search_root, ".asmdef"):
            try:
                data = json.loads(asmdef_path.read_text(encoding="utf-8"))
            except Exception:
                continue
            if not isinstance(data, dict):
                continue
            asm_name = data.get("name")
            if not isinstance(asm_name, str) or not asm_name:
                continue
            meta_path = Path(f"{asmdef_path}.meta")
            if meta_path.is_file():
                for line in meta_path.read_text(encoding="utf-8", errors="ignore").splitlines():
                    stripped = line.strip()
                    if stripped.startswith("guid:"):
                        guid = stripped.split(":", 1)[1].strip()
                        if guid:
                            guid_map[guid.lower()] = asm_name
                        break
    return guid_map


def resolve_asmdef_ref(raw_ref: str, guid_map: Dict[str, str]) -> str:
    if raw_ref.startswith("GUID:"):
        guid = raw_ref[5:].strip().lower()
        return guid_map.get(guid, raw_ref)
    return raw_ref


def extract_csproj_properties(root: ET.Element) -> Dict[str, List[str]]:
    props: Dict[str, List[str]] = {}
    for elem in root.iter():
        tag = elem.tag.split("}")[-1]
        if elem.text is not None:
            text = elem.text.strip()
            if text:
                props.setdefault(tag, []).append(text)
    return props


def validate_csproj(
    csproj_path: Path,
    *,
    is_test_project: bool,
    is_core_pkg: bool,
    report: ValidationReport,
) -> None:
    try:
        tree = ET.parse(csproj_path)
    except ET.ParseError as exc:
        report.error(f"{csproj_path}: invalid XML ({exc})")
        return

    root = tree.getroot()
    props = extract_csproj_properties(root)

    tfms = props.get("TargetFramework", [])
    multi_tfms = props.get("TargetFrameworks", [])
    if is_test_project:
        if not tfms:
            report.error(f"{csproj_path}: missing <TargetFramework>")
        else:
            tfm = tfms[-1]
            m = re.match(r"^net(\d+)\.(\d+)$", tfm)
            if not m or int(m.group(1)) < 8:
                report.error(
                    f"{csproj_path}: test <TargetFramework> '{tfm}' must target runnable .NET (e.g. net8.0 or net11.0)"
                )
    else:
        if multi_tfms:
            report.error(f"{csproj_path}: use single <TargetFramework>netstandard2.1</TargetFramework>")
        elif not tfms or tfms[-1].lower() != "netstandard2.1":
            actual = tfms[-1] if tfms else "<missing>"
            report.error(f"{csproj_path}: <TargetFramework> must be 'netstandard2.1', got '{actual}'")

    lang_versions = props.get("LangVersion", [])
    if not lang_versions or lang_versions[-1] != "9.0":
        actual = lang_versions[-1] if lang_versions else "<missing>"
        report.error(f"{csproj_path}: <LangVersion> must be '9.0', got '{actual}'")

    nullables = props.get("Nullable", [])
    if not nullables or nullables[-1].lower() != "enable":
        actual = nullables[-1] if nullables else "<missing>"
        report.error(f"{csproj_path}: <Nullable> must be 'enable', got '{actual}'")

    treat_warn = props.get("TreatWarningsAsErrors", [])
    if not treat_warn or treat_warn[-1].lower() != "true":
        actual = treat_warn[-1] if treat_warn else "<missing>"
        report.error(f"{csproj_path}: <TreatWarningsAsErrors> must be 'true', got '{actual}'")

    for elem in root.iter():
        tag = elem.tag.split("}")[-1]
        if tag in {"Reference", "PackageReference", "ProjectReference"}:
            inc = elem.attrib.get("Include", "")
            if "UnityEngine" in inc or "UnityEditor" in inc or inc.startswith("Unity."):
                report.error(f"{csproj_path}: forbidden Unity reference '{inc}'")
            if is_core_pkg and tag == "PackageReference":
                report.error(f"{csproj_path}: MopedSim.Core must have zero NuGet dependencies (found '{inc}')")
            if is_core_pkg and tag == "ProjectReference":
                report.error(f"{csproj_path}: MopedSim.Core must have zero ProjectReferences (found '{inc}')")

    report.passed(f"{csproj_path}: .csproj strictness checks passed")


def validate_cs_syntax_and_unity_refs(
    cs_path: Path,
    *,
    allow_unity_engine: bool,
    allow_unity_editor_anywhere: bool,
    allow_unity_editor_ifdef: bool,
    report: ValidationReport,
) -> None:
    raw = cs_path.read_text(encoding="utf-8", errors="replace")
    stripped = strip_cs_comments_and_strings(raw)

    # Track #if UNITY_EDITOR state per line
    editor_stack: List[Optional[bool]] = []
    for lineno, line in enumerate(stripped.splitlines(), start=1):
        s = line.strip()
        if s.startswith("#if"):
            cond = s[3:].strip()
            is_ed = bool(re.search(r"(?<!!)\bUNITY_EDITOR\b", cond)) and not re.search(
                r"!\s*UNITY_EDITOR\b", cond
            )
            editor_stack.append(is_ed)
            continue
        if s.startswith("#elif"):
            if editor_stack:
                cond = s[5:].strip()
                is_ed = bool(re.search(r"(?<!!)\bUNITY_EDITOR\b", cond)) and not re.search(
                    r"!\s*UNITY_EDITOR\b", cond
                )
                editor_stack[-1] = is_ed
            continue
        if s.startswith("#else"):
            if editor_stack and editor_stack[-1] is not None:
                editor_stack[-1] = not editor_stack[-1]
            continue
        if s.startswith("#endif"):
            if editor_stack:
                editor_stack.pop()
            continue

        in_editor_ifdef = any(flag is True for flag in editor_stack)

        if FILE_SCOPED_NS_RE.match(line):
            report.error(f"{cs_path}:{lineno}: C# 10+ file-scoped namespace is forbidden (LangVersion 9.0)")
        if GLOBAL_USING_RE.match(line):
            report.error(f"{cs_path}:{lineno}: C# 10+ global using is forbidden (LangVersion 9.0)")
        if REQUIRED_MEMBER_RE.match(line):
            report.error(f"{cs_path}:{lineno}: C# 11+ 'required' modifier is forbidden (LangVersion 9.0)")
        if COLLECTION_EXPR_RE.search(line):
            report.error(f"{cs_path}:{lineno}: C# 12+ collection expression '[]' is forbidden (LangVersion 9.0)")

        if not allow_unity_engine:
            if USING_UNITY_ENGINE_RE.search(line) or QUALIFIED_UNITY_ENGINE_RE.search(line):
                report.error(f"{cs_path}:{lineno}: forbidden UnityEngine reference in engine-agnostic code")

        if not allow_unity_editor_anywhere:
            if not (allow_unity_editor_ifdef and in_editor_ifdef):
                if USING_UNITY_EDITOR_RE.search(line) or QUALIFIED_UNITY_EDITOR_RE.search(line):
                    report.error(f"{cs_path}:{lineno}: forbidden unguarded UnityEditor reference")


def validate_pure_package(
    pkg_dir: Path,
    pkg_name: str,
    allowed_refs: Set[str],
    guid_map: Dict[str, str],
    report: ValidationReport,
) -> None:
    asmdef_path = pkg_dir / f"{pkg_name}.asmdef"
    if not asmdef_path.is_file():
        report.error(f"{asmdef_path}: missing required .asmdef")
    else:
        data = load_json(asmdef_path, report)
        if isinstance(data, dict):
            if data.get("name") != pkg_name:
                report.error(f"{asmdef_path}: 'name' must be '{pkg_name}', got {data.get('name')!r}")
            if data.get("noEngineReferences") is not True:
                report.error(f"{asmdef_path}: 'noEngineReferences' MUST be true")

            refs = data.get("references", [])
            if not isinstance(refs, list):
                report.error(f"{asmdef_path}: 'references' must be a list")
            else:
                for raw_ref in refs:
                    resolved = resolve_asmdef_ref(str(raw_ref), guid_map)
                    if resolved not in allowed_refs:
                        report.error(
                            f"{asmdef_path}: forbidden reference '{resolved}' (allowed: {sorted(allowed_refs)})"
                        )
                report.passed(f"{asmdef_path}: noEngineReferences=true and clean refs")

            precompiled = data.get("precompiledReferences", [])
            if isinstance(precompiled, list):
                for pr in precompiled:
                    if "UnityEngine" in str(pr) or "UnityEditor" in str(pr):
                        report.error(f"{asmdef_path}: forbidden precompiled Unity reference '{pr}'")

    pkg_json = pkg_dir / "package.json"
    if pkg_json.is_file():
        pdata = load_json(pkg_json, report)
        if isinstance(pdata, dict):
            expected_id = f"com.mopedgame.{pkg_name.split('.')[-1].lower()}"
            if pdata.get("name") != expected_id:
                report.warn(f"{pkg_json}: package name '{pdata.get('name')}' != expected '{expected_id}'")
    else:
        report.error(f"{pkg_json}: missing UPM package.json")

    csprojs = [p for p in pkg_dir.glob("*.csproj")]
    if not csprojs:
        report.error(f"{pkg_dir}: missing .csproj file")
    for csproj in csprojs:
        validate_csproj(
            csproj,
            is_test_project=False,
            is_core_pkg=(pkg_name == "MopedSim.Core"),
            report=report,
        )

    # Unity imports every file inside a UPM package folder, including stale bin/obj build output. The src/ .csproj
    # files redirect BaseOutputPath/BaseIntermediateOutputPath to <repo>/artifacts/, so no compiled binary may remain here.
    stray_binaries = sorted(
        p for p in pkg_dir.rglob("*") if p.is_file() and p.suffix.lower() in COMPILED_BINARY_SUFFIXES
    )
    for binary in stray_binaries:
        report.error(
            f"{binary}: compiled binary inside UPM package (Unity would import it); build outputs belong in artifacts/"
        )
    if not stray_binaries:
        report.passed(f"{pkg_dir}: no stray compiled binaries (.dll/.pdb) inside UPM package")

    cs_files = iter_files(pkg_dir, ".cs")
    before_errors = len(report.errors)
    for cs_path in cs_files:
        validate_cs_syntax_and_unity_refs(
            cs_path,
            allow_unity_engine=False,
            allow_unity_editor_anywhere=False,
            allow_unity_editor_ifdef=False,
            report=report,
        )
    if len(report.errors) == before_errors:
        report.passed(f"{pkg_dir}: {len(cs_files)} C# files clean (no Unity refs, C# 9.0)")


def validate_unity_assembly(
    scripts_root: Path,
    asm_name: str,
    guid_map: Dict[str, str],
    report: ValidationReport,
) -> None:
    asm_dir = scripts_root / asm_name
    asmdef_path = asm_dir / f"{asm_name}.asmdef"
    if not asm_dir.exists() or not asmdef_path.is_file():
        report.error(f"{asmdef_path}: missing required Unity assembly definition")
        return

    data = load_json(asmdef_path, report)
    if not isinstance(data, dict):
        return

    rules = UNITY_ASMDEF_RULES[asm_name]
    if data.get("name") != asm_name:
        report.error(f"{asmdef_path}: 'name' must be '{asm_name}', got {data.get('name')!r}")

    refs = data.get("references", [])
    if not isinstance(refs, list):
        report.error(f"{asmdef_path}: 'references' must be a list")
    else:
        allowed_moped: Set[str] = rules["allowed_moped"]  # type: ignore[assignment]
        allowed_external: Set[str] = rules["allowed_external"]  # type: ignore[assignment]
        forbidden: Set[str] = rules["forbidden"]  # type: ignore[assignment]
        for raw_ref in refs:
            ref = resolve_asmdef_ref(str(raw_ref), guid_map)
            if ref in forbidden or any(ref.startswith(f + ".") for f in forbidden):
                report.error(f"{asmdef_path}: forbidden assembly reference '{ref}'")
            elif ref.startswith("MopedSim."):
                if ref not in allowed_moped:
                    report.error(
                        f"{asmdef_path}: MopedSim reference '{ref}' not permitted (allowed: {sorted(allowed_moped)})"
                    )
            else:
                if ref not in allowed_external and not any(
                    ref.startswith(prefix + ".") for prefix in allowed_external
                ):
                    report.error(
                        f"{asmdef_path}: unexpected external reference '{ref}' "
                        f"(allowed per §4.2: {sorted(allowed_external)})"
                    )

    include_platforms = data.get("includePlatforms", [])
    if not isinstance(include_platforms, list):
        include_platforms = []
    if rules["require_editor_only"] and include_platforms != ["Editor"]:
        report.error(
            f"{asmdef_path}: MopedSim.Editor MUST set \"includePlatforms\": [\"Editor\"] "
            f"(got {include_platforms!r})"
        )
    if rules["forbid_editor_only"] and "Editor" in include_platforms and len(include_platforms) == 1:
        report.error(f"{asmdef_path}: runtime assembly must not restrict includePlatforms to ['Editor']")

    cs_files = iter_files(asm_dir, ".cs")
    before_errors = len(report.errors)
    is_editor = asm_name == "MopedSim.Editor"
    for cs_path in cs_files:
        validate_cs_syntax_and_unity_refs(
            cs_path,
            allow_unity_engine=True,
            allow_unity_editor_anywhere=is_editor,
            allow_unity_editor_ifdef=not is_editor,
            report=report,
        )
    if len(report.errors) == before_errors:
        report.passed(f"{asmdef_path}: §4.2 deps & {len(cs_files)} C# files valid")


def validate_unity_tests(unity_root: Path, guid_map: Dict[str, str], report: ValidationReport) -> None:
    tests_root = unity_root / "Assets" / "Tests"
    if not tests_root.exists():
        return
    for asmdef_path in iter_files(tests_root, ".asmdef"):
        data = load_json(asmdef_path, report)
        if not isinstance(data, dict):
            continue
        is_edit_mode = "EditMode" in asmdef_path.parts
        is_play_mode = "PlayMode" in asmdef_path.parts
        include_platforms = data.get("includePlatforms", [])
        if is_edit_mode and include_platforms != ["Editor"]:
            report.error(f"{asmdef_path}: EditMode test assembly must set \"includePlatforms\": [\"Editor\"]")
        if is_play_mode:
            refs = [resolve_asmdef_ref(str(r), guid_map) for r in data.get("references", [])]
            if "MopedSim.Editor" in refs:
                report.error(f"{asmdef_path}: PlayMode test assembly must not reference MopedSim.Editor")
        report.passed(f"{asmdef_path}: Unity test assembly valid")

    for cs_path in iter_files(tests_root, ".cs"):
        validate_cs_syntax_and_unity_refs(
            cs_path,
            allow_unity_engine=True,
            allow_unity_editor_anywhere=("EditMode" in cs_path.parts),
            allow_unity_editor_ifdef=True,
            report=report,
        )


def validate_unity_manifest(unity_root: Path, report: ValidationReport) -> None:
    manifest_path = unity_root / "Packages" / "manifest.json"
    if not manifest_path.is_file():
        report.warn(f"{manifest_path}: not found yet")
        return
    data = load_json(manifest_path, report)
    if not isinstance(data, dict):
        return
    deps = data.get("dependencies", {})
    if not isinstance(deps, dict):
        report.error(f"{manifest_path}: 'dependencies' must be an object")
        return
    expected = {
        "com.mopedgame.core": "file:../../src/MopedSim.Core",
        "com.mopedgame.data": "file:../../src/MopedSim.Data",
    }
    for pkg, target in expected.items():
        if deps.get(pkg) != target:
            report.error(f"{manifest_path}: dependency '{pkg}' must be '{target}', got {deps.get(pkg)!r}")
    for req_pkg in ("com.unity.cloud.gltfast", "com.unity.nuget.newtonsoft-json"):
        if req_pkg not in deps:
            report.error(f"{manifest_path}: missing required dependency '{req_pkg}'")
    report.passed(f"{manifest_path}: UPM dependencies valid")


def run_validation(
    repo_root: Path,
    *,
    strict: bool = False,
    skip_missing_unity: bool = False,
    allow_partial: bool = False,
) -> ValidationReport:
    report = ValidationReport()
    guid_map = collect_asmdef_guid_map(repo_root)

    # 1. Pure C# packages under src/
    src_root = repo_root / "src"
    if not src_root.exists():
        if strict and not allow_partial:
            report.error(f"{src_root}: missing src/ directory")
        else:
            report.skip("src/ not present yet; skipping pure-C# boundary checks")
    else:
        for pkg_name, allowed_refs in PURE_CSHARP_PACKAGES.items():
            pkg_dir = src_root / pkg_name
            if not pkg_dir.exists():
                if strict and not allow_partial:
                    report.error(f"{pkg_dir}: missing required package directory")
                else:
                    report.skip(f"{pkg_dir}: not present yet; skipping")
            else:
                validate_pure_package(pkg_dir, pkg_name, allowed_refs, guid_map, report)

    # 2. Pure .NET test projects under tests/
    tests_root = repo_root / "tests"
    if not tests_root.exists():
        if strict and not allow_partial:
            report.error(f"{tests_root}: missing tests/ directory")
        else:
            report.skip("tests/ not present yet; skipping .NET test csproj checks")
    else:
        test_csprojs = iter_files(tests_root, ".csproj")
        if not test_csprojs and strict and not allow_partial:
            report.error(f"{tests_root}: no .csproj files found under tests/")
        for csproj in test_csprojs:
            validate_csproj(csproj, is_test_project=True, is_core_pkg=False, report=report)

    # 3. Unity scripts & packages under unity/
    unity_root = repo_root / "unity"
    scripts_root = unity_root / "Assets" / "Scripts"
    if not scripts_root.exists():
        if skip_missing_unity or allow_partial or not strict:
            report.skip("unity/Assets/Scripts not present yet; skipping Unity .asmdef checks")
        else:
            report.error(f"{scripts_root}: missing Unity scripts directory")
    else:
        for asm_name in UNITY_ASMDEF_RULES:
            validate_unity_assembly(scripts_root, asm_name, guid_map, report)
        # Flag any unexpected MopedSim.* directory inside unity/Assets/Scripts
        for child in sorted(scripts_root.iterdir()):
            if child.is_dir() and child.name.startswith("MopedSim.") and child.name not in UNITY_ASMDEF_RULES:
                report.error(f"{child}: undocumented assembly directory not in PROJECT_GUIDE.md §4.2")
        validate_unity_tests(unity_root, guid_map, report)
        validate_unity_manifest(unity_root, report)

    return report


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    default_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Validate MopedGame architectural boundaries and C# strictness.")
    parser.add_argument("--repo-root", type=Path, default=default_root, help="Repository root path")
    parser.add_argument("--strict", action="store_true", help="Fail if any layer directory is missing")
    parser.add_argument("--skip-missing-unity", action="store_true", help="Skip missing unity/ directory checks")
    parser.add_argument("--allow-partial", action="store_true", help="Allow partial workspace trees")
    return parser.parse_args(argv)


def main(argv: Optional[List[str]] = None) -> int:
    args = parse_args(argv)
    report = run_validation(
        args.repo_root.resolve(),
        strict=args.strict,
        skip_missing_unity=args.skip_missing_unity,
        allow_partial=args.allow_partial,
    )
    for item in report.passes:
        print(f"  [PASS] {item}")
    for item in report.skips:
        print(f"  [SKIP] {item}")
    for item in report.warnings:
        print(f"  [WARN] {item}")
    for item in report.errors:
        print(f"  [FAIL] {item}", file=sys.stderr)

    print(
        f"Boundary check summary: {len(report.passes)} passed, "
        f"{len(report.skips)} skipped, {len(report.warnings)} warnings, {len(report.errors)} failed"
    )
    return 1 if report.errors else 0


if __name__ == "__main__":
    sys.exit(main())
