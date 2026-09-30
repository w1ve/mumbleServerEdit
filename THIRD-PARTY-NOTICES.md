# Third-Party Notices

MumbleServerEdit is distributed under the **GNU General Public License, version 2**
(see [`LICENSE`](LICENSE)). It incorporates the following third-party components.

---

## ZeroC Ice for .NET (`zeroc.ice.net`) — 3.7.11

- Copyright © ZeroC, Inc.
- License: **GNU General Public License, version 2 (GPLv2)**
- Project: https://zeroc.com/ice

Ice is the RPC runtime used to speak the Mumble/Murmur management protocol.
Its license states that linking Ice — statically, dynamically, or *through an
abstraction layer* — creates a combined work covered by the GPLv2. **This is the
reason MumbleServerEdit as a whole is licensed under the GPLv2.**

ZeroC grants a special exception permitting the Mumble project to license its own
Ice-based contributions under the BSD license. That exception applies to the Mumble
project itself and does **not** extend to MumbleServerEdit, which is an independent
work and is therefore distributed under the GPLv2.

The full Ice license text is reproduced in [`ICE_LICENSE`](ICE_LICENSE).

## SSH.NET (`SSH.NET`) — 2026.0.0

- Copyright © Renci and SSH.NET contributors
- License: **MIT License**
- Project: https://github.com/sshnet/SSH.NET

Used to open the SSH connection and local port-forward tunnel to the remote host,
and to run host-side helper commands (UFW, connectivity checks, service restart).

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in the
Software without restriction, including without limitation the rights to use, copy,
modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,
and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies
or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## .NET Runtime

- Copyright © .NET Foundation and contributors
- License: **MIT License**
- Project: https://github.com/dotnet/runtime

Bundled into the self-contained single-file builds.

---

## Generated code

`Generated/MumbleServer.cs` is produced by ZeroC's `slice2cs` compiler from the
Mumble `MumbleServer.ice` slice definition. It is machine-generated ZeroC Ice code
and carries ZeroC's copyright; it is covered by the same GPLv2 terms as Ice above.
