# InTest.Cli

`intest` is a .NET global tool that generates a committed, owned MSTest project exercising a
**deployed** API over real HTTP, from its OpenAPI document.

```bash
dotnet tool install --global InTest.Cli
intest init --name Orders.ApiTests --spec ./orders.json
intest generate
```

The generated project is a normal MSTest project: committed to your repo, edited by your team,
and run with `dotnet test` like any other test project. `InTest.Cli` is a development-time tool —
it is not a runtime dependency of the tests it generates; that role belongs to
[`InTest.Runtime`](https://www.nuget.org/packages/InTest.Runtime).

Full documentation, including the adoption walkthrough and design rationale, lives in the
[InTest repository](https://github.com/Tjayojo/intest) — start with
[`docs/getting-started.md`](https://github.com/Tjayojo/intest/blob/main/docs/getting-started.md).

MIT licensed.
