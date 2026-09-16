using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CodexUsageWidget.Infrastructure.Windows;

public static class CompanionScheduledStartup
{
    public static string TaskName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return "CodexPetCompanion-" + identity.User!.Value;
        }
    }

    // null means this portable copy has not installed a recovery task.
    public static bool? GetEnabled() => AccessTask(null);
    public static bool TrySetEnabled(bool enabled) => AccessTask(enabled) is not null;

    private static bool? AccessTask(bool? enabled)
    {
        object? service = null, folder = null, task = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service!).GetFolder(@"\");
            try { task = ((dynamic)folder).GetTask(TaskName); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
            if (enabled.HasValue) ((dynamic)task).Enabled = enabled.Value;
            return (bool)((dynamic)task).Enabled;
        }
        finally
        {
            if (task is not null) Marshal.FinalReleaseComObject(task);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            if (service is not null) Marshal.FinalReleaseComObject(service);
        }
    }
}
