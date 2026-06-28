using System;
using System.Runtime.InteropServices;

namespace LogisticsZero {
    public enum CellState : uint {
        Dirty = 0,
        Computing = 1,
        UpToDate = 2,
        Cycle = 3
    }

    public enum OpType : byte {
        Const = 0,
        CellRef = 1,
        Add = 2,
        Sub = 3,
        Mul = 4,
        Div = 5,
        Sum = 6,
        Avg = 7
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ExpressionNode {
        public OpType Type;
        public double ConstValue;
        public int CellRefIdx;
        public int RangeStartIdx;
        public int RangeEndIdx;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct Cell {
        public int Id;
        public double Value;
        public int ExprStart;
        public int ExprCount;
        public ulong SyncState; // Upper 32 bits: Thread ID, Lower 32 bits: CellState
    }

    public static partial class EngineInterop {
        [LibraryImport("sovereign_engine", EntryPoint = "RecalculateEngine")]
        public static partial void Recalculate(
            [In, Out] Cell[] cells,
            int cellsCount,
            [In] ExpressionNode[] exprNodes,
            int exprNodesCount,
            int threadCount
        );
    }
}
