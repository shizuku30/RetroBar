using System;
using System.Runtime.InteropServices;

namespace RetroBar.Utilities
{
    public record PowerState(bool IsKnown, bool HasBattery, bool OnAcPower, bool Charging, int Percent)
    {
        public static readonly PowerState Unknown = new PowerState(false, false, false, false, -1);

        public static PowerState Read()
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status) || status.BatteryFlag == 255 || status.ACLineStatus == 255)
                return Unknown;
            return new PowerState(true, (status.BatteryFlag & 128) == 0, status.ACLineStatus == 1,
                (status.BatteryFlag & 8) != 0, status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : -1);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    }
}
