
namespace BootLauncherLite.Audio
{
    public enum AudioDeviceFlow
    {
        Render,
        Capture
    }

    public sealed class AudioDevice
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public AudioDeviceFlow Flow { get; init; }
        public bool IsDefault { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// CoreAudio-based audio service for enumerating and setting default devices.
    /// </summary>
    public sealed class AudioService
    {
        // ---------------------------
        // Public API
        // ---------------------------

        public IEnumerable<AudioDevice> GetDevices()
        {
            var list = new List<AudioDevice>();

            IMMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

                // Render (playback)
                list.AddRange(GetDevicesForFlow(enumerator, EDataFlow.eRender, AudioDeviceFlow.Render));

                // Capture (recording)
                list.AddRange(GetDevicesForFlow(enumerator, EDataFlow.eCapture, AudioDeviceFlow.Capture));
            }
            finally
            {
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }

            return list;
        }

        public void SetDefaultPlayback(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentNullException(nameof(deviceId));

            SetDefaultEndpoint(deviceId, AudioDeviceFlow.Render);
        }

        public void SetDefaultCapture(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentNullException(nameof(deviceId));

            SetDefaultEndpoint(deviceId, AudioDeviceFlow.Capture);
        }

        // ---------------------------
        // Internal helpers
        // ---------------------------

        private static IEnumerable<AudioDevice> GetDevicesForFlow(
            IMMDeviceEnumerator enumerator,
            EDataFlow dataFlow,
            AudioDeviceFlow flow)
        {
            var list = new List<AudioDevice>();

            IMMDeviceCollection? collection = null;
            IMMDevice? defaultDevice = null;
            string? defaultId = null;

            try
            {
                // Devices with state = ACTIVE
                Marshal.ThrowExceptionForHR(
                    enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out collection));

                Marshal.ThrowExceptionForHR(
                    enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.eMultimedia, out defaultDevice));

                defaultId = GetDeviceId(defaultDevice);

                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out var dev);
                    try
                    {
                        string id = GetDeviceId(dev);
                        string name = GetDeviceFriendlyName(dev);

                        list.Add(new AudioDevice
                        {
                            Id = id,
                            Name = name,
                            Flow = flow,
                            IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    finally
                    {
                        if (dev != null)
                            Marshal.ReleaseComObject(dev);
                    }
                }
            }
            finally
            {
                if (defaultDevice != null)
                    Marshal.ReleaseComObject(defaultDevice);
                if (collection != null)
                    Marshal.ReleaseComObject(collection);
            }

            return list;
        }

        private static string GetDeviceId(IMMDevice device)
        {
            device.GetId(out var ptr);
            if (ptr == IntPtr.Zero)
                return string.Empty;

            string id = Marshal.PtrToStringUni(ptr) ?? string.Empty;
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(ptr);
            return id;
        }

        private static string GetDeviceFriendlyName(IMMDevice device)
        {
            IPropertyStore? store = null;
            try
            {
                Marshal.ThrowExceptionForHR(
                    device.OpenPropertyStore(STGM_READ, out store));

                var key = PKEY_Device_FriendlyName;
                store.GetValue(ref key, out var value);

                string? name = value.GetValue();
                value.Clear();

                return string.IsNullOrWhiteSpace(name) ? "(Unnamed device)" : name;
            }
            finally
            {
                if (store != null)
                    Marshal.ReleaseComObject(store);
            }
        }

        private void SetDefaultEndpoint(string deviceId, AudioDeviceFlow flow)
        {
            // Map to eRender/eCapture
            EDataFlow dataFlow = flow == AudioDeviceFlow.Render
                ? EDataFlow.eRender
                : EDataFlow.eCapture;

            // Before we try to set default, verify that this device actually exists
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? dev = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

                Marshal.ThrowExceptionForHR(
                    enumerator.GetDevice(deviceId, out dev));

                // If GetDevice didn't throw, we assume it's valid.
            }
            finally
            {
                if (dev != null)
                    Marshal.ReleaseComObject(dev);
                if (enumerator != null)
                    Marshal.ReleaseComObject(enumerator);
            }

            // Use PolicyConfig to set the default endpoint for all roles (Console, Multimedia, Communications)
            IPolicyConfigVista? policy = null;
            try
            {
                policy = (IPolicyConfigVista)new PolicyConfigClient();

                var roles = new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications };

                foreach (var role in roles)
                {
                    int hr = policy.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                }
            }
            finally
            {
                if (policy != null)
                    Marshal.ReleaseComObject(policy);
            }
        }

        // ---------------------------
        // CoreAudio constants & COM interop
        // ---------------------------

        private const uint DEVICE_STATE_ACTIVE = 0x00000001;
        private const int STGM_READ = 0x00000000;

        private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY
        {
            // DEVPROPKEY for PKEY_Device_FriendlyName
            fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
            pid = 14
        };

        private enum EDataFlow
        {
            eRender = 0,
            eCapture = 1,
            eAll = 2
        }

        private enum ERole
        {
            eConsole = 0,
            eMultimedia = 1,
            eCommunications = 2
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection devices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice device);
            int RegisterEndpointNotificationCallback(IntPtr client);   // not used
            int UnregisterEndpointNotificationCallback(IntPtr client); // not used
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out uint count);
            int Item(uint index, out IMMDevice device);
        }


        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
            int GetId(out IntPtr ppstrId);
            int GetState(out uint state);
        }

        [ComImport]
        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint count);
            int GetAt(uint index, out PROPERTYKEY key);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public int pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr p;
            public int p2;

            public string? GetValue()
            {
                // VT_LPWSTR == 31
                const ushort VT_LPWSTR = 31;
                if (vt == VT_LPWSTR && p != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(p);
                }

                return null;
            }

            public void Clear()
            {
                PropVariantClear(ref this);
            }
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);

        // IPolicyConfigVista (Vista+): used to set default endpoint
        [ComImport]
        [Guid("568B9108-44BF-40B4-9006-86AFE5B5A620")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfigVista
        {
            // We only care about SetDefaultEndpoint, but the vtable order must match,
            // so we declare the preceding methods as placeholders.

            int GetMixFormat(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                IntPtr ppFormat);

            int GetDeviceFormat(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                int bDefault,
                IntPtr ppFormat);

            int SetDeviceFormat(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                IntPtr pEndpointFormat,
                IntPtr pMixFormat);

            int GetProcessingPeriod(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                int bDefault,
                IntPtr pmftDefault,
                IntPtr pmftMinimum);

            int SetProcessingPeriod(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                IntPtr pmftPeriod);

            int GetShareMode(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                IntPtr pMode);

            int SetShareMode(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                IntPtr pMode);

            int GetPropertyValue(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                ref PROPERTYKEY key,
                out PROPVARIANT pv);

            int SetPropertyValue(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                ref PROPERTYKEY key,
                ref PROPVARIANT pv);

            int SetDefaultEndpoint(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                ERole role);

            int SetEndpointVisibility(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName,
                int bVisible);
        }

        [ComImport]
        [Guid("294935CE-F637-4E7C-A41B-AB255460B862")]
        private class PolicyConfigClient
        {
        }
    }
}

