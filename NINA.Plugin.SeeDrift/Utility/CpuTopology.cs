using System;
using System.Globalization;
using System.Management;
namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Reports physical CPU cores on Windows (sum of <c>Win32_Processor.NumberOfCores</c> across sockets).
    /// </summary>
    internal static class CpuTopology {

        private static readonly Lazy<int> PhysicalCoreCountLazy = new(ComputePhysicalCoreCount);

        /// <summary>
        /// Physical CPU cores. Uses WMI; falls back to <see cref="Environment.ProcessorCount"/> if WMI fails.
        /// </summary>
        public static int PhysicalCoreCount => PhysicalCoreCountLazy.Value;

        private static int ComputePhysicalCoreCount() {
            try {
                using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
                var total = 0;
                foreach (ManagementBaseObject mo in searcher.Get()) {
                    using (mo)
                        total += Convert.ToInt32(mo["NumberOfCores"], CultureInfo.InvariantCulture);
                }

                if (total > 0)
                    return total;
            } catch {
                // WMI unavailable or access denied
            }

            return Math.Max(1, Environment.ProcessorCount);
        }
    }
}
