using System;
using System.Runtime.InteropServices;

namespace LogisticsZero {
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct Cell {
        public int Id;
        public double Value;
        public int FormulaType; // 0: Raw, 1: SUM, 2: AVERAGE
        public int DepStart;
        public int DepCount;
        public ulong SyncState;
    }

    public static class EngineInterop {
        // The .NET Runtime automatically resolves "sovereign_engine" to 
        // sovereign_engine.dll on Windows or libsovereign_engine.so on Linux
        [DllImport("sovereign_engine", CallingConvention = CallingConvention.Cdecl, EntryPoint = "RecalculateEngine")]
        public static extern void Recalculate(
            [In, Out] Cell[] cells,
            int cellsCount,
            [In] int[] depIndices,
            int depIndicesCount,
            int threadCount
        );
    }
}
