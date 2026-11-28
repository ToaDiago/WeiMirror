# WeiMirror audio core

This is the buildable Core subset of iPhoneMirror **v1.8.3**, commit
`1270c443a29457751cadd20f6d7bbe1ce213b9db`:
https://github.com/RayrenSX/iPhoneMirror/tree/v1.8.3

The Core remains GPL-3.0-only. Original copyright notices, `LICENSE` and
`THIRD_PARTY_NOTICES.md` are retained. The libusb and libusb-win32 headers and
import libraries are copied from that same snapshot; their licenses are in
`third_party/libusb/COPYING` and `third_party/libusb-win32/COPYING_LGPL.txt`.
`third_party/libusb/bin/x64/libusb-1.0.dll` is used for the native build's
post-build dependency copy. The application continues to obtain the other
runtime dependencies from the original verified v1.8.3 component package.

WeiMirror modifications (2026-11-28):

- Build only Core; the existing USB helper and wireless hosts remain supplied
  by the upstream component pack. ABI version 18 is unchanged.
- Use sample-rate-scaled playback targets: USB 20 ms startup / 60 ms high
  water; AirPlay 40 ms startup / 100 ms high water. Startup and high water
  expand when required by the actual packet or endpoint buffer size.
- Start after the larger of the packet, endpoint and base target, rather
  than requiring a packet plus a whole endpoint before the first playback.
- When the queue exceeds high water, discard oldest queued PCM back to the
  startup target, keeping all incoming PCM that fits in the ring.
- Submit partial PCM to WASAPI immediately. Do not withhold a short packet
  until it fills the endpoint, or append a full endpoint of synthetic silence.
- Throttle underrun logs and identify the audio policy in the native log.
- Mark the modified DLL as file version 1.8.3.1, with a WeiMirror description.
- Update the original queue assertions and add device-free timing/queue tests.

`UPSTREAM-WASAPI.md` is the original upstream documentation, retained as
historical context. Its buffer descriptions and original hardware measurements
do not describe this modified build.

Build from PowerShell 7 using Visual Studio 2022/2026 C++ Build Tools, CMake,
Ninja and a Windows 10/11 SDK:

```powershell
./native/build.ps1 -RunTests
# For a portable/unregistered toolchain:
./native/build.ps1 -VisualStudioRoot 'D:\BuildTools' -RunTests
```

The 27 device-free checks exercise actual production queue helpers, steady
packet cadence, burst recovery, large packets, short final packets and bounds
across 8–192 kHz. An optional `WeiMirror.AudioQA.exe --endpoint-smoke` uses
digital silence to exercise a real Windows default audio endpoint. It requires
an available audio output device, and does not measure iPhone-to-ear latency.

The bundled DLL is written only into the application's per-build worker cache.
The imported/installed upstream pack is not modified. The worker cache identity
includes the patched DLL's SHA-256 and is checked before loading it.
