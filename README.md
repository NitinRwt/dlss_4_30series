# DLSSG for SM86 (proxy build) - 0.4.2

[中文](README.md) · **English**

Enables NVIDIA DLSS Frame Generation (DLSS-G) on RTX 30-series (SM86) and RTX 20-series (SM75) GPUs, and provides INT8 acceleration for DLSS Super Resolution on RTX 30 / 20. Windows x64 / D3D12. The runtime files are `version.dll`, `dlssg_sm86.ini` and the `dlssg_sm86\` directory.

> **Upgrading from 0.3.x? Read this first: 0.4.0 is a major upgrade. The file layout and the install procedure both changed, so you cannot copy it over the old version.**
>
> 1. Remove the old version completely: quit the game, then delete every proxy DLL of this project from the game directory (0.3.1–0.3.6 had you copy four of them: `version.dll`, `winmm.dll`, `dbghelp.dll` and `dinput8.dll`; delete all four), plus `dlssg_sm86.ini` and the whole `dlssg_sm86\` directory.
> 2. Then copy the three items of the new version: `version.dll`, `dlssg_sm86.ini` and the whole `dlssg_sm86\` directory. The runtime, backend, kernel pack, XeSS model pack and super-resolution files all live inside `dlssg_sm86\`; copying only the DLL does nothing.
> 3. Keep only one proxy at any time: the package root contains only `version.dll`. If the game does not load it, swap in one from `alternatives\` (swap, do not add).
> 4. Use the new `dlssg_sm86.ini`: keys moved (`Optimized` and `Preset` are now in `[DLSSG]`, and `[XeSSFG]` is new), and `[Runtime] Mode=Auto` was removed. `Optimized` / `Preset` at their old locations are no longer read: if they are set only there and are not the default (for example `[FrameGeneration] Optimized=1` in an old INI), frame generation is not enabled and one `configuration_invalid` is logged. Re-apply any settings you changed in the new INI.
>
> Details: "Install & upgrade" below, and the manual `MANUAL.en.html`.

## Contents

- [Project directory overview](#project-directory-overview)
- [Changes in this release](#changes-in-this-release)
- [Statement and plans](#statement-and-plans)
- [Requirements](#requirements)
- [Extra VRAM for frame generation](#extra-vram-for-frame-generation)
- [Install & upgrade](#install--upgrade)
- [Antivirus false positives & signing](#antivirus-false-positives--signing)
- [Performance](#performance)
- [Estimating the frame rate with frame generation](#estimating-the-frame-rate-with-frame-generation)
- [About 6X](#about-6x)
- [Field feedback](#field-feedback)
- [Known issues](#known-issues)
- [Diagnostics & limits](#diagnostics--limits)
- [Credits](#credits)
- [Source & build](#source--build)
- [License & third parties](#license--third-parties)

## Project directory overview

This is what the folder you are looking at contains, and how the pieces fit together.

### How it works, in one paragraph

The game loads the proxy DLL (`version.dll`) because it imports a system DLL of that name. The proxy forwards every export to the real system DLL, but intercepts the game's request to load `nvngx_dlssg.dll` (NVIDIA's DLSS-G runtime). It hands the game the unmodified NVIDIA runtime that ships in this package instead. The backend (`sm86_backend.dll`) then replaces the GPU kernels inside that runtime, which NVIDIA compiled only for Ada / Blackwell, with SM86 / SM75 versions from the kernel pack (`fg_kernels.dll`). Optionally it also swaps DLSS Super Resolution for an INT8 kernel set, or runs Intel's XeSS-FG model instead of DLSS-G. The game keeps supplying color, depth and motion vectors as usual.

```text
Game ──loads──> version.dll (proxy: loader only, forwards system exports)
                    │  finds dlssg_sm86\ relative to its own folder
                    ▼
        dlssg_sm86\runtime\nvngx_dlssg.dll   NVIDIA DLSS-G runtime (unmodified)
        dlssg_sm86\backend\sm86_backend.dll  hooks, kernel routing, SR path, capture
        dlssg_sm86\kernels\fg_kernels.dll    SM86 / SM75 frame-generation kernels
        dlssg_sm86\xefg\xefg_kernels.dll     XeSS-FG model pack (Engine=XeSS)
        dlssg_sm86\sr\*                      Super Resolution snippet + INT8 kernels
