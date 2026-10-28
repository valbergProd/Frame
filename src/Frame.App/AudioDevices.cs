using System.Runtime.InteropServices;

namespace Frame.App;

public sealed record AudioEndpoint(string Id, string Name);
public static class AudioDevices
{
    public static AudioEndpoint[] List(bool playback)
    {
        var result = new List<AudioEndpoint> { new("", "Windows default") };
        object? instance = null; IMMDeviceCollection? collection = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)instance).EnumAudioEndpoints(playback ? 0 : 1, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null; IPropertyStore? properties = null; PropVariant value = default;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device)); Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
                    result.Add(new(id, value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? id : id));
                }
                finally { PropVariantClear(ref value); if (properties != null) Marshal.ReleaseComObject(properties); if (device != null) Marshal.ReleaseComObject(device); }
            }
        }
        catch (COMException e) { throw new IOException("Windows could not list the audio devices. " + e.Message, e); }
        finally { if (collection != null) Marshal.ReleaseComObject(collection); if (instance != null) Marshal.ReleaseComObject(instance); }
        return result.ToArray();
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator
    { [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceCollection
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, out nint result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key); [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
