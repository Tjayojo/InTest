# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Use GitHub's private vulnerability reporting on this repository
(**Security → Report a vulnerability**), which opens a channel visible only to maintainers.

Useful things to include: what an attacker can do, how to reproduce it, the affected version or
commit, and anything that limits exploitability. A rough report sent promptly beats a polished
one sent late.

You should get an acknowledgement within a few working days. InTest is maintained alongside
other work and does not have a staffed on-call rotation — that is a statement of fact rather
than a service level.

## Supported versions

`InTest.Cli` and `InTest.Runtime` `0.1.0-preview.1` are published to nuget.org (see
`docs/v0-acceptance.md`). It is a v0 prerelease: expect breaking changes before a `0.1.0`
stable release, and report anything you find against it.

The current-major/previous-major, 12-months-supported policy (§3 of the design spec) starts to
apply once a `1.x` major exists. Until then there is no previous major to patch — fixes go to
whatever the current `0.x` prerelease is.

## Scope

InTest generates test code, runs against APIs you point it at, and reads OpenAPI documents you
supply. Reports we are particularly interested in:

- **Generated code that leaks secrets** — into committed files, test output, the `.trx`, or a
  request to an unintended host. §10 exists specifically to keep credentials out of committed
  fixtures, and a gap there is a real finding.
- **A malicious OpenAPI document** causing code execution, path traversal on generation, or a
  denial of service in the parser. Specs are untrusted input: they may come from a third-party
  vendor or a URL.
- **Generated tests sending credentials to a host derived from the spec.** The base URL comes
  from configuration and `servers[]` is deliberately ignored (§7); a path around that is a
  finding.
- **A dependency vulnerability** that reaches consumers of `InTest.Runtime`.

Out of scope, though still worth an ordinary issue:

- Tests that fail against your API. That is usually InTest doing its job.
- Data left behind in your own test environments. Cleanup is best-effort by design and the spec
  says so plainly (§14); run the sweeper it describes.
- Anything requiring an attacker to already control your build or your machine.

## A note on what InTest does by design

Worth stating so it is not reported as a vulnerability, and so you know what you are running:

InTest generates tests that send **real HTTP requests to whatever you configure**, including
requests with deliberately malformed input. The `security` variation category — SQL injection
strings, path traversal, script payloads — is **off by default** because it trips WAFs, can get
an agent's IP blocked, and raises alerts someone has to triage. Turning it on is a decision
about your own systems.

The design also states that InTest targets pre-production environments and adds no guard rails
against being pointed at production. That is deliberate and documented, not an oversight.