```

### Top level of this folder

| Path | What it is |
|---|---|
| `version.dll` | The proxy (about 0.8 MB, signed). The one and only hook DLL you normally copy next to the game's render EXE. |
| `dlssg_sm86.ini` | User configuration. Copy it next to the proxy. Only the keys a player might change are in it; the full key list is in `docs/INSTALL.en.md`. |
| `dlssg_sm86\` | The payload directory the proxy loads everything from (see below). Must be copied together with the DLL. |
| `alternatives\` | Five spare proxies (`winmm.dll`, `dbghelp.dll`, `dinput8.dll`, `dxgi.dll`, `d3d12.dll`) plus its own `README.md` with the order to try them. Not copied by default. |
| `dlssg_sm86ini_Configurator.html` | Single-file, dependency-free INI editor: edit common keys per section, live INI preview, load an existing INI (picker or drag and drop), download / copy, Chinese / English toggle, built-in validation. Open it in any browser. |
| `MANUAL.html` / `MANUAL.en.html` | Step-by-step install manual (Chinese / English). Double-click to open in a browser. |
| `README.md` / `README.en.md` | This overview in Chinese / English. |
| `THIRD_PARTY_NOTICES.txt` | Attributions and license statements for NVIDIA, Intel, Coldwood1026, MFG-Unlocker, etc. |
| `docs\` | Technical documentation (see below). |
| `archive\` | Old releases (0.1.0, 0.2.4, 0.3.5) kept for reference. Not needed for use; about 400 MB. |

### `dlssg_sm86\` (the payload)

| Path | What it is |
|---|---|
| `backend\sm86_backend.dll` | The backend (about 2.1 MB): NGX hooks and dispatch, kernel routing and fusion, super-resolution path, capture and diagnostics. Contains no kernel images. |
| `kernels\fg_kernels.dll` | The frame-generation kernel pack (about 18.3 MB, **required**): all kernel images for the SM86 and SM75 families, plus the manifest. Without it, `Engine=DLSS` frame generation is refused (`kernel_pack_missing`, `fg_kernels_unavailable`). |
| `runtime\nvngx_dlssg.dll` | NVIDIA's DLSS-G runtime 310.9.16.0 (about 7.5 MB), unmodified. Supports up to 6X. The version the game ships is irrelevant. |
| `xefg\xefg_kernels.dll` | XeSS-FG model pack (about 16.9 MB): programs, weights and CUDA images for Intel model 18 and model 15. Used only when `Engine=XeSS`. |
| `sr\nvngx_dlss.dll`, `sr\sr_kernels.dll` | Super-resolution snippet 310.9.16.0 (74.2 MB, unmodified) and the INT8 kernel pack for RTX 30 / 20 (106.6 MB). A matched pair, used only when `[SuperResolution] Mode` is not `Off`. *(The full release package contains this folder; this checkout of the repository does not.)* |
| `logs\dlssg_sm86.log` | Created at run time. One shared text log for every module and process. May not exist until something is logged. |

### `docs\` (technical documentation)

| File | Contents |
|---|---|
| `INSTALL.md` / `INSTALL.en.md` | Full install guide and **every INI key**, log fields, driver requirement (R580), preset and optimization-level explanations. |
| `PROJECT.md` | What is in the release package and how the source repository is organised and built. |
| `ARCHITECTURE.md` | The execution path from game request to presented frame (proxy → runtime verification → backend → kernel routing). |
| `CAPTURE.md` | Game Capture & Replay (GCR): record every DLSS-G Evaluate's inputs, parameters and outputs in a real game, then replay offline and compare against the stock runtime bit for bit. |
| `CAPTURE_SR.md` | `[Debug] CaptureSR`: recording DLSS Super Resolution inputs for offline calibration. |
| `SIGNING.md` | Optional Authenticode signing of the project's own DLLs during packaging. |

### `archive\`

Previous releases, each with its own README, INI, proxy and (for 0.2.4 / 0.3.5) docs: `0.1.0`, `0.2.4` (native-host mode, with `config\` and `altnative\`), `0.3.5` (proxy mode, with `alternatives\` and a `310.1\` runtime-model variant). The layouts differ from the current one; do not mix files between versions.

### Source repository (separate)

The C++ source (proxy, backend, tests, tools) lives in a separate source repository and builds with `build.ps1`; see [Source & build](#source--build). This folder is the **release package**, not the source tree.

## Changes in this release

### 0.4.2

- **XeSS-FG performance fixed on RTX 50 (Blackwell)**: 0.4.1 shipped compute_75 PTX to RTX 40 / 50. On sm_120, ptxas compiles every `m8n8k16` into one full `m16n8k16` and discards half of the result (144 vs 72 instructions in the backbone convolution, registers 79 → 124), so the tensor-core kernels ran about twice as slow on Blackwell as on Ampere. 0.4.2 builds separate `.bw` images for 26 kernels (compute_86 PTX, with the 3×3 taps paired into `m16n8k32`) and narrows the dispatches where DXIL is faster on RTX 50 to D25 and model 15's D02 / D30. Cyberpunk 2077 at 3440×1440 (RTX 5070): 4X per real frame 6.54 → 4.68 ms, real frame rate 46.5 → 52.0 fps; 2X 2.43 → 1.69 ms (57.8 → 59.8 fps); DLSS 4X on the same card is 3.98 ms.
- **XeSS-FG supports jittered motion vectors (`DLSSG.MvecJittered=1`)**: 3X / 4X no longer end in `xefg_kernel_fatal{unsupported_input}`. D31 now runs four `jt_` kernels that carry model 18's jitter term `D = (jp − jc) · (1 − t)`; without jitter the output is bit-identical to the normal path. Cost is about +0.08 ms per generated frame (1440p RGBA8) and +0.17 ms at 4K.
- **4K optimizations**: model 18's D24 / model 15's D29 MV splat is now this project's own CUDA kernel, and the scatter buffers are cleared only over the prefix that this or the previous scatter may have written. Another 3.0–5.6 % faster per real frame at 4K, output bit-identical.
- **New `dlssg_sm86ini_Configurator.html`**: a single-file, dependency-free INI configurator. It edits common keys per section, shows a live preview of the generated INI, loads an existing INI (picker or drag and drop), downloads / copies the result, switches between Chinese and English, and validates (`MaxGeneratedFrames` 1–5, `Engine=XeSS` only 1–3, a hint for the old `Model=` key, and so on). Shipped with the release package.
- **`[XeSSFG] UseHudless=Auto` now behaves the same as `0`**: the back buffer is always the network input and the whole image, UI included, is interpolated, whether or not the game supplies a HUD-less image (`xefg_hudless_unused` is no longer produced). `1` stays the strict mode (no usable HUD-less image → `xefg_kernel_fatal{hudless}`). The key's accepted values and its factory value (`Auto`) are unchanged.
- Version 0.4.2; `README` / `MANUAL` / `INSTALL` / `COMPAT` updated in step (`COMPAT` gains C139).

### 0.4.1

- XeSS-FG engine (optional):
  - Multi-frame generation (MFG): `[FrameGeneration] Engine=XeSS` now reaches 2X / 3X / 4X (`MaxGeneratedFrames` 1–3). The model is Intel's model 15 as ported by MFG-Unlocker 1.6.0: 2X still runs model 18's graph (4–5 % more than before), while 3X / 4X use model 15's own multi-frame path, where the i-th generated frame of each real frame uses phase t = i / (N + 1). The `[XeSSFG] Model` key was removed (there is only one model); an old INI that sets it only logs a warning.
  - Tensor-core CUDA backend: the ported backbone layers (3×3 INT8 convolutions) are hand-written INT8 IMMA kernels (`mma.sync.m16n8k16`) with `cp.async` double-buffered weight prefetch. The whole graph is run by this project's own D3D12 + NVAPI CUDA runner on the game's command list; Intel's `libxess_fg.dll` is not loaded.
  - `[XeSSFG] FlowResolution=CoarseAt4K`: automatically `Coarse` when the output has ≥ 3840 × 2160 pixels, still `Middle` at 1440p.
  - `[XeSSFG] UseHudless` became tri-state: `Auto` (factory, decided per frame) / `1` (strict: reject if the HUD-less image is missing) / `0` (do not use).
  - On RTX 30: 8–15 % faster than DLSS-G at 1440p and below, 27–30 % slower from 4K up; see "Performance".
- `[SuperResolution] Mode` is now an explicit preset: `Off` (factory) / `M-INT8` / `L-INT8` / `M-FP16` / `L-FP16`. 0.4.0's `Mode=On` was removed; an invalid value is `configuration_invalid` and super resolution is not enabled.
- `[DLSSG] Preset` is only A / B / C now: `Auto` was removed (runs as `A`), factory `A`.
- A batch of keys was removed and is no longer read: the old-location `[FrameGeneration] Optimized` / `[Compatibility] OptimizedKernels` / `[Compatibility] Preset`, `[Compatibility] AccelerateNewerGpus` / `ForceGeneratedFrames` / `SM75Family` / `HardwareBilinear` / `LaunchChains` / `PlainVariant`, `[Backends]` and so on. Writing them only logs a warning, and behaviour follows the factory value. RTX 40 / 50 always run this project's kernels.
- The driver requirement is now a hard R580 floor: when a CUDA 13 cubin is rejected by the driver, it no longer falls back to PTX; frame generation is refused with an error (`driver_below_floor` / `kernel_image_unavailable`).
- Super-resolution INT8 first-frame stutter fixed: uploading the 12 weight mirrors moved out of the first observed frame (previously that single Evaluate cost 5–6 ms more CPU time than the same-preset FP16).
- Explicit switches that cannot be honoured are refused instead of silently falling back: `[Runtime] Path` / `SRPath` pointing at the wrong file, missing bundled files, `KernelImage=Cubin` on RTX 40 / 50, and so on all refuse and state the reason.
- Logging: only one text log, `dlssg_sm86\logs\dlssg_sm86.log` (0.4.0's per-process `frames_<pid>.jsonl` is gone); `[Logging] FrameTiming=1` additionally writes `frame_timing.txt`.

### 0.4.0

- Super-resolution INT8 (RTX 30 / 20 only): with `[SuperResolution] Mode=On`, the quality tiers the game runs with presets L / M (for most games "Performance" and "Ultra Performance") are replaced by this project's INT8 kernels; the preset itself does not change. On an RTX 3070, the GPU time of one super-resolution pass versus the same-preset FP16 is −6..−16 % for L and −9..−22 % for M. Factory `Mode=Off`. (`Mode=On` was removed on 2026-10-19; it is now `M-INT8` / `L-INT8` / `M-FP16` / `L-FP16`.)
- DLSS-G preset C (FG Lite, optional): `[DLSSG] Preset=C`. Each generated frame skips the second network and post-processing. On real captures on an RTX 3070 the GPU time per generated frame is −29..−34 % at 2X and about −45 % at 4X, with slightly lower quality (mainly in HUD and occlusion edges). The factory value was `Preset=Auto` (`Auto` was removed on 2026-10-22; the factory value is now `A`).
- XeSS-FG engine (optional): `[FrameGeneration] Engine=XeSS` generates the interpolated frames with Intel's XeSS-FG model: Intel model 15 (a port from MFG-Unlocker 1.6.0) up to 3X / 4X, and model 18's graph at 2X. Since 2026-10-26 there is only this one model; the `[XeSSFG] Model` key was removed (no longer read). `[XeSSFG] FlowResolution` (`Coarse` / `Middle` / `Fine`) and `UseHudless` remain. Intel's `libxess_fg.dll` is neither shipped nor loaded; the model runs on this project's own CUDA kernels. On RTX 30 it is slower than DLSS-G, see "Known issues". Factory is still `Engine=DLSS`.
- The bundled runtime became `nvngx_dlssg.dll` 310.9.16 (bit-identical to 310.9.1 with the default preset). The version the game ships makes no difference.
- New INI layout: DLSS-G-only `Optimized` and `Preset` moved to `[DLSSG]`, XeSS-only keys are in `[XeSSFG]`. The old locations still worked at first and logged a `configuration_warning` (since 2026-10-22 they are no longer read; see item 4 above). `[Runtime] Mode=Auto` was removed: writing it runs as `Bundled` and logs a warning.
- The release root now holds a single proxy `version.dll`; the other five are in `alternatives\` and you use one at a time (0.3.1–0.3.6 had you copy all four, so extras must be deleted when upgrading; see "Install & upgrade").
- RTX 40 / 50: frame generation also switched to this project's kernels (PTX, compiled by the driver). `[Compatibility] AccelerateNewerGpus=0` switched back to NVIDIA's official kernels (that key was removed on 2026-10-19 and is no longer read: RTX 40 / 50 always run this project's kernels). Not tested on real hardware.
- `[Logging] FrameTiming=1`: writes one line per real frame in the log directory (frame interval, GPU time of super resolution and frame generation), for performance feedback.
- New install manual `MANUAL.html` (English: `MANUAL.en.html`).

### 0.3.6

- New optional `[FrameGeneration] CatmullRomWarp=1` (default 0): the two motion-compensation taps in a generated frame switch from bilinear to Catmull-Rom, so fine structure (railings, text, distant window panes) is a little clearer in generated frames. The cost is about +3 % per generated frame (1080p / 1440p) to +4 % (4K), and the picture is no longer bit-identical to the official one. Measured offline on 11 real captures from 6 games, fine-structure energy rose about 5 percentage points and the flicker metric improved with it; the visible improvement is slight. It is not part of any `Optimized` tier and the 310.1 build does not support it. There is also an experimental key `DetailRestore` (items C / D) whose measured benefit was small or had side effects; enabling it is not recommended, see `docs/INSTALL.md`.
- The log now records the DLSS-G runtime version, hash and generation the game ships (`runtime_redirect` / `game_runtime_generation`). Games using an early Streamline (1.x–2.4, shipping DLSS-G 3.x) are limited to 2X: that plug-in generation never writes the generated-frame count to the runtime, which no setting of this project can change; the log now says so directly. Streamline 1.x versions were all logged as 1.0.0.0 before; fixed.
- With the NVIDIA App's "DLSS override" on, the frame-generation component is supplied by the NGX model repository and this project takes it over as usual; the log adds `ngx_override_active` to mark this case. The Streamline online plug-in from the model repository is now also recognised (previously `ForcePluginFrames` was silently ineffective in such processes).
- The release root now holds all four proxies (`version` / `winmm` / `dbghelp` / `dinput8`) and the INI, matching `alternatives/README.md`; `310.1\` also carries the INI.
- Removed a never-called NVAPI stub (`SetRawScgPriority`, which exists in none of the runtimes, the NGX core or Streamline).
- `docs/INSTALL.md` additions: what `Preset` means and when it takes effect, why `Optimized=2/3` "shows no difference", who owns image artefacts, and the order of elimination for whole-screen flicker.

### 0.3.5

- Fixed a defect present since 0.3.0: when the game rebuilds the frame-generation feature (menu switches, resolution / settings changes, toggling frame generation; some games rebuild frequently while running), the driver recycles kernel handles, and a recycled handle could still be bound to the previous kernel's optimized variant, so the wrong kernel could be used afterwards. It showed up as corrupted generated frames, or random crashes / GPU driver resets after playing for a while (`Optimized=0` is unaffected). In an offline test of 15 rebuilds on a 3070, the old version produced bad frames in 3 of 4 runs; the new version was bit-identical every time.
- The log gains `fg_gate_exit` (written on normal process exit); if it is missing from the log, the game was killed or crashed.

### 0.3.4

- Fixed 0.3.3's crash on RTX 30 (GPU driver reset on entering the game or enabling DLSS; FHC01 in *Forza Horizon 6*). Cause: when the NVIDIA App's DLSS override or an NGX online update was active, the DLSS super-resolution model was loaded from the NGX model directory, and it also received this project's GPU-architecture rewrite meant for Streamline. On RTX 30 it then took a kernel path that does not belong to the card and hung the GPU. NVIDIA's own components (the NGX model directory, driver modules) now always get the real architecture; the rewrite applies only to Streamline and the game.
- Logging: `arch_spoof_applied` is written once per calling module with its full path, and `arch_spoof_excluded` is new. Three small fixes: a missing system export no longer makes the proxy fail to load (#10 / #65), `backend_install -100` gives a concrete reason, and HAGS being off is reported separately (#513).

### 0.3.3 (preview)

- `[FrameGeneration] Optimized=1` no longer turns on `SkipRepeatedRealCopy`, and no tier turns it on any more; write `SkipRepeatedRealCopy=1` manually if you need it. It is bit-identical in synthetic scenes and offline replay, but that rests on assumptions outside the kernels (how the game-side plug-in submits repeated Evaluates of the same group), it has never been verified in a real game, and one flicker report appeared on 0.3.2 (*Zenless Zone Zero*, public-repository issue #532). Tier `1` remains bit-identical with the official runtime.
- RTX 20 series (Turing): the NVAPI GPU-architecture rewrite presented to the game is now installed when the game starts (this mod's `DllMain`, or when `nvapi64.dll` is loaded). Streamline 2.x decides in `slInit` that "this GPU does not support DLSS-G"; at that point the DLSS-G runtime had not been loaded and earlier versions had not yet installed the rewrite, so a 2080 Ti with *Final Fantasy VII Rebirth* (Streamline 2.8) lost its frame-generation plug-in. RTX 30 needs the same: an RTX 3060 Laptop showed exactly the same behaviour in the same game (public-repository issue #509; 0.2.4's native build reported Ada for every card, which is why that version worked). So the real answer is to rewrite Turing and Ampere to Blackwell (RTX 50; both versions do multi-frame generation, and the game's and Streamline's 3X / 4X / 6X options are unlocked for this architecture). Ada / Blackwell are left untouched.
- Diagnostics: the log field `arch_spoof_installed.trigger` gains the values `dllmain` / `nvapi_load` / `dependency_load`.

### 0.3.2

- Rewrote some inference kernels (310.9 build): generated frames now match the official DLSS-G exactly (bit-identical on an RTX 3080 Ti and an RTX 5070; the RTX 2080 Ti output is bit-identical to the 3080 Ti), no longer lossy, with a small speed-up (0–8 % on the 3080 Ti).
- Optimized-kernel tiers reworked: `[FrameGeneration] Optimized` is now four levels `0`–`3`. `0` stock kernels, no acceleration; `1` all accelerations, bit-identical with the official picture (factory default); `2` additionally enables lossy image kernels, faster, about 50 dB PSNR or better against the official picture (310.9 build only); `3` all lossy accelerations, fastest. See [`docs/INSTALL.en.md`](docs/INSTALL.en.md).

### 0.3.1

- Simpler install: the release root now carries four proxies directly (`version.dll`, `winmm.dll`, `dbghelp.dll`, `dinput8.dll`); **copy all of them next to the render EXE, no choosing**. The first one the game loads in the process becomes this mod; the others stand by automatically, only forward their exports to the same-named system DLL, and do not install frame generation twice. Only when none of the four is loaded, take `dxgi.dll` or `d3d12.dll` (pick one) from `alternatives\` by hand.
- Factory `MaxGeneratedFrames` changed from `5` to `3` (4×). Games with their own Dynamic MFG run up to the limit written here by default, and `5` is too high for most people (public-repository issues #497 / #499). For 6× use the 310.9 build and change this line to `5` by hand; with a game that ships the older 4X plug-in you get only 4× whatever you write.
- A wrong key in the INI no longer turns off the whole mod: the key falls back to its own default and one `configuration_warning` is logged (visible only at `Level=2` since 2026-10-23, C106; `Level=1` before), and the other keys work as usual (since 2026-10-19 this changed: a wrongly written value is `configuration_invalid`, and the feature the key belongs to is not enabled, see the factory-INI paragraph below). When the backend does not support a switch (for example `Preset=B` on the 310.1 build), it also **drops that switch and continues installing**, logging `kernel_selection_unsupported{stripped_flags}`, instead of refusing outright.
- RTX 20 series (Turing) works again with the factory `dlssg_sm86.ini`; no key needs changing: the kernel family is chosen automatically by the physical card (the SM75 family). Frame generation at 2X was verified on an RTX 2080 Ti with *The Witcher 3* next-gen (the game's own Streamline plug-in tops out at 2X); 4X / 6X are decided by the game's plug-in, same as on 30-series. R580 or newer driver required (a hard requirement since 2026-10-26; see "Driver version requirement: R580" in `docs/INSTALL.md`).
- 0.3.0 could not enable frame generation on any RTX 20: the GPU architecture it reported to the NGX core was not the card's real one, so the core refused the frame-generation feature (public-repository issues #491 / #492). This change does not affect RTX 30 numbers or performance.
- The two RTX 20-related keys `SM75Family` and `SpoofArchToGame` are not in the factory INI; their defaults are correct. See [`docs/INSTALL.en.md`](docs/INSTALL.en.md) if needed.
- Diagnostics when frame generation will not open: `[Logging] Level=2` now records which gate refused it: hardware-accelerated GPU scheduling (HAGS), the driver version, the NGX core's answer to the capability query, and the return value of `CreateFeature`. All these records are named `fg_gate_…`.
- RTX 20 quality and performance have not been measured: this version only proves frame generation can run on 20-series (thousands of Evaluates, zero failures, zero fallbacks). There is no FPS data and no quality comparison with RTX 30.

### 0.3.0

- Went back from native mode to proxy mode. Nativizing (a self-built NGX host) had compatibility problems in some games that were hard to fix; this version embeds the unmodified stock runtime via a proxy DLL, so the game's NGX calls are unchanged and compatibility is better.
- The 310.9 runtime adds 6X (`MaxGeneratedFrames` limit raised from 3 to 5). In games that support Dynamic MFG themselves, choosing "Dynamic / Auto" frame generation runs at 6X.
- The best kernel set (about 19–32 % faster in the offline benchmark), the two-tier factory INI, capture and replay, and diagnostic logging are described below and in `docs/`.

## Statement and plans

- The kernel-level work on frame interpolation (DLSS-G) has essentially reached the best state achievable at this stage; no further kernel optimisation is planned. Later versions will focus on compatibility and bug fixes.
- INT8 for super-resolution presets L / M is provided in this version (RTX 30 / 20).
- XeSS-FG engine: model 15, 2X–4X, no performance tuning done; for performance and the HDR (RGBA16F) back buffer see "Known issues".
- Vulkan support is hard to continue in the project's current state: the wrapper layer's game compatibility is not good enough and test coverage is thin, and Vulkan would only bring more compatibility problems. Users who need it can use the community patch builds.

## Requirements

- OS and game: Windows 10/11 x64, D3D12.
- GPU: RTX 30 series (SM86) or RTX 20 series (Turing / SM75). Development validation and all in-game tests were done on an RTX 3070; the complete 0.4.0 test package was also run on an RTX 3080 Ti. On RTX 20, frame generation was tested on a 2080 Ti (0.3.x) along with one INT8 super-resolution smoke test. RTX 40 / 50 also work, with frame generation going through this project's kernels as PTX, not tested on real hardware (except the XeSS-FG results on RTX 5070 noted in 0.4.2).
- Driver: an NVIDIA driver of **R580 or newer** (CUDA 13.0); 591.86, 610.74, 610.88 and 617.14 were verified. On older drivers frame generation is not enabled and the log has one `driver_below_floor`.
- No CUDA Toolkit and no Python needed.

| Release package | Bundled runtime | Frame-generation limit | `version.dll` size | SHA-256 |
|---|---|---|---|---|
| `dlssg-release-0.4.0-x64.zip` | 310.9.16.0 | 6X | 853,280 | `7b3f90435be4f94348bf8d128c84871c7f904c704f7690601a263277c327a7b2` |

The proxy DLL is about 0.85 MB; the runtime, backend and the resource packs are all files under `dlssg_sm86\`: the backend `backend\sm86_backend.dll` 2.1 MB (contains no kernels), the frame-generation kernel pack `kernels\fg_kernels.dll` 18.3 MB (required), the runtime `runtime\nvngx_dlssg.dll` 7.5 MB, the XeSS model pack `xefg\xefg_kernels.dll` 16.9 MB (programs, weights and CUDA images of model 18 and model 15), and super resolution `sr\nvngx_dlss.dll` 74.2 MB and `sr\sr_kernels.dll` 106.6 MB (the two are a pair, used only when `[SuperResolution] Mode` is not `Off`).

## Extra VRAM for frame generation

The local VRAM increase of frame generation itself (after warm-up, minus the value before creating the feature, rounded up to 10 MiB; 310.9 build, best kernels, measured in the 0.3.x era). The increase depends only on the output resolution and not on the multiplier: 2X and 6X use the same amount, and a higher multiplier takes no extra VRAM.

| Aspect | Output resolution | Extra VRAM for frame generation |
|---|---|---|
| 16:9 | 720p | about 230 MiB |
| 16:9 | 1080p | about 350 MiB |
| 16:9 | 1440p | about 540 MiB |
| 16:9 | 4K / 3840×2160 | about 810 MiB |
| 21:9 | 2560×1080 | about 440 MiB |
| 21:9 | 3440×1440 | about 700 MiB |
| 21:9 | 5120×2160 | about 1050 MiB |
| 32:9 | 3840×1080 | about 610 MiB |
| 32:9 | 5120×1440 | about 990 MiB |
| 4:3 | 1920×1440 | about 430 MiB |

Resolutions with the same height but different widths use similar amounts (by output pixel count). The figure is the increase of frame generation itself, not counting the game or the composition input textures. With `Engine=XeSS`, whole-machine dedicated VRAM is about 410–530 MiB more than with DLSS-G (RTX 3070, 4K output, measured on five games).

## Install & upgrade

If this is your first time installing a mod, follow the install manual `MANUAL.en.html` in the release package root (Chinese: `MANUAL.html`; double-click to open it in a browser) step by step. In short:

1. Quit the game completely.
2. Go to the directory of the EXE the game actually renders with (for example `...\b1\Binaries\Win64\` for *Black Myth: Wukong*). If unsure, with the game running open Task Manager → "Details", right-click the game process → "Open file location".
3. Copy `version.dll`, `dlssg_sm86.ini` and the folder `dlssg_sm86\` from the release package root into it; if the directory already has a DLL of the same name, back up the original first. The runtime, backend, kernel pack, XeSS model pack and the super-resolution snippet are all inside `dlssg_sm86\`, and the proxy itself is under 1 MB, so **copying only the DLL and not the folder does nothing**. You normally do not copy `alternatives\`.
4. Start the game and enable DLSS frame generation in the graphics settings; pick 2X / 3X / 4X (for 6× see "About 6X" below, where `MaxGeneratedFrames` must be changed to `5` by hand). On RTX 20 / 30, if the frame-generation option that used to be greyed out can now be turned on, it is installed.
5. To confirm the proxy is really loaded (needed because on RTX 40 / 50 frame generation can be enabled anyway): set `[Logging] Level` to `2`, play a while with frame generation on and quit; if `dlssg_sm86\logs\dlssg_sm86.log` has lines with this game's EXE name, it was loaded (the file also keeps lines from earlier runs; look at this one). The factory `Level=1` logs only on errors, so an empty `logs` folder or no such folder at all is normal.
6. If the game does not load `version.dll` (with frame generation on at `Level=2` there are also no lines with the EXE name in `dlssg_sm86.log`): quit the game, delete it, and from `alternatives\` put one next to the EXE at a time in the order `winmm.dll` → `dbghelp.dll` → `dinput8.dll` → `dxgi.dll` → `d3d12.dll` and try again. Only one at any time; `dxgi.dll` and `d3d12.dll` sit on the render path, so use them last, and never both together. See `alternatives\README.md`.
7. Upgrading: quit the game, first delete the old proxy DLL(s), `dlssg_sm86.ini` and the `dlssg_sm86\` directory, then copy these three items of the new version; redo any settings you changed in the new INI. 0.3.1–0.3.6 told you to copy four proxies (`version.dll`, `winmm.dll`, `dbghelp.dll`, `dinput8.dll`), so delete all four when upgrading: when a directory has several of this project's proxies, the first one loaded in the process runs this mod, and if the one that wins is an old version, the old version is what runs. If you are upgrading from 0.3.x you can also delete `%LOCALAPPDATA%\DlssgSm86`: the new version no longer writes anything there.
8. Uninstall: delete `version.dll` (or the replacement you used), `dlssg_sm86.ini` and the `dlssg_sm86\` directory, and put back any original DLL you backed up.

Switches in the factory `dlssg_sm86.ini` that players may want to change:

- `[DLSSG] Optimized`: consistency tier `0`–`3`. Factory `1` = all bit-identical accelerations (since 2026-10-21; 0.4.0's factory was `2`); `2` = additionally the lossy image kernels: no longer bit-identical with the official output, no visible difference from `1` in side-by-side comparison against ground truth, 1–3 % faster per generated frame; `0` = stock numerics; `3` = all lossy accelerations. Before 2026-10-14 it lived in `[FrameGeneration]`; the old location is no longer read (see item 4 above).
- `[DLSSG] Preset`: factory `A`; what you write here is what runs, even if the game or a driver profile asks for B / C (`Auto` was removed: writing it runs as `A` and logs a warning); `B` = force UI recomposition (effective only when the game hands over both a HUD-less image and a UI plane); `C` = FG Lite (faster, slightly lower quality), which also uses this project's accelerated kernels (following the `Optimized` tier) and works whichever DLSS-G version the game ships.
- `[FrameGeneration] MaxGeneratedFrames`: factory `3` means 4X; change to `5` for 6X. This is a request, not a cap: it is passed to the game unchanged (a driver profile cannot push it lower), and the game menu can choose a lower multiplier. When it cannot be done (above 5, or above 3 with `Engine=XeSS`), frame generation is not enabled and the log explains why (`max_generated_frames_unsupported`); it never silently lowers it.
- `[FrameGeneration] Engine`: factory `DLSS`; `XeSS` = Intel XeSS-FG model (model 15, with 2X using model 18's graph): `MaxGeneratedFrames` may be `1`–`3`, and the factory `3` works directly; the `[XeSSFG] Model` key was removed on 2026-10-26 and is ignored. `[XeSSFG] FlowResolution` (factory `Middle`; finer is slower) and `UseHudless` (factory `Auto` = same as `0`: the whole image, UI included, is interpolated; `1` = always required, error exit if the game does not supply it) apply to XeSS only.
- `[SuperResolution] Mode`: factory `Off`. On RTX 30 / 20, `M-INT8` / `L-INT8` = every super-resolution quality tier (DLAA included) uses preset M / L and runs this project's INT8 kernels, while the tier is still chosen in-game; `M-FP16` / `L-FP16` = the same preset on NVIDIA's own kernels (for comparison). For the Quality / Balanced / DLAA tiers, M / L is slower than the game's own K; the speed-up is in Performance / Ultra Performance. There are only these five values; anything else (including 0.4.0's `On`) leaves super resolution disabled and logs a `configuration_invalid`; the old `Tier` key is not read. It has no effect on RTX 40 / 50.

The remaining items in the factory file (`Enabled`, logging, runtime source `[Runtime] Mode=Bundled`) normally need no change; more diagnostic / compatibility items are not in the factory file and take safe defaults; for the complete list see [`docs/INSTALL.en.md`](docs/INSTALL.en.md). A wrongly written key value does not fall back to a default: the log records one `configuration_invalid` and the feature the key belongs to is not enabled (a frame-generation key → frame generation not enabled, same as `Enabled=0`; a super-resolution key → super resolution not enabled; a logging / debugging key → only that one diagnostic is turned off).

## Antivirus false positives & signing

The project's own DLLs in the release package are all code-signed: `version.dll` in the root, the five spare proxies under `alternatives\`, and the backend and the three resource packs under `dlssg_sm86\` (frame-generation kernels `fg_kernels.dll`, super-resolution kernels `sr_kernels.dll`, XeSS model `xefg_kernels.dll`), 10 files in all. NVIDIA's runtime and super-resolution snippet in `dlssg_sm86\runtime\` and `dlssg_sm86\sr\` keep NVIDIA's own signature and are untouched. A self-signed certificate only proves the signer's identity and file integrity and is not trusted by Windows by default; on first run Windows SmartScreen may still show "Unknown publisher". That is a reputation-based prompt and a different thing from an antivirus detection. This release uses the self-signed certificate `CN=DLSSG for SM86 (self-signed)`, SHA-1 thumbprint `85BA66762F851E49148D706915D09026281418E6`; you can check the signer and thumbprint under file Properties → Digital Signatures or with `signtool verify /pa`. See `docs/SIGNING.md` for how signing is done.

## Performance

All numbers below come from this project's own timing (D3D12 timestamps around each Evaluate, or the `[Logging] FrameTiming` log). Offline GPU milliseconds measure the overhead of frame generation / super resolution itself; they do not equal the in-game frame-rate gain.

### Frame generation: real game captures (RTX 3070, offline replay)

RTX 3070, driver 617.14, complete run (run_all) of the 0.4.0 test package. Inputs recorded in the games (Cyberpunk 2077, Black Myth: Wukong) were replayed through the whole route (proxy → backend → NVIDIA runtime 310.9.16 or the XeSS engine); the first 4 real frames of each run are dropped and the median over all frames of 4 rounds is taken. Units are GPU milliseconds per generated frame, with the change against the DLSS default in brackets (negative = faster). DLSS default = 0.4.0's factory INI (`Optimized=2`, `Preset=Auto`; since 2026-10-21 the factory tier is `1`, which is 1–3 % slower per generated frame, see the 3080 Ti table below); stock kernels = `Optimized=0`.

**2X**

| Capture | DLSS default | Preset C | XeSS Middle | XeSS Coarse | XeSS Fine | Stock kernels |
|---|---:|---:|---:|---:|---:|---:|
| Cyberpunk 2077 1440p | 2.554 | 1.694 (−33.7 %) | 2.974 (+16.4 %) | 2.344 (−8.2 %) | 3.828 (+49.9 %) | 3.639 (+42.4 %) |
| Black Myth: Wukong 1440p | 2.581 | 1.745 (−32.4 %) | 3.266 (+26.5 %) | 2.634 (+2.0 %) | 4.164 (+61.3 %) | 3.657 (+41.7 %) |
| Cyberpunk 2077 4K | 3.557 | 2.499 (−29.7 %) | 6.379 (+79.3 %) | 5.059 (+42.2 %) | 8.296 (+133.2 %) | 4.407 (+23.9 %) |
| Black Myth: Wukong 4K | 3.756 | 2.679 (−28.7 %) | 6.974 (+85.7 %) | 5.682 (+51.3 %) | 9.025 (+140.3 %) | 4.560 (+21.4 %) |

**4X** (each generated frame = the sum of the 3 Evaluates of one real frame ÷ 3): Cyberpunk 2077 4K default 2.532 ms, preset C 1.392 ms (−45.0 %); Black Myth: Wukong 4K default 2.733 ms, preset C 1.517 ms (−44.5 %).

### Frame generation: real game captures (RTX 3080 Ti, offline replay)

RTX 3080 Ti, driver 610.88, full run of the same test package (on the tester's machine); same method as the table above. During these timings the GPU held a steady 1665 MHz (lower than the 1950 MHz during the synthetic-scene timing), so absolute values are conservative and the comparisons remain valid.

**2X**

| Capture | DLSS default | Preset C | XeSS Middle | XeSS Coarse | XeSS Fine | Stock kernels |
|---|---:|---:|---:|---:|---:|---:|
| Cyberpunk 2077 1440p | 1.651 | 1.032 (−37.5 %) | 2.235 (+35.4 %) | 1.786 (+8.2 %) | 2.791 (+69.1 %) | 2.381 (+44.2 %) |
| Black Myth: Wukong 1440p | 1.663 | 1.062 (−36.1 %) | 2.359 (+41.9 %) | 1.911 (+14.9 %) | 2.936 (+76.6 %) | 2.391 (+43.8 %) |
| Cyberpunk 2077 4K | 2.171 | 1.473 (−32.2 %) | 4.640 (+113.7 %) | 3.668 (+68.9 %) | 5.908 (+172.1 %) | 2.860 (+31.7 %) |
| Black Myth: Wukong 4K | 2.275 | 1.577 (−30.7 %) | 4.763 (+109.3 %) | 3.822 (+68.0 %) | 6.060 (+166.3 %) | 2.935 (+29.0 %) |

**4X**: Cyberpunk 2077 4K default 1.567 ms, preset C 0.822 ms (−47.5 %); Black Myth: Wukong 4K default 1.666 ms, preset C 0.897 ms (−46.2 %).

### Super-resolution INT8 vs. same-preset FP16 (RTX 3070, offline)

Same test package. GPU time of one super-resolution Evaluate, in ms: 64 measured frames per run, median, then the median of 3 rounds. Same runtime (310.7.129) and same preset; only INT8 versus FP16 is compared. The two kernel families correspond to The Witcher 3 / Resonance / Horizon 6 / Spider-Man (mvlo) and Black Myth: Wukong / Hogwarts (mvhi).

| Family | Output | Tier | L FP16 | L INT8 | L change | M FP16 | M INT8 | M change |
|---|---|---|---:|---:|---:|---:|---:|---:|
| mvlo | 3840×2160 | Performance | 7.443 | 6.459 | −13.2 % | 6.065 | 4.905 | −19.1 % |
| mvlo | 2560×1440 | Performance | 3.415 | 2.993 | −12.4 % | 2.787 | 2.294 | −17.7 % |
| mvlo | 1920×1080 | Performance | 1.965 | 1.736 | −11.7 % | 1.615 | 1.353 | −16.2 % |
| mvlo | 1920×1080 | Ultra Performance | 1.258 | 1.179 | −6.3 % | 1.066 | 0.967 | −9.3 % |
| mvhi | 3840×2160 | Performance | 7.305 | 6.127 | −16.1 % | 5.914 | 4.623 | −21.8 % |
| mvhi | 2560×1440 | Performance | 3.346 | 2.845 | −15.0 % | 2.719 | 2.171 | −20.2 % |
| mvhi | 1920×1080 | Performance | 1.934 | 1.656 | −14.4 % | 1.581 | 1.278 | −19.2 % |
| mvhi | 1920×1080 | Ultra Performance | 1.235 | 1.106 | −10.4 % | 1.057 | 0.914 | −13.5 % |

### Super-resolution INT8 vs. same-preset FP16 (RTX 3080 Ti, offline)

Same test package, same method, GPU at 1665 MHz.

| Family | Output | Tier | L FP16 | L INT8 | L change | M FP16 | M INT8 | M change |
|---|---|---|---:|---:|---:|---:|---:|---:|
| mvlo | 3840×2160 | Performance | 5.338 | 4.578 | −14.2 % | 4.341 | 3.455 | −20.4 % |
| mvlo | 2560×1440 | Performance | 2.484 | 2.151 | −13.4 % | 2.028 | 1.644 | −18.9 % |
| mvlo | 1920×1080 | Performance | 1.465 | 1.333 | −9.0 % | 1.208 | 1.047 | −13.4 % |
| mvlo | 1920×1080 | Ultra Performance | 1.000 | 0.948 | −5.2 % | 0.849 | 0.791 | −6.9 % |
| mvhi | 3840×2160 | Performance | 5.262 | 4.355 | −17.2 % | 4.252 | 3.239 | −23.8 % |
| mvhi | 2560×1440 | Performance | 2.437 | 2.045 | −16.1 % | 1.991 | 1.545 | −22.4 % |
| mvhi | 1920×1080 | Performance | 1.441 | 1.272 | −11.8 % | 1.186 | 0.994 | −16.1 % |
| mvhi | 1920×1080 | Ultra Performance | 0.979 | 0.892 | −8.9 % | 0.848 | 0.746 | −12.0 % |

### In game (RTX 3070, 4K output, 2X)

RTX 3070, driver 617.14, `[Logging] FrameTiming=1` logs; median over a 60 s idle window after entering the world, tier 2, XeSS at `Middle`. Per generated frame = GPU time of frame generation; real frame rate = frames the game renders (the displayed frame rate is 2× that).

| Game | DLSS default (ms) | XeSS (ms) | XeSS real frame rate | Preset C (ms) | Preset C real frame rate |
|---|---:|---:|---:|---:|---:|
| Cyberpunk 2077 | 3.755 | 6.733 (+79 %) | −8.6..−9.7 % | 2.748 (−27 %) | +3.6 % |
| Black Myth: Wukong | 4.035 | 7.308 (+81 %) | −11.0 % | 3.085 (−24 %) | +4.3 % |
| The Witcher 3 | 3.973 | 7.166 (+80 %) | −15.0 % | 2.974 (−25 %) | −0.8 % |
| A Plague Tale: Resonance | 3.943 | 6.968 (+77 %) | −28.1 % | 2.947 (−25 %) | +9.5 % |
| Forza Horizon 6 | 3.856 | 6.847 (+78 %) | −8.0 % | 2.803 (−27 %) | +7.4 % |

Cyberpunk 2077: path tracing + ray reconstruction, DLSS Ultra Performance; Black Myth: Wukong: DLSS 50 %. How much the real frame rate changes depends on where the game's bottleneck is: on The Witcher 3, preset C is 1 ms faster per generated frame but the frame rate does not change.

### Super-resolution INT8 in game (RTX 3070, The Witcher 3)

RTX 3070, driver 617.14, frame generation off, frame rate over an idle window after entering the world; the baseline is the same-preset FP16 (`Mode=M-FP16` / `L-FP16`), INT8 and FP16 alternated 2–4 runs each, the table shows means and the gain is by median. The preset M rows are 0.4.0's shipped kernel set (measured 10-10); the L rows were measured on 10-09 (the L set of this family has not changed since).

| Output | Tier (render) | Preset | FP16 fps | INT8 fps | Gain | Per frame |
|---|---|---|---:|---:|---:|---|
| 3840×2160 | Performance (1920×1080) | M | 47.15 | 50.85 | +7.85 % | 21.21 → 19.67 ms (−1.54) |
| 2560×1440 | Performance (1280×720) | M | 85.48 | 91.34 | +6.86 % | 11.70 → 10.95 ms (−0.75) |
| 1920×1080 | Performance (960×540) | M | 122.14 | 128.35 | +5.08 % | 8.19 → 7.79 ms (−0.40) |
| 3840×2160 | Ultra Performance (1280×720) | L | 63.56 | 66.13 | +4.0 % | 15.73 → 15.12 ms (−0.61) |
| 2560×1440 | Ultra Performance (853×480) | L | 109.42 | 111.80 | +2.2 % | 9.14 → 8.94 ms (−0.19) |
| 1920×1080 | Ultra Performance (640×360) | L | 145.83 | 147.57 | +1.2 % | 6.86 → 6.78 ms (−0.08) |

Except for the last row, the gains exceed twice the repeat-to-repeat difference of the same configuration; 1080p Ultra Performance L is within one run's fluctuation, but all 4 INT8 runs are above all 4 FP16 runs. Quality: with preset M, still images flicker slightly more than FP16 (in-game inter-frame stability of still pixels 61.8 vs 62.5 dB), and PSNR against ground truth is not lower than FP16; with preset L the difference from FP16 is ≤ 0.6 dB.

### Whole-group frame-generation GPU time (RTX 3080 Ti, synthetic scene)

RTX 3080 Ti, driver 610.88, 0.4.0 test package (on the tester's machine). Units are GPU milliseconds of the whole frame-generation group per real frame (the span of one group of Evaluates submitted together): 256 groups per run, median, then the median of 4 rounds; reduction = (tier0 − current) / tier0. tier0 = stock kernels (`Optimized=0`), tier1 = bit-identical acceleration (`1`, factory default), tier2 = lossy tier (`2`, 0.4.0's factory default). The consistency checks of the same package are bit-identical with the RTX 3070 reference: frame generation 18/18, real captures 28/28, super resolution 32/32, super resolution `Mode=On` 4/4.

| Resolution | Multiplier | tier0 (ms) | tier1 (ms) | Reduction | tier2 (ms) | Reduction |
|---|---|---:|---:|---:|---:|---:|
| 1080p | 2X | 1.364 | 0.926 | 32.1 % | 0.917 | 32.8 % |
| 1080p | 3X | 1.942 | 1.423 | 26.7 % | 1.400 | 27.9 % |
| 1080p | 4X | 2.520 | 1.914 | 24.0 % | 1.888 | 25.1 % |
| 1080p | 5X | 3.089 | 2.411 | 21.9 % | 2.375 | 23.1 % |
| 1080p | 6X | 3.677 | 2.911 | 20.8 % | 2.867 | 22.0 % |
| 1440p | 2X | 2.094 | 1.459 | 30.3 % | 1.443 | 31.1 % |
| 1440p | 3X | 3.030 | 2.238 | 26.1 % | 2.205 | 27.2 % |
| 1440p | 4X | 3.963 | 3.008 | 24.1 % | 2.960 | 25.3 % |
| 1440p | 5X | 4.900 | 3.786 | 22.7 % | 3.719 | 24.1 % |
| 1440p | 6X | 5.841 | 4.558 | 22.0 % | 4.472 | 23.4 % |
| 4K | 2X | 2.472 | 1.901 | 23.1 % | 1.864 | 24.6 % |
| 4K | 3X | 3.792 | 3.002 | 20.8 % | 2.924 | 22.9 % |
| 4K | 4X | 5.112 | 4.106 | 19.7 % | 3.987 | 22.0 % |
| 4K | 5X | 6.437 | 5.212 | 19.0 % | 5.054 | 21.5 % |
| 4K | 6X | 7.760 | 6.319 | 18.6 % | 6.123 | 21.1 % |

The lower the resolution and the lower the multiplier, the bigger the gain (closer to being launch / latency bound).

## Estimating the frame rate with frame generation

First, with frame generation off and the same scene, output resolution, DLSS super-resolution tier and quality settings, get the frame rate `F_off`. Convert it to the base frame time with `1000 / F_off`, then pick the whole-group frame-generation time `T_FG` (ms) from the table above by resolution, multiplier and kernel configuration.

```text
Base frame time        T_base (ms) = 1000 / F_off
Group time             T_group (ms) ≈ T_base + T_FG
Real-frame/group rate  G (groups/s) ≈ 1000 / T_group
Total frame rate       F_out (FPS)  ≈ G × M = 1000 × M / (1000 / F_off + T_FG)
```

`M` is the multiplier (2X…6X correspond to 2…6). A group contains one real frame and `M − 1` generated frames; the `T_FG` in the table already includes the whole group of generated frames and the shared preprocessing, so do not multiply it by `M − 1` again. With frame generation on, the real-frame / group rate `G` is lower than `F_off` with frame generation off.

Example: with frame generation off at about **50 FPS** (base frame time 20 ms), take `T_FG` from the RTX 3080 Ti **4K 4X** row above:

| Kernels | T_FG (ms) | Group time (ms) | Group rate (groups/s) | Estimated total frame rate |
|---|---|---|---|---|
| Stock kernels (tier0) | 5.112 | 25.112 | 39.8 | 159.3 FPS |
| Factory default (tier1) | 4.106 | 24.106 | 41.5 | 165.9 FPS |
| Lossy tier (tier2) | 3.987 | 23.987 | 41.7 | 166.8 FPS |

For other resolutions / multipliers use the matching row, and enter your own measured `F_off` for that setting. This is a rough estimate adding the base render time and the frame-generation group overhead; GPU resource contention, synchronisation, CPU overhead, frame caps and refresh rate all affect the real result, and the estimate is not guaranteed to equal a counter reading or the actual displayed frame rate.

## About 6X

6X (5 generated frames per real frame) is NVIDIA DLSS 4.5's Dynamic Multi Frame Generation. The bundled runtime supports it, and this project makes it work on Ampere (`Engine=DLSS` only).

**The factory `MaxGeneratedFrames` is `3` (4X); you must change it yourself for 6X.** The reason is that games with their own Dynamic MFG run straight up to the number written here by default, and `5` is too high for most people (public-repository issues #497 / #499); you can set it back to `5` at any time to get 6X. Whether you really get 6X also depends on the game:

- The game itself supports 6X (ships a newer Streamline frame-generation plug-in, and the menu offers 6X or Dynamic MFG): change `MaxGeneratedFrames` to `5` and that is all; 6X was measured to run stably.
- The game supports only 4X (ships an older 4X plug-in, which is the case for most games at present): the limit is set by the game-side plug-in and this project cannot raise it to 6X (that plug-in lays out its internal presentation queue for 4X at initialization, and forcing more frames goes out of bounds and breaks frame generation or crashes). Use 4X with such games.

## Field feedback

*Black Myth: Wukong*, *Cyberpunk 2077* and *Forza Horizon 6* were tested and enable 4X normally; *A Plague Tale: Resonance* defaults to 6X when frame generation is set to "Dynamic / Auto". On an RTX 3070, 0.4.0 ran DLSS default, preset C and XeSS (2X, 4K output) in each of these four games plus *The Witcher 3*, five games in all, with no crash, TDR or device removal, and the HUD complete in the screenshots. How frame interpolation looks depends on the base frame rate: with a low base frame rate, image corruption and edge artefacts can appear and get more obvious with higher multipliers, and in some games even 4X needs lower quality settings to raise the base frame rate before it looks good; this is how frame generation inherently behaves when frame-rate headroom is lacking, not a defect of this project.

## Known issues

- XeSS-FG (`Engine=XeSS`) is optional, and the default is still DLSS-G. On RTX 30 it is much slower per generated frame than DLSS-G (4K: RTX 3070 +77..+86 %, about +3 ms; RTX 3080 Ti offline +109..+114 %), the real frame rate is 8..15 % lower, and 28 % lower in *A Plague Tale: Resonance* (the cause of the extra wait is unidentified); VRAM is about 0.4–0.5 GB higher; `Engine=XeSS` goes up to 4X (`MaxGeneratedFrames` 1–3), and writing 4 or above leaves frame generation disabled (`max_generated_frames_unsupported`). The image-quality difference between HUD-less on / off has not been compared frame by frame. `[Debug] Capture` records nothing under XeSS.
- Cost of XeSS-FG (model 15) (RTX 3070, offline whole-route replay, `Middle`, DIRECT, GPU milliseconds per real frame together with its generated frames): at 4X, 1440p is 4.5–5.0 ms against 5.3–5.4 ms for the DLSS default (8–15 % faster); 4K is 10.3–10.8 ms against 7.9–8.5 ms for the DLSS default (27–30 % slower); preset C is the fastest at every tier. 2X uses model 18's graph and costs 4–5 % more than the old `Model=18` (+1.7 % measured in game on a 5070, with bit-identical output) (it copies the previous frame's depth as well, plus the back buffer when the HUD-less image is used). This is offline GPU time, not the game's frame rate. In-game real frame rate in *Black Myth: Wukong* 4K (R10G10B10A2, no HUD-less image): 3X 32.0 fps (DLSS default 34.0), 4X 28.5 fps (DLSS default 31.5). Quality was compared against frame-skipping ground truth on only 4 captures: at sequence positions close to the previous real frame (t = 1/4, 1/3) there is less ghosting than with model 18 + t, but it is not better in every scene.
- When XeSS cannot run, it does not fall back to DLSS-G: if the game's declared back buffer is not R8G8B8A8 / B8G8R8A8 / R10G10B10A2 (for example RGBA16F, commonly used for HDR; Intel's own XeSS-FG does not accept it either) or XeSS's objects cannot be created, the interpolation feature is refused (frame generation off, error-level `xefg_feature_refused`); problems found only while running (missing back buffer, depth, motion vectors or interpolation output, mismatched sizes, capture failure) log one error-level `xefg_kernel_fatal` with the reason, and the game exits with `0xE0D55001`. Factory `UseHudless=Auto` equals `0` (since 2026-11-10, C139), so the whole image, UI included, is interpolated and `xefg_hudless_unused` no longer appears; with `UseHudless=1` this is an `xefg_kernel_fatal` and the game exits with `0xE0D55001`.
- `[DLSSG] Preset=B` (force UI recomposition) takes effect only when the game provides both a HUD-less image and a UI plane, which most games do not; the log (at factory level) then records one `preset_ineffective` saying which one is missing, and the picture is the same as not writing `Preset=B`.
- When the game ships generation-A DLSS-G runtime (≤ 3.8.x, old Streamline plug-in, able to drive only 2X; for example The Witcher 3 next-gen, Bright Memory, Spider-Man Remastered, Atomic Heart, Delta Force), frame generation is not enabled since 2026-10-19 and the game's request is forwarded unchanged (error-level `game_runtime_generation` in the log); before that these games could enable 2X.
- Preset C has no comparison against the official preset C on RTX 40 / 50; on The Witcher 3 preset C is 1 ms faster per generated frame but the frame rate is unchanged (the bottleneck is elsewhere).
- `[FrameGeneration] CatmullRomWarp` has no corresponding kernel row in the 310.9.16 package: writing it refuses frame generation (error-level `detail_restore_row_unknown`; before 2026-10-20 it was merely ineffective). The experimental `DetailRestore*`, `[Compatibility] LaunchChains` / `PlainVariant` / `ChainBlock0` / `HardwareBilinear`, `[Runtime] CacheDirectory` and `[Debug] QueueTrace` were removed on 2026-10-21 and have not been read since 2026-10-26 (writing them is the same as not writing them).
- Explicit switches that cannot be honoured are always refused instead of switching to another path (2026-10-20): `[Runtime] Path` / `SRPath` pointing at the wrong file, a missing bundled runtime or super-resolution snippet (previously these fell back to the bundled one / handed back to the game's own, which on RTX 40 / 50 meant native 2X), and `KernelImage=Cubin` on RTX 40 / 50 (`kernel_image_unavailable`); when super-resolution install fails, this load of `nvngx_dlss.dll` is refused, so DLSS is unavailable in the game rather than running the stock FP16. How the game menu behaves after a refusal has not been observed in a real game.
- Super resolution: `Mode` pins M / L at every tier, so the Quality / Balanced / DLAA tiers are slower than the game's own K (accepted by the project owner); 0.4.0's `Mode=On` (which accelerated only the tiers where the game already used L / M) was removed.
- RTX 20: the three new kernels of 310.9.16 and XeSS were validated only by sm_75 forward JIT on an RTX 3070 and have not been run on real Turing; super-resolution INT8 had one smoke test on a 2080 Ti. RTX 40 / 50: the PTX route of frame generation has not been run on real hardware (except for the XeSS-FG results on an RTX 5070 in 0.4.2).
- When the game is killed or exits abnormally, the backend does not write XeSS's summary record (`xefg_summary`; affects diagnostics only).

## Diagnostics & limits

- The log is written to `dlssg_sm86\logs\dlssg_sm86.log` in the game directory: every module and every process appends to this one file. Each line starts with the local time, `pid N`, the EXE name and a level letter (E = Level 1 / I = 2 / D = 3), followed by a JSON record, for example `2026-10-22 14:03:11.482  pid 12345  Cyberpunk2077.exe  E  {"event":...}`; when looking at a single game, filter by EXE name or pid (`Select-String` / `findstr`). With `FrameTiming=1` there is also a plain-text `frame_timing.txt`, with one section appended per process. The default `[Logging] Level=1` records errors only; set `2` or `3` when troubleshooting.
- When frame generation does not take effect, first look in the log for `install.active=true` with `backend_install.status=0`; if missing, it is usually a driver / runtime mismatch and the reason is recorded. When installation fails, the load of `nvngx_dlssg.dll` is refused and frame generation is not enabled (`runtime_selection_failed`); the game's own runtime is no longer used instead.
- Frame generation is on but not a single frame is generated: check the log for `foreign_fg_mod`. If present, another frame-interpolation mod is installed in the game (rtx-encore, OptiScaler, an old ReShade companion plug-in, and so on); keep only one. When the game crashes, `fg_gate_crash` at the end of the log says in which module it crashed (`ours` indicates whether the code is this project's); please attach it when reporting problems.
- A drop in offline time must not be read as a measured in-game FPS gain; the real frame-rate gain depends on the game and where its bottleneck is.
- All INI keys, log fields, and capture / replay are in [`docs/INSTALL.en.md`](docs/INSTALL.en.md) and [`docs/CAPTURE.md`](docs/CAPTURE.md).

## Credits

- Coldwood1026's RTX 20-series / SM75 port (the source of the SM75 kernel family; see `THIRD_PARTY_NOTICES.txt`).
- NVIDIA's DLSS-G runtime, DLSS super-resolution snippet, models and pre/post-processing (bundled, unmodified).
- Intel's XeSS-FG model (shaders and weights, MIT license, see `THIRD_PARTY_NOTICES.txt`).
- MFG-Unlocker 1.6.0 (Michael Robles / dashdogy, MIT): the model 15 port used by XeSS-FG and the approach for unlocking the frame count; 46 of its programs were adapted from Intel's programs and fall under the Intel XeSS SDK terms. The author of the "XessFG Model 15 Final" data pack found the way to relabel model 15 as model 16.

## Source & build

- The C++ source of the proxy, backend, tests and tools is in the source repository and is built with `build.ps1` (the default runtime model is 310.9.16, `-Runtime310916` points at the runtime; `-RuntimeModel 310.9` is a development alternative used with `-Runtime3109`; `-KernelPack` points at the kernel pack). For steps, tests and submission rules see `CONTRIBUTING.md` in the source repository. `docs/PROJECT.md` describes the source tree.
- The kernel pack `dlssg_sm86\kernels\fg_kernels.dll`, the super-resolution kernel pack, the XeSS model pack and the NVIDIA runtime are not in the source; take them from the release package of the same version. Without the kernel pack, `Engine=DLSS` frame generation is refused (log `kernel_pack_missing`, `fg_kernels_unavailable`) and the runtime's own kernels are not used instead; without the XeSS model pack, `Engine=XeSS` refuses interpolation (`xefg_unavailable`, interpolation off).

## License & third parties

- This project's C++ source is released under the MIT license (`LICENSE.md`, Copyright (c) 2026 sdli1995). The kernel images that ship in the package are provided as binaries only and are not under the MIT license.
- The bundled `nvngx_dlssg.dll` (310.9.16, SHA prefix `cadee1aa…`) sits as a file in `dlssg_sm86\runtime\`, and the super-resolution snippet `nvngx_dlss.dll` (310.9.16) in `dlssg_sm86\sr\`; both are unmodified. The kernel images in the kernel pack `dlssg_sm86\kernels\fg_kernels.dll` and in `dlssg_sm86\sr\sr_kernels.dll` were extracted, recompiled or rewritten from NVIDIA's kernels (the sm_75 family was imported from Coldwood1026) and are provided as binaries only, outside the MIT license. These are NVIDIA and upstream third-party materials and are not sublicensed with the source; see `THIRD_PARTY_NOTICES.txt`.
- The shaders and weights in `dlssg_sm86\xefg\xefg_kernels.dll` come from Intel XeSS-FG and ship under Intel's MIT license; the notice is in `THIRD_PARTY_NOTICES.txt`. The model 15 programs in the same DLL are taken from MFG-Unlocker 1.6.0 (MIT); 46 of them were adapted from Intel's XeSS SDK programs and fall under the Intel XeSS SDK terms, not under MFG-Unlocker's MIT or this project's `LICENSE.md`. Intel's `libxess_fg.dll` is not included.
