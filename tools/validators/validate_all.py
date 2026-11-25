#!/usr/bin/env python3
"""Unified Gate 2 validation runner for MopedGame.

Orchestrates:
1. validate_boundaries.py (pure C# isolation, .csproj strictness, Unity .asmdef matrix)
2. validate_data.py       (JSON schemas, referential integrity, assembly DAG acyclicity)
3. validate_assets.py     (binary .glb header, tri budgets, UV0, unit scale, SOCKET_* nodes)
"""

import argparse
import sys
import time
from pathlib import Path
from typing import List, Optional

VALIDATORS_DIR = Path(__file__).resolve().parent
if str(VALIDATORS_DIR) not in sys.path:
    sys.path.insert(0, str(VALIDATORS_DIR))

import validate_assets  # noqa: E402
import validate_boundaries  # noqa: E402
import validate_data  # noqa: E402


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    default_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Run all MopedGame Gate 2 static validators.")
    parser.add_argument("--repo-root", type=Path, default=default_root, help="Repository root path")
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Fail if any expected repository layer (src, data, unity) is missing",
    )
    parser.add_argument(
        "--skip-missing-unity",
        action="store_true",
        help="Skip missing unity/ scripts or .glb directories gracefully",
    )
    parser.add_argument(
        "--allow-partial",
        action="store_true",
        help="Allow partial workspace trees (still fails on any present violation)",
    )
    return parser.parse_args(argv)


def _print_section(title: str, report) -> None:
    print(f"--- {title} ---")
    for msg in report.passes:
        print(f"  [PASS] {msg}")
    for msg in report.skips:
        print(f"  [SKIP] {msg}")
    for msg in report.warnings:
        print(f"  [WARN] {msg}")
    for msg in report.errors:
        print(f"  [FAIL] {msg}", file=sys.stderr)


def main(argv: Optional[List[str]] = None) -> int:
    args = parse_args(argv)
    repo_root = args.repo_root.resolve()
    start = time.monotonic()

    # Once all core layers are integrated, automatically enforce cross-layer completeness
    # unless explicitly running in --allow-partial or --skip-missing-unity mode.
    integrated = (
        (repo_root / "src").exists()
        and (repo_root / "data").exists()
        and (repo_root / "unity").exists()
    )
    effective_strict = args.strict or (integrated and not args.allow_partial and not args.skip_missing_unity)

    steps = [
        ("Boundary & Assembly Matrix Validator", validate_boundaries.run_validation),
        ("Schema, Catalog & DAG Validator", validate_data.run_validation),
        ("Binary .glb Asset & Socket Validator", validate_assets.run_validation),
    ]

    total_passes = 0
    total_skips = 0
    total_warnings = 0
    total_errors = 0

    for title, fn in steps:
        report = fn(
            repo_root,
            strict=effective_strict,
            skip_missing_unity=args.skip_missing_unity,
            allow_partial=args.allow_partial,
        )
        _print_section(title, report)
        total_passes += len(report.passes)
        total_skips += len(report.skips)
        total_warnings += len(report.warnings)
        total_errors += len(report.errors)

    elapsed = time.monotonic() - start
    print(
        f"\nGate 2 Summary ({elapsed:.2f}s): {total_passes} passed, "
        f"{total_skips} skipped, {total_warnings} warnings, {total_errors} failed"
    )
    if total_errors > 0:
        print("[FAIL] Gate 2 static validation failed.", file=sys.stderr)
        return 1

    print("[PASS] Gate 2 static validation passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
