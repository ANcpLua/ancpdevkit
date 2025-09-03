[![.NET 8.0/9.0](https://img.shields.io/badge/.NET-8.0%7C9.0-7C3AED)](https://dotnet.microsoft.com/download/dotnet)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ANcpLua/ancpdevkit/blob/main/LICENSE)

# ANCP Devkit

Two cross-platform .NET tools for text and code cleanup:

- **f**: Count words in text files (txt/md/docx) with optional comment stripping
- **fcc**: Strip C# comments and regions in place (Roslyn-based)

## Prerequisites

- .NET SDK 8.0+ or 9.0

## Installation

```bash
# From NuGet (recommended)
dotnet tool install -g ancp
dotnet tool install -g ancp.fcc

# From source
./scripts/install_tools.sh
# OR
make install
```

## Usage

### f - Word Counter

```bash
f path/to/file.txt              # Count total words
f --unique path/to/file.docx    # Count unique words  
f --strip path/to/README.md     # Strip comments before output
```

### fcc - C# Comment Stripper

```bash
fcc path/to/File.cs              # Process single file
fcc path/to/Folder               # Process folder recursively
```

## Uninstall

```bash
dotnet tool uninstall --global ancp
dotnet tool uninstall --global ancp.fcc
```

## License

This project is licensed under the [MIT License](LICENSE).