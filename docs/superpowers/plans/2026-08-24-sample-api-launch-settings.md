# Sample API Launch Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each of the four `samples/` projects a `Properties/launchSettings.json` so `dotnet run --project samples/<X>` binds to a distinct, non-colliding port by default — no more hand-typed `ASPNETCORE_URLS`/`Identity__Authority`/`ASPNETCORE_ENVIRONMENT` juggling required to run more than one at once.

**Architecture:** `dotnet run` on an SDK-style web project (`Microsoft.NET.Sdk.Web`) reads `Properties/launchSettings.json` automatically when present, applying the default profile's `applicationUrl` and `environmentVariables` without any extra flags. Today none of the four sample projects has one (deliberately, per `samples/README.md`), so all four collide on the ASP.NET Core default `http://localhost:5000` unless the operator supplies `ASPNETCORE_URLS` by hand every time. This plan bakes the already-documented port assignments (5081–5084) and the `Identity.Server`/`Orders.Api` issuer/authority pairing into a `launchSettings.json` per project, then rewrites `samples/README.md`'s "Running them" section to match the new (much shorter) reality, and fixes an unrelated uncommitted regression that currently points `examples/Orders.ApiTests` back at the colliding default port.

**Tech Stack:** ASP.NET Core (net10.0) `launchSettings.json` (`Project` command, `applicationUrl`, `environmentVariables`); no new packages.

---

## Context you need before starting

- Port assignments are already documented in `samples/README.md` (lines 48–88, to be rewritten by Task 5) and used consistently in `docs/v0-acceptance.md`:
  - `Catalog.Api` → `http://localhost:5081`
  - `Orders.Api` → `http://localhost:5082`
  - `Inventory.Api` → `http://localhost:5083`
  - `Identity.Server` → `http://localhost:5084`
- `Identity.Server/Program.cs:9` defaults `IssuerUri` to `https://localhost:5443` unless `IdentityServer:IssuerUri` (env var form `IdentityServer__IssuerUri`) is set. `Orders.Api/Program.cs:11` defaults `Identity:Authority` the same way (env var form `Identity__Authority`). These two **must name the same host and port** or Orders.Api's token validation targets an address Identity.Server isn't listening on.
- `Orders.Api/Program.cs:18` sets `RequireHttpsMetadata = builder.Environment.IsProduction()`. With no `launchSettings.json`, `dotnet run` defaults to the `Production` environment, and Orders.Api then refuses a plain-HTTP `Identity:Authority` — every request 500s instead of 401ing. So `Orders.Api`'s launch profile must set `ASPNETCORE_ENVIRONMENT=Development`. `Identity.Server`, `Catalog.Api`, and `Inventory.Api` don't strictly need it (they don't gate on environment), but set it anyway for consistency with how a contributor will actually run these while developing.
- `launchSettings.json` is a `dotnet run`/IDE-time file. It has **no effect on `dotnet build`**, so it does not touch the build-time-generated `samples/*/​*.Api.json` OpenAPI documents, and no effect on `scripts/ci/dogfood.ps1` (which never starts the samples live — confirmed by reading that script).
- `examples/Orders.ApiTests/appsettings.json` currently has an uncommitted change (`git diff`) that reverts `Api:BaseUrl` from `http://localhost:5082/` to `http://localhost:5000/` — the exact colliding default this plan removes reliance on. Task 6 reverts that regression back to `5082` so it matches Orders.Api's baked-in port.

---

### Task 1: `Catalog.Api` launch profile

**Files:**
- Create: `samples/Catalog.Api/Properties/launchSettings.json`

- [ ] **Step 1: Create the file**

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "applicationUrl": "http://localhost:5081",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Step 2: Verify it's picked up**

Run: `dotnet run --project samples/Catalog.Api`
Expected: console output includes `Now listening on: http://localhost:5081`. Stop the process (Ctrl+C) once confirmed.

- [ ] **Step 3: Commit**

```bash
git add samples/Catalog.Api/Properties/launchSettings.json
git commit -m "feat: add launchSettings.json to Catalog.Api for a fixed, non-colliding port"
```

---

### Task 2: `Inventory.Api` launch profile

**Files:**
- Create: `samples/Inventory.Api/Properties/launchSettings.json`

- [ ] **Step 1: Create the file**

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "applicationUrl": "http://localhost:5083",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Step 2: Verify it's picked up**

Run: `dotnet run --project samples/Inventory.Api`
Expected: console output includes `Now listening on: http://localhost:5083`. Stop the process (Ctrl+C) once confirmed.

- [ ] **Step 3: Commit**

```bash
git add samples/Inventory.Api/Properties/launchSettings.json
git commit -m "feat: add launchSettings.json to Inventory.Api for a fixed, non-colliding port"
```

---

### Task 3: `Identity.Server` launch profile

**Files:**
- Create: `samples/Identity.Server/Properties/launchSettings.json`

- [ ] **Step 1: Create the file**

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "applicationUrl": "http://localhost:5084",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "IdentityServer__IssuerUri": "http://localhost:5084"
      }
    }
  }
}
```

- [ ] **Step 2: Verify it's picked up**

Run: `dotnet run --project samples/Identity.Server`
Expected: console output includes `Now listening on: http://localhost:5084`. Stop the process (Ctrl+C) once confirmed.

