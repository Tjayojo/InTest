# Sample APIs

Three ASP.NET Core APIs and an identity server, built as fixtures for InTest. They serve three
purposes at once: the target for the v0 acceptance run
([`../docs/v0-acceptance.md`](../docs/v0-acceptance.md)), the producer matrix required by §16 of
the design spec, and a corpus a contributor can regenerate against without needing a private
API.

Nothing here is in the dependency closure of `InTest.Cli` or `InTest.Runtime`. Installing
either package pulls none of it.

## The four projects

| Project | Auth | OpenAPI producer | Emits | Why it exists |
|---|---|---|---|---|
| `Catalog.Api` | none | built-in `Microsoft.AspNetCore.OpenApi` | **3.1.1** | Every primitive the OpenAPI type system distinguishes, plus the nullable variant of each. No `operationId`, so synthesis is exercised |
| `Orders.Api` | Duende, client-credentials | Swashbuckle | **3.0.4** | Declares `security` per operation with the scope each needs, so auth contract tests have something to assert |
| `Inventory.Api` | none | NSwag | **3.0.0** | `{Controller}_{Action}` operationIds — stable-looking, but they churn when an action is renamed |
| `Identity.Server` | — | — | — | Two clients, one full-access and one read-only, so a genuine multi-identity token provider exists |

Three producers, three different OpenAPI versions. That was not contrived — it is simply what
each emits by default, and it is worth knowing that a single organisation can meet all three.

## Deliberate variations

- **`Catalog.Api`**: `ProductsController` has no `DELETE` (products are deactivated, never
  removed); `CategoriesController` has one. Generation must follow the spec, not an assumed
  CRUD shape.
- **`Orders.Api`**: `OrdersController` has `DELETE`; `CustomersController` does not. Reads need
  `orders.read`, writes need `orders.write`, so a read-only token receives 403 on writes.
- **`Inventory.Api`**: `StockController` has `DELETE`; `WarehousesController` is read-only. Its
  route parameters are strings and ints rather than GUIDs, so percent-encoding and empty-segment
  behaviour differ from the other two.
- **Status coverage**: 200, 201 with `Location`, 204 (bodiless), 400, 401, 403, 404, and 409
  from real unique indexes and restricted foreign keys.
- **Parameter positions**: route, query and header — the variation catalog is per-position.

## Persistence

File-backed SQLite, created and seeded on startup. A real relational provider is the point:
duplicate SKUs and referenced categories produce genuine 409s from database constraints. The EF
Core InMemory provider enforces neither, so error-path endpoints would return 200 where a real
deployment returns 409.

Seed data uses fixed GUIDs (`aaaaaaaa-…`, `11111111-…`) so tests can reference known rows
without fixtures — which is what made a v0 acceptance run possible before fixtures exist.

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

### Configuring a generated suite against these

Two things the acceptance run got wrong first, both recorded in
[`../docs/v0-acceptance.md`](../docs/v0-acceptance.md):

- **`Api:BaseUrl` must be the origin** — `http://localhost:5081/`, not `http://localhost:5081/api/`.
  These specs' paths already begin `/api/`, and InTest appends them. Getting this wrong gives
  every test a 404 against configuration that looks right.
- **`readiness.path` must be absolute** here, because `/health/ready` sits at the host root
  rather than under `/api`.

## A note on Duende

`Duende.IdentityServer` requires a paid licence for **production** use. Development, testing and
personal projects are free, which is what this is — and it is never in the dependency closure of
a shipped InTest package.

If you lift `Identity.Server` into anything that serves real users, that changes: get a licence.
Running without a key logs a startup warning and does not otherwise restrict the application,
so it is easy to miss.
