# DllCompare

DllCompare is a Windows desktop tool for comparing the metadata shape of .NET DLL files.

It is designed for cases where two rebuilt assemblies are binary-different, but should expose the same code structure. Instead of comparing raw bytes, DllCompare reads .NET metadata and compares namespaces, types, methods, and method signatures.

## Features

- Compare two managed `.dll` files without loading or executing them.
- Read assembly metadata using `System.Reflection.Metadata`.
- Compare:
  - namespaces
  - classes and interfaces
  - nested types
  - type visibility and modifiers
  - method names
  - method visibility and modifiers
  - return types
  - parameter types
  - `ref`, `out`, and `in` parameters
  - generic method arity
- Side-by-side tree view inspired by diff tools such as Beyond Compare.
- Drag and drop DLL files onto either side of the window.
- Automatically compare when both sides have a DLL selected.
- Highlight differences:
  - green for items added on the rebuilt side
  - red for items removed from the rebuilt side
  - gray for unchanged items
- Automatically expand parent nodes that contain differences.

## Use Case

This tool is useful when you rebuild or reproduce a .NET assembly and want to verify that its public or internal structure is unchanged, even if the resulting binary is not byte-for-byte identical.

Examples:

- Comparing an original DLL with a rebuilt DLL.
- Checking whether a decompiled and recompiled assembly preserved its type and method structure.
- Verifying that build system changes did not alter the assembly metadata shape.

## Screenshots

Screenshots are not included yet. Add images under a `docs/` or `assets/` folder and reference them here when publishing the project.

## Requirements

- Windows 10 version 1809 or later.
- .NET SDK 8.0 or later.
- Visual Studio 2022 is recommended for WinUI development.

The project is configured as a WinUI 3 desktop app and uses Windows App SDK self-contained deployment for easier local execution.

## Build

From the repository root:

```powershell
dotnet build "DllCompare.sln" -p:Platform=x64
```

The debug executable is generated at:

```text
DllCompare\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\DllCompare.exe
```

## Run

After building, run:

```powershell
& "DllCompare\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\DllCompare.exe"
```

You can also run the project from Visual Studio using the unpackaged profile.

## Usage

1. Drag the original DLL onto the left side, or click `Browse...` under `Original`.
2. Drag the rebuilt DLL onto the right side, or click `Browse...` under `Rebuilt`.
3. Once both files are selected, comparison starts automatically.
4. Expand highlighted nodes to inspect differences.

## What It Does Not Compare Yet

DllCompare currently focuses on metadata structure, not implementation details.

Not yet compared:

- IL method bodies
- fields
- properties as first-class nodes
- events as first-class nodes
- custom attributes
- assembly references
- resources
- PDB/debug symbols
- native exports
- unmanaged DLL files

Property and event accessors may still appear as metadata methods, depending on how the compiler emitted them.

## Design Notes

DllCompare avoids `Assembly.Load` for target DLLs. This keeps comparison safer and avoids dependency resolution issues caused by executing or loading arbitrary assemblies into the current process.

The current comparison model treats metadata items as stable string keys. If a signature changes, the old signature appears as removed on the left and the new signature appears as added on the right.

## Roadmap

Potential future improvements:

- Add fields, properties, and events as dedicated tree nodes.
- Add filters for public API only vs all metadata.
- Add search and next/previous difference navigation.
- Add synchronized scrolling and node expansion between left and right trees.
- Export comparison reports as text, JSON, or HTML.
- Add command-line comparison mode for CI usage.
- Add tests for metadata parsing and signature formatting.

## License

No license file has been added yet. If you plan to open source this project, add a `LICENSE` file before publishing.

Common choices include MIT, Apache-2.0, and GPL-3.0.
