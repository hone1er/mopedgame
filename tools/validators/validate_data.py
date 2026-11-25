#!/usr/bin/env python3
"""Zero-dependency JSON Schema subset, referential integrity, and DAG validator for MopedGame.

Enforces AGENTS.md Golden Rule #3 and PROJECT_GUIDE.md §2.2, §2.3, §4.4:
1. Validates JSON syntax for all data/schemas/*.json and data/catalogs/*.json.
2. Validates catalogs against their JSON Schemas (Draft-07 / 2020-12 subset, pure Python).
3. Verifies unique IDs across every catalog.
4. Verifies referential integrity:
   - archetypes -> parts (default loadout / mount slots)
   - jobs -> zones (and optional part refs)
   - faults -> repair tools / spares in parts.json
   - zones -> connected/adjacent zones
5. Verifies DAG acyclicity of mount slot parent hierarchies and removal preconditions.
"""

import argparse
import json
import math
import re
import sys
from dataclasses import dataclass, field
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Set, Tuple


REQUIRED_SCHEMAS = (
    "part.schema.json",
    "bike_archetype.schema.json",
    "fault.schema.json",
    "zone.schema.json",
    "job.schema.json",
    "save_state.schema.json",
)

CATALOG_SCHEMA_MAP: Dict[str, Tuple[str, Tuple[str, ...]]] = {
    "parts.json": ("part.schema.json", ("parts", "items", "entries")),
    "archetypes.json": ("bike_archetype.schema.json", ("archetypes", "bikes", "items", "entries")),
    "faults.json": ("fault.schema.json", ("faults", "items", "entries")),
    "zones.json": ("zone.schema.json", ("zones", "items", "entries")),
    "jobs.json": ("job.schema.json", ("jobs", "gigs", "items", "entries")),
}

REQUIRED_ARCHETYPE_IDS = ("arch_maxer_e50", "arch_bramble_pa50", "arch_targa_a35")
REQUIRED_FAULT_IDS = (
    "fault_fouled_plug",
    "fault_clogged_jet",
    "fault_condenser_heatsoak",
    "fault_points_drift",
    "fault_air_leak",
    "fault_thrown_chain",
    "fault_snapped_belt",
    "fault_exhaust_loose",
    "fault_soft_seize",
    "fault_hard_seize",
)
REQUIRED_ZONE_IDS = (
    "Zone_WestOakland",
    "Zone_OaklandHills",
    "Zone_BayCrossing",
    "Zone_SF_Mission",
)
REQUIRED_SAVE_KEYS = (
    "schemaVersion",
    "seed",
    "cashUsd",
    "bikes",
    "inventory",
    "activeToolRoll",
    "completedGigs",
)


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


def _is_int(val: Any) -> bool:
    return isinstance(val, int) and not isinstance(val, bool)


def _is_num(val: Any) -> bool:
    return isinstance(val, (int, float)) and not isinstance(val, bool) and math.isfinite(float(val))


