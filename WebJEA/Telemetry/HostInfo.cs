using System.Runtime.InteropServices;

namespace WebJEA.Telemetry;

public static class HostInfo
{
    public static void AddSystemMetrics(Dictionary<string, object> metrics)
    {
        metrics["CPUCount"] = Environment.ProcessorCount; // cpu count

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("Select MaxClockSpeed from Win32_Processor");
                foreach (var mgmtobj in searcher.Get())
                {
                    metrics["CPUMhz"] = mgmtobj["maxclockspeed"];
                }
            }
            catch
            {
            }
        }

        metrics["OS"] = RuntimeInformation.OSDescription; // os details
        metrics["RAM"] = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d, 1); // GB ram
    }
}
