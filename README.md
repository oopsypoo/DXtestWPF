# DXtestWPF

A small **WPF + C#** test application that acts as:

- a **hardware / display diagnostics launcher**, and
- a **Direct3D 11 demo host**

The app was built as a modern replacement for the old "DirectX SDK sample launcher" style workflow:

- inspect the PC
- show graphics/display information
- then launch a separate 3D demo window

---

## What the project currently does

### WPF diagnostics launcher
The main window shows:

- machine and OS information
- .NET runtime and process architecture
- logical processor count
- physical memory summary
- detected display adapters
- detected monitors and current resolutions
- basic Direct3D probe results

### Direct3D 11 demo window
The demo window:

- opens in a separate WPF window
- hosts native Direct3D rendering through `HwndHost`
- creates a Direct3D 11 device and swap chain
- renders a **rotating textured cube**
- uses `Assets/world.png` as the cube texture
- supports:
  - `F11` to toggle borderless fullscreen
  - `Esc` to close the demo window

---

## Technology stack

- **C# / .NET**
- **WPF** for the launcher UI and demo window shell
- **WinForms** support for monitor enumeration via `Screen.AllScreens`
- **Vortice.Direct3D11** for Direct3D 11 bindings
- **Vortice.DXGI** for swap chain / DXGI integration
- **HLSL** shaders compiled with **`fxc.exe`** from the Windows SDK

---

## Project structure

| File / Folder | Purpose |
|---|---|
| `DXtestWPF/DXtestWPF.csproj` | Project configuration, NuGet references, shader compilation build step, asset copy rules |
| `DXtestWPF/MainWindow.xaml` | Launcher UI |
| `DXtestWPF/MainWindow.xaml.cs` | Loads diagnostics data and opens the Direct3D demo |
| `DXtestWPF/HardwareInfoService.cs` | Collects system, display, and basic Direct3D information |
| `DXtestWPF/Dx11DemoWindow.xaml` | Separate Direct3D demo window |
| `DXtestWPF/Dx11DemoWindow.xaml.cs` | Demo window behavior and keyboard handling |
| `DXtestWPF/Dx11RenderHost.cs` | WPF `HwndHost` that creates a native child window for Direct3D rendering |
| `DXtestWPF/D3D11Renderer.cs` | Direct3D 11 renderer, cube geometry, texture loading, render loop, resize handling |
| `DXtestWPF/Assets/world.png` | Texture used on all faces of the cube |
| `DXtestWPF/Shaders/TexturedCubeShader.md` | HLSL shader source for the textured cube |

> Note: the shader source is currently stored in a `.md` file because of earlier workspace restrictions during scaffolding. It contains HLSL source and is compiled by `fxc.exe` during build.

---

## Direct3D details implemented so far

### Device / swap chain
The renderer currently creates:

- a Direct3D 11 device
- an immediate context
- a DXGI swap chain
- a render target view
- a depth/stencil buffer and depth/stencil view

It first tries:

- **hardware** device creation

and falls back to:

- **WARP** if hardware creation fails

### Pipeline resources
The renderer sets up:

- vertex shader
- pixel shader
- input layout
- vertex buffer
- index buffer
- constant buffer
- sampler state
- rasterizer state
- texture shader resource view

### Scene
The demo currently renders:

- a textured cube
- perspective projection
- continuous rotation over time
- anisotropic texture sampling
- no lighting yet

---

## Direct3D information shown in the launcher

The launcher currently reports:

- whether Direct3D device creation used **hardware** or **WARP fallback**
- the detected **feature level**
- an **estimated shader model** based on feature level

> The shader model shown in the launcher is an estimate derived from feature level. It is not a full shader capability matrix.

---

## Build requirements

### Required
To build the shader bytecode for the cube demo, the project needs:

- a Windows SDK installation that includes **`fxc.exe`**

Typical location:

```text
C:\Program Files (x86)\Windows Kits\10\bin\<sdk-version>\x64\fxc.exe
```

### If auto-detection does not work
Set `FxcPath` explicitly in `DXtestWPF/DXtestWPF.csproj`:

```xml
<PropertyGroup>
  <FxcPath>C:\Program Files (x86)\Windows Kits\10\bin\<sdk-version>\x64\fxc.exe</FxcPath>
</PropertyGroup>
```

### Build-time shader output
The project compiles these shader objects during build:

- `Shaders/TexturedCubeVS.cso`
- `Shaders/TexturedCubePS.cso`

These are written to the build output folder and loaded by `D3D11Renderer` at runtime.

---

## How to run

1. Open the solution in **Visual Studio**.
2. Ensure the Windows SDK / HLSL compiler tools are installed.
3. Verify `FxcPath` if needed.
4. Build the project.
5. Run the app.
6. In the main window:
   - review the hardware / display information
   - click **Start DirectX 11 Demo**
7. In the demo window:
   - press `F11` for fullscreen
   - press `Esc` to close

---

## Notes from the implementation

This project intentionally stays fairly close to the Direct3D 11 pipeline. That means even a simple textured cube requires a fair amount of setup code:

- device creation
- swap chain creation
- render target / depth buffer setup
- geometry buffers
- HLSL shaders
- texture loading
- sampler configuration
- matrix updates per frame

That is expected for low-level Direct3D work. The Vortice libraries make the APIs accessible from C#, but they do not remove the underlying Direct3D concepts.

---

## Current limitations

At the moment, the project is intentionally simple and does **not** yet include:

- lighting
- camera controls
- wireframe toggle
- advanced shader capability reporting
- full monitor mode enumeration
- explicit adapter selection UI
- audio / input diagnostics

---

## Possible next steps

Some logical next enhancements would be:

- add basic directional lighting
- add mouse-controlled camera movement
- add wireframe / culling toggles
- report more Direct3D capability details
- allow monitor / display mode selection
- show adapter memory and driver information

---

## Summary

This repository now contains a working **WPF diagnostics launcher + Direct3D 11 textured cube demo** built with modern C# tooling.

It demonstrates:

- WPF and Direct3D interop
- basic hardware / display diagnostics
- Direct3D 11 initialization from C#
- textured 3D rendering with HLSL shaders
- fullscreen/windowed demo control
