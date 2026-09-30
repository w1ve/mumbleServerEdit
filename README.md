# MumbleServerEdit

**An interactive, cross-platform console tool for administering Mumble / Murmur voice servers over SSH + ZeroC Ice.**

[![License: GPL v2](https://img.shields.io/badge/License-GPL%20v2-blue.svg)](LICENSE)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)
![Version 0.9.0](https://img.shields.io/badge/version-0.9.0-green)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS%20%7C%20Raspberry%20Pi-lightgrey)

MumbleServerEdit connects to a Mumble/Murmur host over SSH, tunnels to the server's
Ice management port, and lets you **add, edit, and delete virtual servers**, manage
**channels**, set the **SuperUser password**, keep **UFW** in sync, **test connectivity**,
and **restart** the service — all from a single self-contained binary. No install, no
runtime prerequisites.

> Works with Mumble/Murmur **through version 1.5**.

---

## ✨ Features

| Area | What you can do |
|---|---|
| 🖥️ **Virtual servers** | List, add (with next-free-port suggestion), edit, delete, start/stop |
| ⚙️ **Full config editing** | Edit-in-place for name, password, port, bandwidth, slots, welcome message, channels, HTML/ping, timeouts, and **every other option the library exposes** |
| 🔑 **SuperUser password** | Set or disable the per-server SuperUser password (write-only Ice call) |
| 🌲 **Channels** | Tree view, add, edit (name / description / position), delete |
| 🔥 **Firewall (UFW)** | Detects UFW, opens the new port on create, and updates rules when a port changes (opens new, closes old) |
| 📡 **Connectivity test** | Checks the host is listening on TCP + UDP for the voice port and that UFW allows it |
| 🔄 **Service restart** | Restarts the `mumble-server` / `murmur` systemd service |
| ✅ **Fail-fast write check** | Right after connecting, does a harmless read-and-write-back to confirm Ice write access before you fill in a form |
| 🔐 **Secure transport** | All traffic rides an SSH tunnel; Ice never needs to be exposed to the internet |

---

## 📦 Downloads

Grab the binary for your platform from the [**latest release**](https://github.com/w1ve/MumbleServerEdit/releases/latest).
Each is a single, self-contained file — the .NET runtime is bundled, so nothing else is required.

| Platform | Asset |
|---|---|
| Windows x64 | `MumbleServerEdit-win-x64.exe` |
| Linux x64 | `MumbleServerEdit-linux-x64` |
| Linux ARM64 (64-bit Raspberry Pi OS) | `MumbleServerEdit-linux-arm64` |
| Linux ARM (32-bit Raspberry Pi OS) | `MumbleServerEdit-linux-arm` |
| macOS Apple Silicon | `MumbleServerEdit-osx-arm64` |
| macOS Intel | `MumbleServerEdit-osx-x64` |

---

## 🚀 Running

### Windows

```powershell
.\MumbleServerEdit-win-x64.exe
```

### Linux (including Raspberry Pi)

```bash
chmod +x MumbleServerEdit-linux-x64        # or -linux-arm64 / -linux-arm
./MumbleServerEdit-linux-x64
```

Pick the right ARM build for a Raspberry Pi:

```bash
uname -m
# aarch64        → use  MumbleServerEdit-linux-arm64
# armv7l / armv6l → use MumbleServerEdit-linux-arm
```

### macOS

The binaries are **not code-signed or notarized**, so Gatekeeper quarantines them on
first download. Clear the quarantine flag once, then run:

```bash
chmod +x MumbleServerEdit-osx-arm64        # or -osx-x64 on Intel
xattr -d com.apple.quarantine MumbleServerEdit-osx-arm64
./MumbleServerEdit-osx-arm64
```

(Alternatively: right-click the file in Finder → **Open** → confirm once.)

---

## 🧭 Using the tool

On launch, MumbleServerEdit asks for:

- **Mumble server host** (IP or DNS)
- **SSH port** (default `22`)
- **SSH username** and **password**
- **Ice write secret** — press Enter if the server doesn't require one

It then opens the SSH tunnel, connects to the Ice `Meta` interface, and runs a quick
**write self-test**. From the main menu you can:

```
  1) List virtual servers
  2) Add a new server
  3) Edit a server
  4) Delete a server
  5) Start / stop a server
  6) Manage channels
  7) Test connectivity (TCP + UDP)
  8) Restart the Mumble server
  Q) Quit
```

When editing, the current value is shown in `[brackets]` — press **Enter** to keep it,
or type a new value to change it.

---

## 🔧 Server prerequisites

Your Mumble/Murmur server must have **Ice enabled**. Minimum config in
`/etc/mumble-server.ini` (1.5) or `murmur.ini` (1.4):

```ini
ice="tcp -h 127.0.0.1 -p 6502"

# A write secret is required to change settings. Reads may use icesecretread.
icesecretwrite=your-secret-here
```

> **Security:** because MumbleServerEdit tunnels over SSH, bind Ice to `127.0.0.1`
> only. Never expose port 6502 to the public internet.

The account you SSH in as needs passwordless `sudo` for the optional UFW and
service-restart features (or you can run those commands manually on the host).

---

## 🛠️ Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/w1ve/MumbleServerEdit.git
cd MumbleServerEdit
dotnet build
dotnet run
```

Produce a self-contained single-file binary for any target:

```bash
# Examples — swap the RID as needed:
dotnet publish -c Release -r win-x64     --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r linux-x64   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r linux-arm   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r osx-arm64   --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
dotnet publish -c Release -r osx-x64     --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

The output binary lands in `bin/Release/net9.0/<rid>/publish/`.

---

## 📜 License

MumbleServerEdit is licensed under the **GNU General Public License, version 2** —
see [`LICENSE`](LICENSE).

It links the **ZeroC Ice** runtime, which is itself GPLv2 and whose license states
that linking Ice (even through an abstraction layer) makes a combined work covered by
the GPL. As a result, MumbleServerEdit as a whole is distributed under the GPLv2.
Third-party components and their licenses are documented in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

This program is free software and comes with **absolutely no warranty**.

---

## 🙋 Author

Copyright © 2026 **Downeast Skunkworks LLC** — gerry@downeastskunkworks.com

Built for Amateur Radio Mumble server operators, but useful for anyone running Murmur.
