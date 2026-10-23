using ManagedShell.Common.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace RetroBar.Utilities
{
    public record VolumeState(bool IsKnown, bool HasDevice, bool Muted, float Level)
    {
        public static readonly VolumeState Unknown = new VolumeState(false, false, false, 0);
    }

    public sealed class VolumeMonitor : IDisposable
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Callback _callback;
        private IMMDeviceEnumerator _enumerator;
        private IAudioEndpointVolume _endpoint;
        private volatile bool _disposed;
        private int _queued;
        private int _reconnect;
        public VolumeState State { get; private set; } = VolumeState.Unknown;
        public event EventHandler StateChanged;

        public VolumeMonitor()
        {
            _callback = new Callback(this);
            try
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_callback));
                Connect();
                Refresh();
            }
            catch (COMException ex)
            {
                ShellLogger.Warning($"VolumeMonitor: Audio status unavailable: {ex.Message}");
            }
        }

        private void Connect()
        {
            ReleaseEndpoint();
            if (_enumerator == null) return;
            IMMDevice device = null;
            try
            {
                int result = _enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                if (result == unchecked((int)0x80070490))
                {
                    SetState(new VolumeState(true, false, false, 0));
                    return;
                }
                Marshal.ThrowExceptionForHR(result);
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out object endpoint));
                _endpoint = (IAudioEndpointVolume)endpoint;
                Marshal.ThrowExceptionForHR(_endpoint.RegisterControlChangeNotify(_callback));
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
            }
        }

        public void Refresh()
        {
            if (_disposed) return;
            try
            {
                if (_endpoint == null) return;
                Marshal.ThrowExceptionForHR(_endpoint.GetMasterVolumeLevelScalar(out float level));
                Marshal.ThrowExceptionForHR(_endpoint.GetMute(out bool muted));
                SetState(new VolumeState(true, true, muted, Math.Clamp(level, 0, 1)));
            }
            catch (COMException)
            {
                SetState(VolumeState.Unknown);
            }
        }

        private void SetState(VolumeState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void QueueRefresh(bool reconnect)
        {
            if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            if (reconnect) Interlocked.Exchange(ref _reconnect, 1);
            if (Interlocked.Exchange(ref _queued, 1) != 0) return;
            try
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Interlocked.Exchange(ref _queued, 0);
                    if (_disposed) return;
                    try
                    {
                        if (Interlocked.Exchange(ref _reconnect, 0) != 0) Connect();
                        Refresh();
                    }
                    catch (COMException)
                    {
                        SetState(VolumeState.Unknown);
                    }
                }));
            }
            catch (InvalidOperationException) when (_disposed || _dispatcher.HasShutdownStarted)
            {
                Interlocked.Exchange(ref _queued, 0);
            }
        }

        private void ReleaseEndpoint()
        {
            if (_endpoint == null) return;
            try { _endpoint.UnregisterControlChangeNotify(_callback); }
            catch (COMException) { }
            Marshal.ReleaseComObject(_endpoint);
            _endpoint = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseEndpoint();
            if (_enumerator != null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_callback); }
                catch (COMException) { }
                Marshal.ReleaseComObject(_enumerator);
                _enumerator = null;
            }
        }

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        public sealed class Callback : IAudioEndpointVolumeCallback, IMMNotificationClient
        {
            private readonly VolumeMonitor _owner;
            internal Callback(VolumeMonitor owner) { _owner = owner; }
            public int OnNotify(IntPtr data) { _owner.QueueRefresh(false); return 0; }
            public int OnDeviceStateChanged(string id, uint state) { _owner.QueueRefresh(true); return 0; }
            public int OnDeviceAdded(string id) { _owner.QueueRefresh(true); return 0; }
            public int OnDeviceRemoved(string id) { _owner.QueueRefresh(true); return 0; }
            public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == 0) _owner.QueueRefresh(true); return 0; }
            public int OnPropertyValueChanged(string id, PropertyKey key) { return 0; }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice endpoint);
            [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient callback);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
            [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }

        [Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
        public interface IAudioEndpointVolumeCallback
        {
            [PreserveSig] int OnNotify(IntPtr data);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PropertyKey { public Guid FormatId; public uint Id; }

        [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
        public interface IMMNotificationClient
        {
            [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
            [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
        }
    }
}
