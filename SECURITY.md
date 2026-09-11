# Security Policy — Printer Rescue

## Supported versions

| Version | Supported |
|---|---|
| 1.0.x | ✅ |

## Reporting a vulnerability

**Do NOT open a public issue.** Report via the repository's security contact
(GitHub Security Advisories → "Report a vulnerability").

Include: affected version, reproduction steps, estimated impact, proof of
concept if any. Response within 72 h; target fix within 30 days for
high-severity vulnerabilities.

## Attack surface and relevant design decisions

- **No telemetry, no own network**: the app only talks to the local print
  subsystem and to the target printer (TCP 9100/515/631 where applicable).
- **Local snapshots**: stored in `%ProgramData%\PrinterRescue\snapshots\`,
  read/write restricted to the machine's administrative profile.
- **Hardened parser**: JSON with max depth 16, max size 1 MiB,
  traversal-validated paths — a snapshot is untrusted input when reloaded.
- **RULE #1 as structural defense**: by never downloading or distributing
  drivers, the app cannot be a third-party binary supply-chain vehicle.
- **Least elevation**: destructive operations require explicit admin; the GUI runs
  `asInvoker` and elevates only for repair.

## Out-of-scope

Printers that never worked on the target machine are out of product scope
by design decision (see README § RULE #1).
