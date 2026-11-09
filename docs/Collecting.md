# Collecting a forest offline

Use this when the forest can't be reached from the machine that makes the slides. Collect the forest on
a machine inside the domain, then bring the snapshot back.

## 1. Build the collector

On the development machine, run:

```powershell
.\tools\Publish-Collector.ps1 -Version 0.1.0
```

The script writes `dist\adtd-0.1.0-win-x64.exe` (about 34 MB) and a `.sha256` file next to it. The exe
is one self-contained file: the target machine doesn't need .NET installed or admin rights.

## 2. Run it inside the domain

Copy the exe to any domain-joined Windows machine (it doesn't have to be a DC), then run:

```powershell
.\adtd-0.1.0-win-x64.exe collect --out forest.json
```

- **Domain:** it uses the machine's own domain. To point it elsewhere, add `--server <dc-or-domain>`.
- **Account:** it runs as the signed-in user, and any domain user can read everything it collects.
  To use another account, add `--user DOMAIN\name`. The password is prompted for and never stored.
- **DNS lookups:** add `--no-addresses` to skip looking up DC IP addresses in DNS.
- **Detail:** add `--verbose` to show every LDAP query as it runs.

The run writes two files:

- `forest.json` is the snapshot.
- `forest.log` lists every query, how many objects it returned, how long it took, and any warnings or
  errors.

Bring both files back. If something looks wrong in the slides, the log usually explains why.

## 3. Check the snapshot

```powershell
adtd info forest.json
```

This prints the forest's levels, FSMO holders, counts, domains, unusual trusts and collection
warnings.

## What it reads

Everything is read over LDAP with signing and sealing. Nothing is written to the directory.

| From | What |
|---|---|
| Configuration partition (any DC) | Domains, sites, NTDS site settings (ISTG, universal group caching), servers and DCs (GC, RODC), replication connections, subnets, site links, site link bridges |
| Schema partition | Schema version and schema master |
| Each domain (one of its own DCs) | PDC, RID and infrastructure masters, trusts, DC computer objects (operating system) |
| Global catalog (only if a domain can't be reached) | That domain's DC operating systems |
| DNS | DC IPv4 addresses |

If a domain can't be reached, it's marked unreachable in the snapshot and collection carries on.

## Privacy

The snapshot holds real server, site, subnet and domain names. Treat it like any other infrastructure
document. In this repository, keep real snapshots in `samples/local/`, which git ignores.
