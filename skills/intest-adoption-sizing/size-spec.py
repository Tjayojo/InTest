#!/usr/bin/env python3
"""Estimate what adopting InTest against an OpenAPI document will cost.

Reads the spec only. Does not need InTest installed, does not call the API, and writes
nothing -- the whole point is to answer "what will this cost me" *before* adopting.

Every rule below is taken from InTest's own source and verified by running the CLI against
a probe spec. Where a rule is subtle, the comment says which file decides it, so a future
reader can check rather than trust this script.

Usage:  python size-spec.py <spec.json> [<spec.json> ...]
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

NOT_FOUND = "404"


def load(path: Path):
    text = path.read_text(encoding="utf-8-sig")
    return json.loads(text)


def operations(doc):
    """Yield (path, method, operation, path_item) for every real operation.

    `parameters` is a sibling of the HTTP methods on a path item, not an operation -- skipping
    it here is what stops it being counted as a method called "parameters".
    """
    methods = {"get", "put", "post", "delete", "options", "head", "patch", "trace"}
    for path, item in (doc.get("paths") or {}).items():
        if not isinstance(item, dict):
            continue
        for method, op in item.items():
            if method.lower() in methods and isinstance(op, dict):
                yield path, method.lower(), op, item


def effective_parameters(op, path_item):
    """Path-item parameters merged with operation parameters, operation winning on (name, in).

    Mirrors src/InTest.Cli/Spec/EffectiveParameters.cs. Before that landed, path-item
    parameters were invisible to InTest entirely (issue #7).
    """
    merged = []
    seen = set()
    for p in op.get("parameters") or []:
        key = (p.get("name"), p.get("in"))
        seen.add(key)
        merged.append(p)
    for p in path_item.get("parameters") or []:
        key = (p.get("name"), p.get("in"))
        if key not in seen:
            merged.append(p)
    return merged


def has_json_body(op):
    """FixtureComposer.HasJsonBodyToCompose.

    An `application/json` entry with **no schema** is valid OpenAPI and counts as no body --
    there is nothing to compose a value from.
    """
    content = ((op.get("requestBody") or {}).get("content")) or {}
    for media_type, media in content.items():
        if media_type.split(";")[0].strip().lower() == "application/json":
            if isinstance(media, dict) and media.get("schema") is not None:
                return True
    return False


def parameter_produces_a_value(p):
    """FixtureComposer.ParameterValue -- returns None for an optional query parameter with
    neither an example nor a default, and such a parameter does not pull an operation into
    needing a fixture at all.

    Path and required-query always produce a value (a TODO sentinel). Optional query produces
    one only from example or default.
    """
    loc = p.get("in")
    if loc == "path" or (loc == "query" and p.get("required")):
        return True
    if loc != "query":
        return False
    schema = p.get("schema") or {}
    return schema.get("example") is not None or schema.get("default") is not None


def needs_fixture(params, op):
    """FixtureComposer.NeedsFixture: a JSON body to compose, or any path/query parameter that
    actually produces a value."""
    if has_json_body(op):
        return True
    return any(p.get("in") in ("path", "query") and parameter_produces_a_value(p)
               for p in params)


def sentinelled_parameters(params):
    """Parameters that arrive as a TODO: sentinel a human must replace.

    MEASURED, and the single most misunderstood rule here: a **path** parameter, and a
    **required query** parameter, are ALWAYS sentinelled -- even when the spec gives them an
    `example` or a `default`. Only an optional query parameter is filled from example/default.

    That is deliberate, not a gap. A spec's example `id` names a resource that almost certainly
    does not exist in your deployment, so filling it would produce a 404 for the wrong reason,
    or -- worse -- a green test against the wrong resource.

    See FixtureComposer.ParameterValue: `alwaysSentinelled = In is Path || (Required && In is Query)`.
    """
    out = []
    for p in params:
        loc = p.get("in")
        if loc == "path" or (loc == "query" and p.get("required")):
            out.append(p.get("name"))
    return out


def plans_not_found_case(params, op):
    """A declared 404 only becomes a test when an unmatchable request can actually provoke it.

    Declaring `404` is necessary but not sufficient. TestPlanBuilder.TryPlanDeclaredNotFound
    drops it to a coverage *note* (the success case still runs) in three situations, each
    because the alternative is asserting 404 against something an API correctly answers
    differently:

      - no path parameter  -> nowhere to put an unmatchable value
      - a required query parameter -> an id-only request omits it, and a compliant API may
        answer 400 rather than 404
      - a required request body -> strictly stronger version of the same; ASP.NET Core rejects
        a bodyless request with 400 before the NotFound() path runs

    Getting this wrong is what made an earlier version of this script over-count Catalog and
    Inventory by exactly one test each.
    """
    if NOT_FOUND not in (op.get("responses") or {}):
        return False
    if not any(p.get("in") == "path" for p in params):
        return False
    if any(p.get("in") == "query" and p.get("required") for p in params):
        return False
    if (op.get("requestBody") or {}).get("required") is True:
        return False
    return True


def operation_level_security(op):
    """Auth cases come from operation-level `security` ONLY.

    TestPlanBuilder.PlanAuthCases: "Operation-level `security` only -- an empty array here
    explicitly overrides a document-level default to 'no auth', and v1-c does not attempt to
    resolve document-level inheritance."

    So a spec that declares `security` only at the document root generates NO auth tests. That
    is a sizing surprise worth catching before adoption, not after.
    """
    sec = op.get("security")
    return isinstance(sec, list) and len(sec) > 0


def size(path: Path):
    doc = load(path)
    report = {"spec": str(path), "blockers": [], "warnings": []}

    version = str(doc.get("openapi") or "")
    if not version.startswith("3."):
        report["blockers"].append(
            f"openapi is '{version or 'absent'}'; InTest supports 3.x only")
    report["openapi"] = version

    ops = list(operations(doc))
    report["operations"] = len(ops)
    if not ops:
        report["blockers"].append("no operations found under paths")
        return report

    success = len(ops)
    not_found = sum(1 for _, _, op, item in ops
                    if plans_not_found_case(effective_parameters(op, item), op))
    secured = sum(1 for _, _, op, _ in ops if operation_level_security(op))
    # One 401 and one 403 per secured operation.
    report["estimated_tests"] = success + not_found + secured * 2
    report["breakdown"] = {
        "success": success, "declared_404": not_found, "auth": secured * 2}

    fixtures = 0
    todo_total = 0
    ops_with_todo = 0
    body_ops = 0
    no_operation_id = 0

    for _, _, op, item in ops:
        if not op.get("operationId"):
            no_operation_id += 1
        params = effective_parameters(op, item)
        if needs_fixture(params, op):
            fixtures += 1
        if has_json_body(op):
            body_ops += 1
        todos = sentinelled_parameters(params)
        if todos:
            ops_with_todo += 1
            todo_total += len(todos)

    report["fixture_files"] = fixtures
    report["operations_needing_hand_editing"] = ops_with_todo
    report["parameter_todos"] = todo_total
    report["operations_with_json_body"] = body_ops
    report["operations_without_operationid"] = no_operation_id

    if secured == 0 and doc.get("security"):
        report["warnings"].append(
            "`security` is declared at the document level but on no operation. InTest reads "
            "operation-level `security` only, so NO auth tests will be generated. Declare it "
            "per operation to get 401/403 coverage.")
    if body_ops:
        report["warnings"].append(
            f"{body_ops} operation(s) send a JSON body. Each needs a realistic request body; "
            "a body built only from spec examples is often rejected by a real API.")
    if no_operation_id:
        report["warnings"].append(
            f"{no_operation_id} operation(s) declare no operationId, so InTest synthesizes one. "
            "Synthesized names change if the path or method changes.")
    return report


def render(r):
    print(f"\n=== {r['spec']} ===")
    if r["blockers"]:
        for b in r["blockers"]:
            print(f"  BLOCKER: {b}")
        if r.get("operations", 0) == 0:
            return
    print(f"  OpenAPI                {r.get('openapi') or '(absent)'}")
    print(f"  Operations             {r['operations']}")
    b = r["breakdown"]
    print(f"  Tests (estimated)      {r['estimated_tests']}"
          f"  ({b['success']} success + {b['declared_404']} declared-404 + {b['auth']} auth)")
    print(f"  Fixture files          {r['fixture_files']}")
    print(f"  Needing hand-editing   {r['operations_needing_hand_editing']} operation(s), "
          f"{r['parameter_todos']} parameter TODO(s)")
    if r["operations_with_json_body"]:
        print(f"  JSON request bodies    {r['operations_with_json_body']}")
    for w in r["warnings"]:
        print(f"  NOTE: {w}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(2)
    for arg in sys.argv[1:]:
        render(size(Path(arg)))
    print()
