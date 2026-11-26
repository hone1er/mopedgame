"""Stylized Bay Area modular world kit generators (Zone_* scenes)."""

from __future__ import annotations

from typing import Dict, List


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    from world import bay_crossing, oakland_hills, sf_mission, west_oakland

    results: List[Dict[str, object]] = []
    results.extend(west_oakland.generate_all(out_dir))
    results.extend(oakland_hills.generate_all(out_dir))
    results.extend(bay_crossing.generate_all(out_dir))
    results.extend(sf_mission.generate_all(out_dir))
    return results