- [ ] **Step 3: Commit**

```bash
git add samples/Identity.Server/Properties/launchSettings.json
git commit -m "feat: add launchSettings.json to Identity.Server for a fixed, non-colliding port"
```

---

### Task 4: `Orders.Api` launch profile

**Files:**
- Create: `samples/Orders.Api/Properties/launchSettings.json`

- [ ] **Step 1: Create the file**

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "applicationUrl": "http://localhost:5082",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "Identity__Authority": "http://localhost:5084"
      }
    }
  }
}
```

- [ ] **Step 2: Verify it's picked up**

Run: `dotnet run --project samples/Orders.Api`
Expected: console output includes `Now listening on: http://localhost:5082`. It will log a warning/error trying to reach `Identity.Server`'s discovery document if that isn't also running — that's expected and fine for this isolated check. Stop the process (Ctrl+C) once confirmed the port is right.

- [ ] **Step 3: Commit**

```bash
git add samples/Orders.Api/Properties/launchSettings.json
git commit -m "feat: add launchSettings.json to Orders.Api for a fixed, non-colliding port"
```

---

### Task 5: Rewrite `samples/README.md`'s "Running them" section

**Files:**
- Modify: `samples/README.md:48-91` (the "Running them" section, from `None of the four sets a port...` through the `Each exposes GET /health/ready...` paragraph)

The current text (`samples/README.md:50-51`) says *"None of the four sets a port in source, an `appsettings.json`, or a `launchSettings.json` (there is none)"* and spends two long paragraphs explaining a manual `ASPNETCORE_URLS`/`IdentityServer__IssuerUri`/`Identity__Authority`/`ASPNETCORE_ENVIRONMENT` dance. That's no longer true after Tasks 1–4, and the manual dance is no longer required for the default ports. Replace the whole "Running them" section (everything between the `## Running them` heading and the `### Configuring a generated suite against these` heading) with:

- [ ] **Step 1: Replace the section**

```markdown
## Running them

Each of the four projects ships a `Properties/launchSettings.json` pinning it to a fixed,
non-colliding port, so `dotnet run --project samples/<Project>` just works — no environment
variables to type by hand:

```bash
dotnet run --project samples/Catalog.Api      # http://localhost:5081
dotnet run --project samples/Identity.Server  # http://localhost:5084 — required only by Orders.Api
dotnet run --project samples/Orders.Api       # http://localhost:5082
dotnet run --project samples/Inventory.Api    # http://localhost:5083
```

`Orders.Api` needs `Identity.Server` reachable to validate tokens — start both when working with
Orders.Api. Their launch profiles already carry the pairing that makes this work:
`Identity.Server`'s profile sets `IdentityServer__IssuerUri=http://localhost:5084` (the address it
actually listens on, overriding the `https://localhost:5443` default in
`Identity.Server/Program.cs:9`), `Orders.Api`'s profile sets
`Identity__Authority=http://localhost:5084` to match, and both set
`ASPNETCORE_ENVIRONMENT=Development` — required because `Orders.Api/Program.cs:18` sets
`RequireHttpsMetadata = builder.Environment.IsProduction()`, so a plain-HTTP authority is only
accepted outside Production.

Confirmed by measurement, not merely by `/health/ready`, which is anonymous and would pass even
if the pairing above were wrong: with all four running as shown, `GET /api/orders` on `Orders.Api`
with no `Authorization` header returns `401`; requesting a token from `Identity.Server`
(`POST /connect/token`, `client_id=orders-client`) and retrying the same request with
`Authorization: Bearer <token>` returns `200` with the seeded order list.

To run on different ports, edit the relevant `Properties/launchSettings.json` (or pass
`ASPNETCORE_URLS`, which overrides `applicationUrl` for a single invocation) — nothing below
depends on these specific numbers, but each project's `Api:BaseUrl` (or
`Identity:Authority`/`IdentityServer:IssuerUri` for the identity pair, kept equal to each other)
must then point at whatever you actually chose.

Each exposes `GET /health/ready`. Each writes its OpenAPI document beside its project file at
build time, so `intest` can read an artifact rather than needing a running instance.
```

- [ ] **Step 2: Read the file back and confirm the section renders correctly**

Run: read `samples/README.md` and confirm the `## Running them` section now ends right before `### Configuring a generated suite against these`, with no leftover duplicate paragraphs from the old text.

- [ ] **Step 3: Commit**

```bash
git add samples/README.md
git commit -m "docs: update samples/README.md now that launchSettings.json ships per project"
```

---

### Task 6: Fix the `examples/Orders.ApiTests` port regression

**Files:**
- Modify: `examples/Orders.ApiTests/appsettings.json:16`

