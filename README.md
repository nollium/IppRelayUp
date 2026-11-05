# IppRelayUp

Local privilege escalation on domain-joined Windows workstations using IppCoerce (WSD Port Monitor) for HTTP authentication coercion, relayed to LDAP for Shadow Credentials abuse.

## How it works

1. Force-start the WebClient service (no admin needed)
2. Start a local HTTP relay server (default port 8080)
3. Coerce SYSTEM to authenticate via IppCoerce: calls `XcvDataW("AssocIppDirected")` on the WSD Port Monitor, which sends an NTLM-authenticated HTTP POST to our relay server
4. Relay the machine account NTLM auth to the Domain Controller's LDAP(S) service
5. Write a KeyCredential blob into the machine's `msDS-KeyCredentialLink` attribute (Shadow Credentials)
6. Use PKINIT to obtain a TGT for the machine account
7. S4U2Self to get a Service Ticket for a domain admin on the local HOST SPN
8. Authenticate to the local SCM and create a service as SYSTEM ([SCMUACBypass](https://gist.github.com/tyranid/c24cfd1bd141d14d4925043ee7e03c82))

## What is IppCoerce

IppCoerce abuses MS-RPRN RpcXcvData (opnum 88) with the `AssocIppDirected` command on the WSD Port Monitor (`APMon.dll`). It tells the spooler to associate an IPP printer at a given URL, which triggers an outbound HTTP request with the machine's NTLM credentials.

Unlike PetitPotam (MS-EFSR), IppCoerce is **not patched** — it uses a legitimate printing feature rather than an EFS vulnerability.

## Where it works

| OS | Works? | Notes |
|---|---|---|
| **Windows 10** (all builds) | Yes | Local trigger via Win32 API |
| **Windows 11** (all builds including 24H2/25H2/26H2) | Yes | Local trigger via Win32 API |
| Windows Server (any) | No | WSD Port Monitor (`APMon.dll`) not present |

**Requirements on the target machine (defaults on workstation editions):**
- Print Spooler service (running by default)
- WSD Port Monitor / `APMon.dll` (present by default on workstations)
- WebClient service (set to Manual by default, auto-started by the tool)

**Domain requirements:**
- Domain Controller running Windows Server 2016+ (PKINIT support)
- Domain Controller with its own key pair (ADCS or custom CA)

No additional Windows features or roles need to be installed.

## Usage

```
Usage: IppRelayUp.exe -t <target> -c command [options]

Required Arguments:
  -t, --target <host>                LDAP target server (e.g., dc01.contoso.local)
  -c, --command <cmd>                Command to run as SYSTEM via SCM UAC Bypass
  -d, --domain <domain>              Full domain name (e.g. contoso.local)

Optional Arguments:
  -p, --port <port>                  HTTP port to listen on (default: 8080)
  -u, --user-to-impersonate <user>   Username to impersonate (default: Administrator)
  --force                            Force overwrite existing msDS-KeyCredentialLink
  --silver                           UnPAC the machine NT hash and forge tickets locally
  -lp, --ldap-port <port>            LDAP port (default: 389, or 636 for LDAPS)
  -s, --ldaps                        Use LDAPS (default: false)
  -a, --auto                         Disable auto-trigger IppCoerce (enabled by default)
  -v, --verbose                      Verbose output
  -h, --help                         Help

Example:
  IppRelayUp.exe -t dc01.contoso.local -d contoso.local -c cmd.exe
  IppRelayUp.exe -t dc01.contoso.local -d contoso.local -c whoami.exe --silver -s
```

## Building

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

A pre-compiled binary is available in the `release/` folder.

## Mitigation

- **Enforce LDAP Signing and Channel Binding** — mitigates the relay. Configure via "Domain Controller: LDAP server signing requirements" GPO.
- **Disable WebClient service** on workstations where it is not needed.

## Detection

Monitor for Windows event 5136 (directory service object modified) on the `msDS-KeyCredentialLink` attribute:

```
eventID = 5136
AND AttributeLDAPDisplayName = "msDS-KeyCredentialLink"
AND OperationType = "%%14674"   (Value Added)
```

Requires an SACL on GUID `5b47d60f-6090-40b2-9f37-2a4de88f3063`.

## Credits

- [WebClientRelayUp](https://github.com/Hack0ura/WebClientRelayUp) by Raphaël HUON ([@Hack0ura](https://github.com/Hack0ura)) — original project, LDAP relay, Shadow Credentials, S4U2Self, SCM UAC bypass
- [DavRelayUp](https://github.com/Dec0ne/DavRelayUp) by [Dec0ne](https://github.com/Dec0ne) — original relay-up concept
- [Impacket PR #2361](https://github.com/fortra/impacket/pull/2361) — IppCoerce RpcXcvData technique
- [sudonoodle](https://github.com/sudonoodle) — original IppCoerce BOF and technique research
- [James Forshaw](https://twitter.com/tiraniddo) — [SCM UAC Bypass](https://gist.github.com/tyranid/c24cfd1bd141d14d4925043ee7e03c82) using Kerberos Service Tickets
- [ShutdownRepo](https://github.com/ShutdownRepo) — [pyWhisker](https://github.com/ShutdownRepo/pywhisker), Shadow Credentials reference
- [Rubeus](https://github.com/GhostPack/Rubeus/) contributors — PKINIT implementation reference