def _json_equal(a: Any, b: Any) -> bool:
    if isinstance(a, bool) or isinstance(b, bool):
        return type(a) is type(b) and a == b
    if _is_num(a) and _is_num(b):
        return float(a) == float(b)
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(_json_equal(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return set(a.keys()) == set(b.keys()) and all(_json_equal(a[k], b[k]) for k in a)
    return type(a) is type(b) and a == b


def _canonical_key(val: Any) -> Any:
    if isinstance(val, bool):
        return ("bool", val)
    if val is None:
        return ("null", None)
    if _is_num(val):
        return ("num", float(val))
    if isinstance(val, str):
        return ("str", val)
    if isinstance(val, list):
        return ("list", tuple(_canonical_key(x) for x in val))
    if isinstance(val, dict):
        return ("dict", tuple((k, _canonical_key(val[k])) for k in sorted(val.keys())))
    return ("other", repr(val))


def _json_type_matches(val: Any, expected: str) -> bool:
    if expected == "null":
        return val is None
    if expected == "boolean":
        return isinstance(val, bool)
    if expected == "integer":
        return _is_int(val)
    if expected == "number":
        return _is_num(val)
    if expected == "string":
        return isinstance(val, str)
    if expected == "array":
        return isinstance(val, list)
    if expected == "object":
        return isinstance(val, dict)
    return True


class JsonSchemaValidator:
    """Pure-Python JSON Schema subset validator (Draft-07 & Draft 2020-12 core)."""

    def __init__(self, schemas_by_name: Dict[str, Dict[str, Any]]) -> None:
        self.schemas_by_name = schemas_by_name

    def _resolve_pointer(self, root_schema: Dict[str, Any], pointer: str) -> Any:
        if pointer in ("", "#"):
            return root_schema
        if not pointer.startswith("#/"):
            raise ValueError(f"unsupported JSON pointer '{pointer}'")
        cur: Any = root_schema
        for raw_part in pointer[2:].split("/"):
            part = raw_part.replace("~1", "/").replace("~0", "~")
            if isinstance(cur, dict) and part in cur:
                cur = cur[part]
            elif isinstance(cur, list):
                idx = int(part)
                cur = cur[idx]
            else:
                raise KeyError(f"pointer '{pointer}' missing segment '{part}'")
        return cur

    def _resolve_ref(
        self, ref: str, current_root: Dict[str, Any]
    ) -> Tuple[Any, Dict[str, Any]]:
        if ref.startswith("#"):
            return self._resolve_pointer(current_root, ref), current_root
        if "#" in ref:
            file_part, ptr_part = ref.split("#", 1)
            ptr = f"#{ptr_part}" if not ptr_part.startswith("#") else ptr_part
        else:
            file_part, ptr = ref, "#"
        target_name = Path(file_part).name
        if target_name not in self.schemas_by_name:
            raise KeyError(f"referenced schema '{file_part}' not found")
        target_root = self.schemas_by_name[target_name]
        return self._resolve_pointer(target_root, ptr), target_root

    def validate(
        self,
        value: Any,
        schema: Any,
        *,
        path: str = "$",
        root_schema: Optional[Dict[str, Any]] = None,
        active_refs: Optional[Set[Tuple[int, int]]] = None,
    ) -> List[str]:
        if root_schema is None and isinstance(schema, dict):
            root_schema = schema
        if active_refs is None:
            active_refs = set()

        if isinstance(schema, bool):
            return [] if schema else [f"{path}: disallowed by false schema"]
        if not isinstance(schema, dict):
            return [f"{path}: invalid non-object schema {schema!r}"]

        errors: List[str] = []

        # $ref
        if "$ref" in schema:
            ref_str = str(schema["$ref"])
            try:
                assert root_schema is not None
                ref_target, ref_root = self._resolve_ref(ref_str, root_schema)
            except Exception as exc:
                return [f"{path}: failed to resolve $ref '{ref_str}' ({exc})"]
            cycle_key = (id(ref_target), id(value))
            if cycle_key not in active_refs:
                next_active = active_refs | {cycle_key}
                errors.extend(
                    self.validate(
                        value,
                        ref_target,
                        path=path,
                        root_schema=ref_root,
                        active_refs=next_active,
                    )
                )

        # Boolean combinators
        if "allOf" in schema and isinstance(schema["allOf"], list):
            for sub in schema["allOf"]:
                errors.extend(
                    self.validate(value, sub, path=path, root_schema=root_schema, active_refs=active_refs)
                )

        if "anyOf" in schema and isinstance(schema["anyOf"], list):
            matched_any = any(
                not self.validate(value, sub, path=path, root_schema=root_schema, active_refs=active_refs)
                for sub in schema["anyOf"]
            )
            if not matched_any:
                errors.append(f"{path}: value {value!r} failed all 'anyOf' branches")

        if "oneOf" in schema and isinstance(schema["oneOf"], list):
            matches = sum(
                1
                for sub in schema["oneOf"]
                if not self.validate(value, sub, path=path, root_schema=root_schema, active_refs=active_refs)
            )
            if matches != 1:
                errors.append(f"{path}: expected exactly 1 'oneOf' match, got {matches}")

        if "not" in schema:
            if not self.validate(
                value, schema["not"], path=path, root_schema=root_schema, active_refs=active_refs
            ):
                errors.append(f"{path}: value must not match 'not' subschema")

        if "if" in schema:
            if_errs = self.validate(
                value, schema["if"], path=path, root_schema=root_schema, active_refs=active_refs
            )
            if not if_errs and "then" in schema:
                errors.extend(
                    self.validate(
                        value, schema["then"], path=path, root_schema=root_schema, active_refs=active_refs
                    )
                )
            elif if_errs and "else" in schema:
                errors.extend(
                    self.validate(
                        value, schema["else"], path=path, root_schema=root_schema, active_refs=active_refs
                    )
                )

        # const & enum
        if "const" in schema and not _json_equal(value, schema["const"]):
            errors.append(f"{path}: expected const {schema['const']!r}, got {value!r}")

        if "enum" in schema and isinstance(schema["enum"], list):
            if not any(_json_equal(value, option) for option in schema["enum"]):
                errors.append(f"{path}: value {value!r} not in enum {schema['enum']!r}")

        # type
        if "type" in schema:
            t_spec = schema["type"]
            allowed_types = [t_spec] if isinstance(t_spec, str) else list(t_spec)
            if not any(_json_type_matches(value, t) for t in allowed_types):
                errors.append(
                    f"{path}: expected type {t_spec!r}, got {type(value).__name__} ({value!r})"
                )
                return errors

        # Numeric constraints
        if _is_num(value):
            fval = float(value)
            if "minimum" in schema and fval < float(schema["minimum"]):
                errors.append(f"{path}: {value} < minimum {schema['minimum']}")
            if "maximum" in schema and fval > float(schema["maximum"]):
                errors.append(f"{path}: {value} > maximum {schema['maximum']}")
            if "exclusiveMinimum" in schema:
                ex_min = schema["exclusiveMinimum"]
                if isinstance(ex_min, bool):
                    if ex_min and "minimum" in schema and fval <= float(schema["minimum"]):
                        errors.append(f"{path}: {value} <= exclusive minimum {schema['minimum']}")
                elif _is_num(ex_min) and fval <= float(ex_min):
                    errors.append(f"{path}: {value} <= exclusiveMinimum {ex_min}")
            if "exclusiveMaximum" in schema:
                ex_max = schema["exclusiveMaximum"]
                if isinstance(ex_max, bool):
                    if ex_max and "maximum" in schema and fval >= float(schema["maximum"]):
                        errors.append(f"{path}: {value} >= exclusive maximum {schema['maximum']}")
                elif _is_num(ex_max) and fval >= float(ex_max):
                    errors.append(f"{path}: {value} >= exclusiveMaximum {ex_max}")
            if "multipleOf" in schema and _is_num(schema["multipleOf"]):
                try:
                    if Decimal(str(value)) % Decimal(str(schema["multipleOf"])) != 0:
                        errors.append(f"{path}: {value} is not a multipleOf {schema['multipleOf']}")
                except (InvalidOperation, ZeroDivisionError):
                    errors.append(f"{path}: invalid multipleOf check against {schema['multipleOf']}")

        # String constraints
        if isinstance(value, str):
            if "minLength" in schema and len(value) < int(schema["minLength"]):
                errors.append(f"{path}: string length {len(value)} < minLength {schema['minLength']}")
            if "maxLength" in schema and len(value) > int(schema["maxLength"]):
                errors.append(f"{path}: string length {len(value)} > maxLength {schema['maxLength']}")
            if "pattern" in schema and isinstance(schema["pattern"], str):
                if re.search(schema["pattern"], value) is None:
                    errors.append(f"{path}: string {value!r} does not match pattern {schema['pattern']!r}")

        # Array constraints
        if isinstance(value, list):
            if "minItems" in schema and len(value) < int(schema["minItems"]):
                errors.append(f"{path}: array length {len(value)} < minItems {schema['minItems']}")
            if "maxItems" in schema and len(value) > int(schema["maxItems"]):
                errors.append(f"{path}: array length {len(value)} > maxItems {schema['maxItems']}")
            if schema.get("uniqueItems") is True:
                seen: Set[Any] = set()
                for idx, elem in enumerate(value):
                    key = _canonical_key(elem)
                    if key in seen:
                        errors.append(f"{path}[{idx}]: duplicate array item violates uniqueItems")
                        break
                    seen.add(key)

            if "prefixItems" in schema and isinstance(schema["prefixItems"], list):
                prefix = schema["prefixItems"]
                for idx, elem in enumerate(value):
                    if idx < len(prefix):
                        errors.extend(
                            self.validate(
                                elem,
                                prefix[idx],
                                path=f"{path}[{idx}]",
                                root_schema=root_schema,
                                active_refs=active_refs,
                            )
                        )
                    elif "items" in schema:
                        errors.extend(
                            self.validate(
                                elem,
                                schema["items"],
                                path=f"{path}[{idx}]",
                                root_schema=root_schema,
                                active_refs=active_refs,
                            )
                        )
            elif "items" in schema:
                items_spec = schema["items"]
                if isinstance(items_spec, list):
                    for idx, elem in enumerate(value):
                        if idx < len(items_spec):
                            errors.extend(
                                self.validate(
                                    elem,
                                    items_spec[idx],
                                    path=f"{path}[{idx}]",
                                    root_schema=root_schema,
                                    active_refs=active_refs,
                                )
                            )
                        elif "additionalItems" in schema:
                            errors.extend(
                                self.validate(
                                    elem,
                                    schema["additionalItems"],
                                    path=f"{path}[{idx}]",
                                    root_schema=root_schema,
                                    active_refs=active_refs,
                                )
                            )
                else:
                    for idx, elem in enumerate(value):
                        errors.extend(
                            self.validate(
                                elem,
                                items_spec,
                                path=f"{path}[{idx}]",
                                root_schema=root_schema,
                                active_refs=active_refs,
                            )
                        )

            if "contains" in schema:
                contains_matches = sum(
                    1
                    for elem in value
                    if not self.validate(
                        elem,
                        schema["contains"],
                        path=path,
                        root_schema=root_schema,
                        active_refs=active_refs,
                    )
                )
                min_c = int(schema.get("minContains", 1))
                max_c = schema.get("maxContains")
                if contains_matches < min_c:
                    errors.append(
                        f"{path}: expected at least {min_c} elements matching 'contains', got {contains_matches}"
                    )
                if max_c is not None and contains_matches > int(max_c):
                    errors.append(
                        f"{path}: expected at most {max_c} elements matching 'contains', got {contains_matches}"
                    )

        # Object constraints
        if isinstance(value, dict):
            if "minProperties" in schema and len(value) < int(schema["minProperties"]):
                errors.append(
                    f"{path}: object has {len(value)} properties < minProperties {schema['minProperties']}"
                )
            if "maxProperties" in schema and len(value) > int(schema["maxProperties"]):
                errors.append(
                    f"{path}: object has {len(value)} properties > maxProperties {schema['maxProperties']}"
                )
            if "required" in schema and isinstance(schema["required"], list):
                for req_key in schema["required"]:
                    if req_key not in value:
                        errors.append(f"{path}: missing required property '{req_key}'")

            if "propertyNames" in schema:
                for key in value:
                    errors.extend(
                        self.validate(
                            key,
                            schema["propertyNames"],
                            path=f"{path}.<key:{key}>",
                            root_schema=root_schema,
                            active_refs=active_refs,
                        )
                    )

            if "dependentRequired" in schema and isinstance(schema["dependentRequired"], dict):
                for trigger_key, req_list in schema["dependentRequired"].items():
                    if trigger_key in value and isinstance(req_list, list):
                        for dep_key in req_list:
                            if dep_key not in value:
                                errors.append(
                                    f"{path}: '{trigger_key}' requires dependent property '{dep_key}'"
                                )

            props = schema.get("properties", {}) if isinstance(schema.get("properties"), dict) else {}
            pat_props = (
                schema.get("patternProperties", {})
                if isinstance(schema.get("patternProperties"), dict)
                else {}
            )

            evaluated_keys: Set[str] = set()
            for key, subschema in props.items():
                if key in value:
                    evaluated_keys.add(key)
                    errors.extend(
                        self.validate(
                            value[key],
                            subschema,
                            path=f"{path}.{key}",
                            root_schema=root_schema,
                            active_refs=active_refs,
                        )
                    )

            for key, val in value.items():
                for pat, subschema in pat_props.items():
                    if re.search(pat, key):
                        evaluated_keys.add(key)
                        errors.extend(
                            self.validate(
                                val,
                                subschema,
                                path=f"{path}.{key}",
                                root_schema=root_schema,
                                active_refs=active_refs,
                            )
                        )

            if "additionalProperties" in schema:
                add_spec = schema["additionalProperties"]
                for key, val in value.items():
                    if key in evaluated_keys:
                        continue
                    if add_spec is False:
                        errors.append(f"{path}: unexpected additionalProperty '{key}'")
                    elif isinstance(add_spec, dict):
                        errors.extend(
                            self.validate(
                                val,
                                add_spec,
                                path=f"{path}.{key}",
                                root_schema=root_schema,
                                active_refs=active_refs,
                            )
                        )

        return errors


def _extract_entries(
    raw: Any, collection_keys: Sequence[str]
) -> Tuple[Optional[List[Any]], str]:
    if isinstance(raw, list):
        return raw, "array"
    if isinstance(raw, dict):
        for key in collection_keys:
            val = raw.get(key)
            if isinstance(val, list):
                return val, f"object.{key}"
    return None, "unknown"


def _schema_expects_collection_root(
    schema: Dict[str, Any], collection_keys: Sequence[str]
) -> bool:
    if schema.get("type") == "array" or "items" in schema:
        return True
    props = schema.get("properties")
    if isinstance(props, dict) and any(k in props for k in collection_keys):
        return True
    return False


def _detect_cycle(graph: Dict[str, List[str]]) -> Optional[List[str]]:
    """Return a cycle path [A, ..., A] if directed graph has a cycle, else None."""
    state: Dict[str, int] = {}  # 0/absent=unvisited, 1=visiting, 2=done
    stack: List[str] = []

    def dfs(node: str) -> Optional[List[str]]:
        state[node] = 1
        stack.append(node)
        for nxt in graph.get(node, []):
            nxt_state = state.get(nxt, 0)
            if nxt_state == 1:
                idx = stack.index(nxt)
                return stack[idx:] + [nxt]
            if nxt_state == 0:
                cyc = dfs(nxt)
                if cyc is not None:
                    return cyc
        stack.pop()
        state[node] = 2
        return None

    for node in sorted(graph.keys()):
        if state.get(node, 0) == 0:
            cyc = dfs(node)
            if cyc is not None:
                return cyc
    return None


def _collect_string_refs(val: Any) -> List[str]:
    if isinstance(val, str):
        return [val] if val.strip() else []
    if isinstance(val, list):
        out: List[str] = []
        for item in val:
            if isinstance(item, str) and item.strip():
                out.append(item)
            elif isinstance(item, dict):
                for key in ("id", "partId", "toolId", "spareId", "zoneId", "slotId"):
                    sub = item.get(key)
                    if isinstance(sub, str) and sub.strip():
                        out.append(sub)
        return out
    return []


def _validate_catalog_uniqueness(
    entries: List[Any], filename: str, report: ValidationReport
) -> Dict[str, Dict[str, Any]]:
    by_id: Dict[str, Dict[str, Any]] = {}
    for idx, entry in enumerate(entries):
        if not isinstance(entry, dict):
            report.error(f"{filename}[{idx}]: catalog entry must be an object")
            continue
        eid = entry.get("id")
        if not isinstance(eid, str) or not eid.strip():
            report.error(f"{filename}[{idx}]: missing or empty string 'id'")
            continue
        if eid in by_id:
            report.error(f"{filename}: duplicate id '{eid}' at index {idx}")
            continue
        by_id[eid] = entry
    return by_id


def _validate_archetypes_and_dag(
    archetypes: Dict[str, Dict[str, Any]],
    parts: Dict[str, Dict[str, Any]],
    report: ValidationReport,
) -> None:
    for req_id in REQUIRED_ARCHETYPE_IDS:
        if req_id not in archetypes:
            report.error(f"archetypes.json: missing required archetype '{req_id}'")

    for arch_id, arch in archetypes.items():
        # Check default part mappings if stored as dict/list
        for field_name in ("defaultParts", "defaultLoadout", "stockParts"):
            dp = arch.get(field_name)
            if isinstance(dp, dict):
                for slot_key, part_ref in dp.items():
                    if part_ref is None:
                        continue
                    if not isinstance(part_ref, str) or part_ref not in parts:
                        report.error(
                            f"archetypes.json[{arch_id}].{field_name}.{slot_key}: "
                            f"unknown part id '{part_ref}'"
                        )
            elif isinstance(dp, list):
                for item in dp:
                    if isinstance(item, str):
                        if item not in parts:
                            report.error(
                                f"archetypes.json[{arch_id}].{field_name}: unknown part id '{item}'"
                            )
                    elif isinstance(item, dict):
                        pid = item.get("partId") or item.get("defaultPartId")
                        if isinstance(pid, str) and pid not in parts:
                            report.error(
                                f"archetypes.json[{arch_id}].{field_name}: unknown part id '{pid}'"
                            )

        # Inspect fasteners[] array on archetype (referencing parts.json requiredToolId)
        arch_fastener_ids: Set[str] = set()
        raw_fasteners = arch.get("fasteners")
        if isinstance(raw_fasteners, list):
            for f_idx, f_entry in enumerate(raw_fasteners):
                if isinstance(f_entry, str) and f_entry:
                    if f_entry in arch_fastener_ids:
                        report.error(f"archetypes.json[{arch_id}].fasteners[{f_idx}]: duplicate fastenerId '{f_entry}'")
                    arch_fastener_ids.add(f_entry)
                elif isinstance(f_entry, dict):
                    fid = f_entry.get("fastenerId") or f_entry.get("id")
                    if not isinstance(fid, str) or not fid.strip():
                        report.error(f"archetypes.json[{arch_id}].fasteners[{f_idx}]: missing fastenerId")
                        continue
                    if fid in arch_fastener_ids:
                        report.error(f"archetypes.json[{arch_id}].fasteners[{f_idx}]: duplicate fastenerId '{fid}'")
                    arch_fastener_ids.add(fid)
                    req_tool = f_entry.get("requiredToolId")
                    if isinstance(req_tool, str) and req_tool and req_tool not in parts:
                        report.error(
                            f"archetypes.json[{arch_id}].fasteners[{fid}].requiredToolId: "
                            f"unknown part id '{req_tool}'"
                        )

        # Inspect assembly slots / DAG
        raw_slots = None
        for slot_key in ("mountSlots", "slots", "assemblySlots", "assemblyGraph"):
            if slot_key in arch:
                raw_slots = arch[slot_key]
                break
        if raw_slots is None:
            continue

        slot_entries: List[Dict[str, Any]] = []
        if isinstance(raw_slots, list):
            slot_entries = [s for s in raw_slots if isinstance(s, dict)]
        elif isinstance(raw_slots, dict):
            for k, v in raw_slots.items():
                if isinstance(v, dict):
                    copy_v = dict(v)
                    copy_v.setdefault("slotId", k)
                    slot_entries.append(copy_v)

        slot_ids: Set[str] = set()
        fastener_ids: Set[str] = set(arch_fastener_ids)
        for idx, slot in enumerate(slot_entries):
            sid = slot.get("slotId") or slot.get("id")
            if not isinstance(sid, str) or not sid.strip():
                report.error(f"archetypes.json[{arch_id}].slots[{idx}]: missing slotId/id")
                continue
            if sid in slot_ids:
                report.error(f"archetypes.json[{arch_id}]: duplicate slot id '{sid}'")
            slot_ids.add(sid)

            slot_fasteners = slot.get("fasteners", [])
            if isinstance(slot_fasteners, list):
                for f in slot_fasteners:
                    if isinstance(f, str) and f:
                        fastener_ids.add(f)
                    elif isinstance(f, dict):
                        fid = f.get("fastenerId") or f.get("id")
                        if isinstance(fid, str) and fid:
                            fastener_ids.add(fid)

            slot_fastener_ids = slot.get("fastenerIds", [])
            if isinstance(slot_fastener_ids, list) and raw_fasteners is not None:
                for fid in slot_fastener_ids:
                    if isinstance(fid, str) and fid not in arch_fastener_ids:
                        report.error(
                            f"archetypes.json[{arch_id}].slot[{sid}].fastenerIds: "
                            f"unknown fastenerId '{fid}'"
                        )

        known_nodes = slot_ids | fastener_ids
        parent_graph: Dict[str, List[str]] = {sid: [] for sid in slot_ids}
        removal_graph: Dict[str, List[str]] = {sid: [] for sid in slot_ids}

        for slot in slot_entries:
            sid = slot.get("slotId") or slot.get("id")
            if not isinstance(sid, str) or sid not in slot_ids:
                continue

            # Slot -> defaultPartId reference & compatibility check
            default_part_id = (
                slot.get("defaultPartId")
                if "defaultPartId" in slot
                else slot.get("partId", slot.get("installedPartId"))
            )
            if isinstance(default_part_id, str) and default_part_id:
                if default_part_id not in parts:
                    report.error(
                        f"archetypes.json[{arch_id}].slot[{sid}]: unknown default part '{default_part_id}'"
                    )
                else:
                    part_obj = parts[default_part_id]
                    allowed_cats = slot.get("allowedCategories") or slot.get("acceptedCategories")
                    if isinstance(allowed_cats, list) and allowed_cats:
                        part_cat = part_obj.get("category")
                        if part_cat not in allowed_cats:
                            report.error(
                                f"archetypes.json[{arch_id}].slot[{sid}]: default part '{default_part_id}' "
                                f"category '{part_cat}' not in {allowed_cats!r}"
                            )
                    req_ifaces = slot.get("compatibleInterfaces") or slot.get("requiredInterfaces")
                    part_ifaces = part_obj.get("compatibleInterfaces")
                    if (
                        isinstance(req_ifaces, list)
                        and req_ifaces
                        and isinstance(part_ifaces, list)
                        and part_ifaces
                    ):
                        if not (set(req_ifaces) & set(part_ifaces)):
                            report.error(
                                f"archetypes.json[{arch_id}].slot[{sid}]: part '{default_part_id}' "
                                f"interfaces {part_ifaces!r} do not intersect slot interfaces {req_ifaces!r}"
                            )

            # Mount hierarchy parent check
            parent_id = slot.get("parentSlotId")
            if parent_id is None:
                parent_id = slot.get("parentId", slot.get("parentSlot"))
            if isinstance(parent_id, str) and parent_id:
                if parent_id not in slot_ids:
                    report.error(
                        f"archetypes.json[{arch_id}].slot[{sid}]: unknown parentSlotId '{parent_id}'"
                    )
                elif parent_id == sid:
                    report.error(f"archetypes.json[{arch_id}].slot[{sid}]: self-parent cycle")
                else:
                    parent_graph[sid].append(parent_id)

            # Removal precondition DAG check
            for blk_key in ("blockedBy", "removalPreconditions", "requiresRemoved"):
                blockers = slot.get(blk_key)
                if isinstance(blockers, list):
                    for blk in blockers:
                        if not isinstance(blk, str) or not blk:
                            continue
                        if blk not in known_nodes:
                            report.error(
                                f"archetypes.json[{arch_id}].slot[{sid}].{blk_key}: unknown node '{blk}'"
                            )
                        elif blk == sid:
                            report.error(
                                f"archetypes.json[{arch_id}].slot[{sid}].{blk_key}: self-blocking cycle"
                            )
                        elif blk in slot_ids:
                            removal_graph[sid].append(blk)

        parent_cycle = _detect_cycle(parent_graph)
        if parent_cycle is not None:
            report.error(
                f"archetypes.json[{arch_id}]: cycle in mount parent hierarchy: {' -> '.join(parent_cycle)}"
            )

        removal_cycle = _detect_cycle(removal_graph)
        if removal_cycle is not None:
            report.error(
                f"archetypes.json[{arch_id}]: cycle in removal preconditions DAG: {' -> '.join(removal_cycle)}"
            )

    report.passed(f"archetypes.json: {len(archetypes)} archetypes, part refs, and slot DAGs acyclic")


def _build_part_capability_index(parts: Dict[str, Dict[str, Any]]) -> Set[str]:
    """Collect valid part IDs plus any explicit toolType/spareType capability keys."""
    valid_refs: Set[str] = set(parts.keys())
    for part in parts.values():
        stats = part.get("stats")
        if isinstance(stats, dict):
            for key in ("toolType", "toolKind", "spareType", "capability"):
                v = stats.get(key)
                if isinstance(v, str) and v.strip():
                    valid_refs.add(v.strip())
        for key in ("toolType", "tags"):
            v = part.get(key)
            if isinstance(v, str) and v.strip():
                valid_refs.add(v.strip())
            elif isinstance(v, list):
                for item in v:
                    if isinstance(item, str) and item.strip():
                        valid_refs.add(item.strip())
    return valid_refs


def _validate_faults(
    faults: Dict[str, Dict[str, Any]],
    parts: Dict[str, Dict[str, Any]],
    report: ValidationReport,
) -> None:
    for req_id in REQUIRED_FAULT_IDS:
        if req_id not in faults:
            report.error(f"faults.json: missing required fault '{req_id}'")

    valid_tool_spare_refs = _build_part_capability_index(parts)

    def check_tool_obj(fault_id: str, obj: Dict[str, Any], ctx: str) -> None:
        for key in (
            "requiredTools",
            "repairToolIds",
            "requiredSpares",
            "consumedSpares",
            "sparePartIds",
            "toolRollRequirements",
            "toolIds",
            "spareIds",
            "alternativeTools",
        ):
            for ref in _collect_string_refs(obj.get(key)):
                if ref not in valid_tool_spare_refs:
                    report.error(
                        f"faults.json[{fault_id}].{ctx}{key}: unknown tool/spare reference '{ref}'"
                    )

    for fault_id, fault in faults.items():
        check_tool_obj(fault_id, fault, "")
        roadside = fault.get("roadsideFix") or fault.get("roadsideRepair")
        if isinstance(roadside, dict):
            check_tool_obj(fault_id, roadside, "roadsideFix.")
        options = fault.get("repairOptions")
        if isinstance(options, list):
            for idx, opt in enumerate(options):
                if isinstance(opt, dict):
                    check_tool_obj(fault_id, opt, f"repairOptions[{idx}].")

    report.passed(f"faults.json: {len(faults)} faults and tool/spare references valid")


def _validate_zones_and_jobs(
    zones: Dict[str, Dict[str, Any]],
    jobs: Dict[str, Dict[str, Any]],
    parts: Dict[str, Dict[str, Any]],
    report: ValidationReport,
) -> None:
    for req_zone in REQUIRED_ZONE_IDS:
        if req_zone not in zones:
            report.error(f"zones.json: missing required zone '{req_zone}'")

    for zone_id, zone in zones.items():
        for adj_key in ("connectedZones", "adjacentZones", "neighborZoneIds"):
            for ref in _collect_string_refs(zone.get(adj_key)):
                if ref not in zones:
                    report.error(f"zones.json[{zone_id}].{adj_key}: unknown zone '{ref}'")

    for job_id, job in jobs.items():
        for z_key in (
            "zoneId",
            "startZoneId",
            "targetZoneId",
            "endZoneId",
            "originZoneId",
            "destinationZoneId",
            "zones",
            "routeZoneIds",
        ):
            for z_ref in _collect_string_refs(job.get(z_key)):
                if z_ref not in zones:
                    report.error(f"jobs.json[{job_id}].{z_key}: unknown zone '{z_ref}'")

        checkpoints = job.get("checkpoints")
        if isinstance(checkpoints, list):
            for idx, cp in enumerate(checkpoints):
                if isinstance(cp, dict):
                    z_ref = cp.get("zoneId")
                    if isinstance(z_ref, str) and z_ref and z_ref not in zones:
                        report.error(
                            f"jobs.json[{job_id}].checkpoints[{idx}].zoneId: unknown zone '{z_ref}'"
                        )

        for p_key in ("requiredPartIds", "requiredParts", "rewardPartIds", "recommendedPartIds"):
            for p_ref in _collect_string_refs(job.get(p_key)):
                if p_ref not in parts:
                    report.error(f"jobs.json[{job_id}].{p_key}: unknown part '{p_ref}'")

    report.passed(f"zones.json ({len(zones)}) & jobs.json ({len(jobs)}) cross-references valid")


def _validate_save_schema_contract(save_schema: Dict[str, Any], report: ValidationReport) -> None:
    props = save_schema.get("properties")
    req = save_schema.get("required")
    if not isinstance(props, dict):
        report.error("save_state.schema.json: missing top-level 'properties'")
        return
    for key in REQUIRED_SAVE_KEYS:
        if key not in props:
            report.error(f"save_state.schema.json: missing required property definition '{key}'")
    if isinstance(req, list):
        for key in REQUIRED_SAVE_KEYS:
            if key not in req:
                report.warn(f"save_state.schema.json: '{key}' is not listed in top-level 'required'")
    report.passed("save_state.schema.json: versioned save contract fields verified")


def run_validation(
    repo_root: Path,
    *,
    strict: bool = False,
    skip_missing_unity: bool = False,  # unused here; kept for uniform orchestrator signature
    allow_partial: bool = False,
) -> ValidationReport:
    del skip_missing_unity
    report = ValidationReport()
    data_root = repo_root / "data"
    schemas_dir = data_root / "schemas"
    catalogs_dir = data_root / "catalogs"

    if not data_root.exists():
        if strict and not allow_partial:
            report.error(f"{data_root}: missing data/ directory")
        else:
            report.skip("data/ not present yet; skipping JSON schema & catalog checks")
        return report

    if not schemas_dir.exists() or not catalogs_dir.exists():
        report.error(f"{data_root}: missing data/schemas or data/catalogs directory")
        return report

    # 1. Load & syntax-check all schemas
    schemas_by_name: Dict[str, Dict[str, Any]] = {}
    for schema_path in sorted(schemas_dir.glob("*.json")):
        try:
            loaded = json.loads(schema_path.read_text(encoding="utf-8"))
        except Exception as exc:
            report.error(f"{schema_path}: invalid JSON ({exc})")
            continue
        if not isinstance(loaded, dict):
            report.error(f"{schema_path}: schema root must be a JSON object")
            continue
        schemas_by_name[schema_path.name] = loaded

    for req_schema in REQUIRED_SCHEMAS:
        if req_schema not in schemas_by_name:
            report.error(f"{schemas_dir / req_schema}: missing required schema file")

    if "save_state.schema.json" in schemas_by_name:
        _validate_save_schema_contract(schemas_by_name["save_state.schema.json"], report)

    schema_validator = JsonSchemaValidator(schemas_by_name)

    # 2. Load & schema-validate all catalogs
    catalog_entries_by_file: Dict[str, Dict[str, Dict[str, Any]]] = {}
    for cat_name, (schema_name, col_keys) in CATALOG_SCHEMA_MAP.items():
        cat_path = catalogs_dir / cat_name
        if not cat_path.is_file():
            report.error(f"{cat_path}: missing required catalog file")
            continue
        try:
            raw_cat = json.loads(cat_path.read_text(encoding="utf-8"))
        except Exception as exc:
            report.error(f"{cat_path}: invalid JSON ({exc})")
            continue

        entries, shape = _extract_entries(raw_cat, col_keys)
        if entries is None:
            report.error(
                f"{cat_path}: expected JSON array or wrapper object containing one of {col_keys!r}"
            )
            continue

        if schema_name in schemas_by_name:
            schema_obj = schemas_by_name[schema_name]
            schema_errs: List[str] = []
            if _schema_expects_collection_root(schema_obj, col_keys):
                schema_errs = schema_validator.validate(raw_cat, schema_obj, path=cat_name)
            else:
                if isinstance(raw_cat, dict):
                    if "schemaVersion" not in raw_cat:
                        schema_errs.append(f"{cat_name}: missing root 'schemaVersion'")
                    elif raw_cat["schemaVersion"] != 1:
                        schema_errs.append(
                            f"{cat_name}.schemaVersion: expected 1, got {raw_cat['schemaVersion']!r}"
                        )
                for idx, entry in enumerate(entries):
                    schema_errs.extend(
                        schema_validator.validate(
                            entry, schema_obj, path=f"{cat_name}[{idx}]"
                        )
                    )
            if schema_errs:
                for err in schema_errs[:50]:
                    report.error(f"Schema error: {err}")
                if len(schema_errs) > 50:
                    report.error(f"{cat_name}: ... and {len(schema_errs) - 50} more schema errors")
            else:
                report.passed(f"{cat_name}: schema valid ({len(entries)} entries, {shape})")

        catalog_entries_by_file[cat_name] = _validate_catalog_uniqueness(entries, cat_name, report)

    # 3. Cross-catalog referential integrity & DAG acyclicity
    parts = catalog_entries_by_file.get("parts.json")
    archetypes = catalog_entries_by_file.get("archetypes.json")
    faults = catalog_entries_by_file.get("faults.json")
    zones = catalog_entries_by_file.get("zones.json")
    jobs = catalog_entries_by_file.get("jobs.json")

    if parts is not None:
        for pid, part in parts.items():
            has_3d = part.get("has3dMesh")
            mesh_path = part.get("meshAssetPath")
            asset_class = part.get("assetClass")
            if has_3d is False:
                if mesh_path is not None and str(mesh_path).strip() != "":
                    report.error(
                        f"parts.json[{pid}]: has3dMesh is false but meshAssetPath is {mesh_path!r}"
                    )
                if asset_class is not None and asset_class != "None":
                    report.error(
                        f"parts.json[{pid}]: has3dMesh is false so assetClass must be 'None', got {asset_class!r}"
                    )
            elif has_3d is True:
                if not isinstance(mesh_path, str) or not mesh_path.strip():
                    report.error(f"parts.json[{pid}]: has3dMesh is true but meshAssetPath is missing")
                if "assetClass" in part and asset_class not in ("Small", "Major", "Garage"):
                    report.error(
                        f"parts.json[{pid}]: has3dMesh is true so assetClass must be Small/Major/Garage, "
                        f"got {asset_class!r}"
                    )
            if isinstance(mesh_path, str) and mesh_path.strip():
                norm = mesh_path.strip().replace("\\", "/")
                if norm.startswith("/") or ".." in norm.split("/"):
                    report.error(f"parts.json[{pid}].meshAssetPath: unsafe path '{mesh_path}'")
                if not norm.lower().endswith(".glb"):
                    report.error(f"parts.json[{pid}].meshAssetPath: expected .glb path, got '{mesh_path}'")

    if parts is not None and archetypes is not None:
        _validate_archetypes_and_dag(archetypes, parts, report)
    if parts is not None and faults is not None:
        _validate_faults(faults, parts, report)
    if parts is not None and zones is not None and jobs is not None:
        _validate_zones_and_jobs(zones, jobs, parts, report)

    # 4. Validate any sample save fixture if present
    if "save_state.schema.json" in schemas_by_name:
        fixtures_dir = data_root / "fixtures"
        if fixtures_dir.is_dir():
            for save_fixture in sorted(fixtures_dir.glob("save*.json")):
                try:
                    save_obj = json.loads(save_fixture.read_text(encoding="utf-8"))
                except Exception as exc:
                    report.error(f"{save_fixture}: invalid JSON ({exc})")
                    continue
                errs = schema_validator.validate(
                    save_obj, schemas_by_name["save_state.schema.json"], path=save_fixture.name
                )
                for err in errs:
                    report.error(f"Save fixture schema error: {err}")
                if not errs:
                    report.passed(f"{save_fixture.name}: valid against save_state.schema.json")

    return report


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    default_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(
        description="Validate MopedGame JSON schemas, catalogs, referential integrity, and DAGs."
    )
    parser.add_argument("--repo-root", type=Path, default=default_root, help="Repository root path")
    parser.add_argument("--strict", action="store_true", help="Fail if data/ directory is missing")
    parser.add_argument("--skip-missing-unity", action="store_true", help="Accepted for CLI parity")
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
        f"Data check summary: {len(report.passes)} passed, "
        f"{len(report.skips)} skipped, {len(report.warnings)} warnings, {len(report.errors)} failed"
    )
    return 1 if report.errors else 0


if __name__ == "__main__":
    sys.exit(main())