There is a pre-existing uncommitted change to this file that reverted `Api:BaseUrl` from
`http://localhost:5082/` (the documented, now-baked-in Orders.Api port) to
`http://localhost:5000/` (the colliding ASP.NET Core default). This task fixes it back, so this
committed test project stays consistent with Task 4's launch profile.

- [ ] **Step 1: Confirm the current (regressed) state**

Run: `git diff examples/Orders.ApiTests/appsettings.json`
Expected: shows `-  "Api": { "BaseUrl": "http://localhost:5082/", "Audience": "orders-api" },` /
`+  "Api": { "BaseUrl": "http://localhost:5000/", "Audience": "orders-api" },`

- [ ] **Step 2: Fix the line**

In `examples/Orders.ApiTests/appsettings.json`, change:

```json
  "Api": { "BaseUrl": "http://localhost:5000/", "Audience": "orders-api" },
```

to:

```json
  "Api": { "BaseUrl": "http://localhost:5082/", "Audience": "orders-api" },
```

- [ ] **Step 3: Verify the file now matches HEAD**

Run: `git diff examples/Orders.ApiTests/appsettings.json`
Expected: no output (file matches the committed version).

- [ ] **Step 4: No commit needed**

This restores the file to its committed state — there is nothing new to commit for this task. (If Task 5 or another task has already staged other files, do not `git add` this one along with them by accident; confirm `git status` shows it clean before moving on.)

---

### Task 7: Full verification pass — all four running together

**Files:** none (verification only, no code changes)

- [ ] **Step 1: Build the solution**

Run: `dotnet build InTest.sln`
Expected: builds with 0 errors (warnings-as-errors is on, per `Directory.Build.props`, so 0
warnings too).

- [ ] **Step 2: Start all four sample APIs in the background, each in its own terminal/process**

```bash
dotnet run --project samples/Identity.Server &
dotnet run --project samples/Catalog.Api &
dotnet run --project samples/Orders.Api &
dotnet run --project samples/Inventory.Api &
```

Expected: four processes, each logging its own `Now listening on:` line for a distinct port
(5084, 5081, 5082, 5083 respectively) — no `Failed to bind to address` errors from a collision.

- [ ] **Step 3: Confirm the auth pairing still works end-to-end**

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5082/api/orders
```

Expected: `401` (no `Authorization` header).

```bash
TOKEN=$(curl -s -X POST http://localhost:5084/connect/token \
  -d "client_id=orders-client&client_secret=orders-secret&grant_type=client_credentials&scope=orders.read" \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['access_token'])")
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5082/api/orders -H "Authorization: Bearer $TOKEN"
```

Expected: `200`. (If the client id/secret above don't match what `Identity.Server` actually seeds,
read `samples/Identity.Server`'s client configuration to get the right ones — the point of this
step is confirming `200` after a token exchange, not these exact literal values.)

- [ ] **Step 4: Stop all four processes**

```bash
kill %1 %2 %3 %4
```

- [ ] **Step 5: Run the fast and dogfood test suites to confirm nothing else broke**

Run: `dotnet test tests/InTest.Architecture.Tests tests/InTest.Cli.Tests tests/InTest.Runtime.Tests`
Expected: all pass (this plan touches no source `InTest.Cli`/`InTest.Runtime` code, so this is a
regression check, not expected to find anything).

Run: `pwsh scripts/ci/dogfood.ps1 -RepoRoot . -ScaffoldRoot <dir-outside-the-checkout> -CliDll <path-to-built-InTest.Cli.dll>`
Expected: passes — this script never starts the samples live, so it should be unaffected by this
plan, but it's cheap to confirm.

- [ ] **Step 6: Final `git status` check**

Run: `git status`
Expected: clean except for the intentional commits from Tasks 1–5 (already committed) and
whatever unrelated pre-existing changes (`src/InTest.Cli/Spec/SpecFetcher.cs`,
`tests/InTest.Cli.Tests/SpecFetcherTests.cs`, and the line-ending-only diffs on
`samples/Catalog.Api/Catalog.Api.json` / `samples/Orders.Api/Orders.Api.json`) were already
present before this plan started and are out of scope for it — do not commit them as part of this
work.

---

## Self-review notes

- **Spec coverage:** "run without port collision" → Tasks 1–4 (fixed distinct ports baked into
  each project), Task 5 (docs no longer describe a manual workaround), Task 6 (test config
  consistency), Task 7 (proves all four coexist and the auth pairing still works).
- **No placeholders:** every step has literal file content or literal commands; the only bracketed
  values (`<dir-outside-the-checkout>`, `<path-to-built-InTest.Cli.dll>`) are pre-existing
  parameters to `scripts/ci/dogfood.ps1` documented in `CLAUDE.md`, not deferred plan content.
- **Consistency:** port numbers (5081/5082/5083/5084) and env var names
  (`IdentityServer__IssuerUri`, `Identity__Authority`, `ASPNETCORE_ENVIRONMENT`) match verbatim
  across Tasks 1–5 and the existing `examples/Orders.ApiTests/appsettings.json` /
  `examples/Catalog.ApiTests/appsettings.json` (Task 6 restores the latter's already-correct
  values as the source of truth for Orders').
