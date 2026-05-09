using System;
using System.Runtime.InteropServices;
namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Reports physical CPU cores on Windows via <c>kernel32!GetLogicalProcessorInformation</c> (counts <c>RelationProcessorCore</c> entries).
    /// </summary>
    internal static class CpuTopology {

        /// <summary>
        /// Concurrent plate solves are capped at this fraction of <see cref="PhysicalCoreCount"/> (floor), leaving headroom for the OS and NINA.
        /// </summary>
        private const double PlateSolveParallelismFraction = 0.8;

        private static readonly Lazy<int> PhysicalCoreCountLazy = new(ComputePhysicalCoreCount);

        /// <summary>
        /// Physical CPU cores from kernel topology. Falls back to <see cref="Environment.ProcessorCount"/> if the API fails.
        /// </summary>
        public static int PhysicalCoreCount => PhysicalCoreCountLazy.Value;

        /// <summary>
        /// Upper bound for the concurrency dropdown and clamps: <c>floor(PhysicalCoreCount × 0.8)</c>, at least 1.
        /// </summary>
        public static int MaxPlateSolveParallelism =>
            Math.Max(1, (int)Math.Floor(PhysicalCoreCount * PlateSolveParallelismFraction));

        private static int ComputePhysicalCoreCount() {
            try {
                uint bufferBytes = 0;
                if (!NativeMethods.GetLogicalProcessorInformation(IntPtr.Zero, ref bufferBytes)) {
                    if (Marshal.GetLastWin32Error() != NativeMethods.ErrorInsufficientBuffer || bufferBytes == 0)
                        return FallbackPhysicalCoreCount();
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)bufferBytes);
                try {
                    if (!NativeMethods.GetLogicalProcessorInformation(buffer, ref bufferBytes))
                        return FallbackPhysicalCoreCount();

                    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION stride differs between 32- and 64-bit processes (winnt.h).
                    var stride = IntPtr.Size == 8 ? 32 : 24;
                    var relationshipOffset = IntPtr.Size == 8 ? 8 : 4;
                    var cores = 0;
                    for (var offset = 0; offset + stride <= (int)bufferBytes; offset += stride) {
                        var relationship = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(buffer, offset + relationshipOffset)));
                        if (relationship == (uint)LogicalProcessorRelationship.RelationProcessorCore)
                            cores++;
                    }

                    if (cores > 0)
                        return cores;
                } finally {
                    Marshal.FreeHGlobal(buffer);
                }
            } catch {
                // Unexpected native/layout failure — use fallback.
            }

            return FallbackPhysicalCoreCount();
        }

        private static int FallbackPhysicalCoreCount() =>
            Math.Max(1, Environment.ProcessorCount);

        private enum LogicalProcessorRelationship : uint {
            RelationProcessorCore = 0,
        }

        private static class NativeMethods {

            internal const int ErrorInsufficientBuffer = 122;

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint returnLength);
        }
    }
}
