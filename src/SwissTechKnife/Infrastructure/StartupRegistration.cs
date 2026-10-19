using System;
using Microsoft.Win32;
using SwissTechKnife.Core;

namespace SwissTechKnife.Infrastructure
{
    /// <summary>"Iniciar com o Windows": per-user Run value (no elevation). The registry is the source of truth.</summary>
    internal static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SwissTechKnife";
        public const string StartupArgument = "--startup";

        public static bool IsEnabled(string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                string value = key?.GetValue(ValueName) as string;
                return value != null && value.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static bool TrySet(string exePath, bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + exePath + "\" " + StartupArgument);
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Startup", "Could not update the Run value", ex);
                return false;
            }
        }
    }
}
