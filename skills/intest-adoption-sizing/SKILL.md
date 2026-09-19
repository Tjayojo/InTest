---
name: intest-adoption-sizing
description: Estimate what adopting InTest against an OpenAPI document will cost before committing to it — how many tests it will generate, how many fixtures need hand-written values, whether auth tests will appear at all, and what will block generation. Use when someone asks "should we adopt InTest", "how much work is this spec", "size this API for contract testing", or is choosing between several specs to start with.
---

# Sizing an InTest adoption

InTest generates a committed test project from an OpenAPI document. The generation is instant; the
**cost is the fixtures** — request bodies and path parameters InTest deliberately refuses to invent,
which arrive as `TODO:` sentinels that fail until a human replaces them.

This skill answers "what will that cost me" from the spec alone, before anything is installed.

## Run the estimator first

```bash
python skills/intest-adoption-sizing/size-spec.py path/to/openapi.json
```

It reads the spec only — no install, no API call, nothing written. Point it at several specs at once
to compare candidates.

Its rules are taken from InTest's source and calibrated against the three sample APIs, where it
reproduces the real tool's output exactly: Catalog 13 tests / 8 fixtures, Orders 24 / 5,
Inventory 9 / 4.

## Then interpret it — this is the part that needs you

The script produces numbers. Turn them into a recommendation.

**`Needing hand-editing` is the real cost.** Each is an operation whose test cannot pass until
someone supplies a value that exists in the target environment. Ten operations with no path
parameters is an afternoon; ten POSTs with required bodies against an unfamiliar domain is a
different conversation.

**`JSON request bodies` is the expensive subset.** A body assembled from spec examples is routinely
rejected by a real API — required fields the schema does not mark required, values that must
reference existing rows, formats enforced beyond the schema. Size these as needing a domain expert,
not a spec reader.

**A `security` warning is a correctness issue, not a note.** If the spec declares `security` only at
the document level, InTest generates **no auth tests at all** — it reads operation-level `security`
only. The suite will look complete and silently test nothing about authentication. Say this
plainly; it is the single most consequential thing the estimator can find.

**Blockers are absolute.** YAML is not supported today, from a file or a URL. Neither is OpenAPI
other than 3.x. Do not propose workarounds that involve hand-converting a spec on every change — that
breaks the regeneration loop InTest exists to provide.

## Estimating effort

The honest unit is "operations needing hand-editing", not tests. A rough shape:

| shape | typical cost |
|---|---|
| GET/DELETE with a path id | minutes — one real id per operation, often reusable |
| operation with a required query parameter | minutes, same |
| POST/PUT with a JSON body | the real work — needs someone who knows the domain |

Tests that need **no** hand-editing at all: every declared-error (404) case and every auth case, both
of which use deliberately unmatchable values. Those land green on day one. Say how many there are —
it is usually the encouraging half of the answer.

## The rule you must not break

**Never propose filling a fixture with an invented value.**

InTest's `TODO:` sentinel exists because a plausible-looking value (`"string"`, `0`, a random GUID)
is accepted by a permissive endpoint, so the suite passes while asserting nothing. A red test gets
fixed; a green test that proves nothing never does. Design principle #3 is "Fail loudly... no silent
green", and an agent that bulk-fills fixtures defeats the product's central guarantee while looking
helpful.

If you help fill fixtures at all, a value may come only from:

1. a real response you obtained by calling the API,
2. a value the human supplied, or
3. the spec's own `example`/`default` — and note that InTest itself **refuses** these for path and
   required-query parameters, precisely because a spec's example `id` names a resource that probably
   does not exist in the target environment.

Report the provenance of every value you propose. Anything you cannot source, leave as `TODO:`.

## What this skill does not tell you

- **It is an estimate, not the planner.** The authority is `intest generate`, which reports the real
  numbers after `init`. If they disagree, the tool is right and this script needs correcting.
- **It says nothing about whether tests will pass.** That depends on the deployment, the data and
  the auth setup — only a live run answers it.
- **It does not read `$ref` across files.** A spec split across documents will under-count.
