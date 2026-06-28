# Logistics-Zero BareMetal

A high-performance, air-gapped, zero-telemetry sovereign desktop application designed for the KSAU-HS Intranet. Logistics-Zero combines a C++20 spreadsheet recalculation engine with a C# OLAP pivot aggregator and a direct bare-metal label printing service.

## Architectural Components

1. **C++20 Recalculation Engine (`sovereign_engine.cpp`)**
   - Utilizes C++20 `std::span` for zero-copy memory access directly on C#-managed buffers, completely bypassing .NET Garbage Collection overhead.
   - Lock-free Speculative Reevaluation: Uses CAS (`std::atomic_ref`) and Thread IDs as tie-breakers to resolve cyclic dependencies without deadlocks or traditional mutexes.

2. **C# FFI & OLAP Bridge (`EngineInterop.cs`, `PivotEngine.cs`)**
   - Interops with the C++20 native shared library (`sovereign_engine`) via P/Invoke.
   - Integrates `DuckDB.NET` to query a local `SQLite` database vault running in WAL (Write-Ahead Logging) mode, delivering O(1) attribute aggregations over 10,000+ data rows.

3. **Direct Thermal Printing (`GiaiPrintService.cs`)**
   - Uses `System.Drawing.Printing` to target thermal printers (e.g. Zebra ZT411) directly.
   - Bypasses standard interactive Windows dialogs to enable clinical-speed bare-metal tag printing formatted strictly under the GS1 GIAI identifier `(8004)`.

4. **Canvas2D User Interface (`univer_executive.html`)**
   - High-fidelity dark mode clinical glassmorphic UI.
   - Embedded inside C# WebView2, using an interactive Canvas2D grid.

---

## Build Instructions (Windows / Linux)

### 1. Compiling the C++ Engine

**On Windows (MSVC):**
```cmd
cl.exe /O2 /LD /std:c++20 sovereign_engine.cpp /link /out:sovereign_engine.dll
```

**On Linux (GCC):**
```bash
g++ -O3 -shared -fPIC -std=c++20 sovereign_engine.cpp -o libsovereign_engine.so
```

### 2. Building the C# Application

1. Place the compiled binary (`sovereign_engine.dll` or `libsovereign_engine.so`) in your C# output directory.
2. Initialize and restore the NuGet dependencies:
   ```bash
   dotnet add package Microsoft.Data.Sqlite
   dotnet add package DuckDB.NET.Data
   ```
3. Run the application:
   ```bash
   dotnet run
   ```
