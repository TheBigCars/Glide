using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// Read-only HID++ discovery. No DPI, profile, or device-mode writes.
internal static class Probe
{
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData
    { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes
    { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices;
        public ushort OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(SafeFileHandle h, IntPtr b, int n, IntPtr count, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(SafeFileHandle h, IntPtr b, int n, IntPtr count, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out int count, bool wait);
    [DllImport("kernel32.dll")] static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);
    [DllImport("kernel32.dll")] static extern IntPtr CreateEvent(IntPtr attr, bool manual, bool initial, string name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    sealed class Operation : IDisposable
    {
        SafeFileHandle handle;
        IntPtr bytes, overlap, signal;
        bool started;
        public Operation(SafeFileHandle h, int length, byte[] write)
        {
            handle=h; bytes=Marshal.AllocHGlobal(length);
            overlap=Marshal.AllocHGlobal(32); Marshal.Copy(new byte[32],0,overlap,32);
            signal=CreateEvent(IntPtr.Zero,true,false,null);
            Marshal.WriteIntPtr(overlap,IntPtr.Size==8?24:16,signal);
            if(write!=null)Marshal.Copy(write,0,bytes,length);
            bool ok=write==null?ReadFile(h,bytes,length,IntPtr.Zero,overlap):WriteFile(h,bytes,length,IntPtr.Zero,overlap);
            int error=Marshal.GetLastWin32Error();
            if(!ok && error!=997) { Dispose(); throw new IOException("HID I/O error "+error); }
            started=true;
        }
        public byte[] Finish(int timeout, CancellationToken cancellation = default(CancellationToken))
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(true)
            {
                cancellation.ThrowIfCancellationRequested();
                if(WaitForSingleObject(signal,(uint)Math.Max(1,Math.Min(50,timeout-(int)watch.ElapsedMilliseconds)))==0)break;
                if(watch.ElapsedMilliseconds>=timeout)throw new TimeoutException("Mouse response timed out");
            }
            int count;
            if(!GetOverlappedResult(handle,overlap,out count,false))throw new IOException("HID completion error "+Marshal.GetLastWin32Error());
            byte[] result=new byte[count]; Marshal.Copy(bytes,result,0,count); return result;
        }
        public void Dispose()
        {
            if(started) { CancelIoEx(handle,overlap); int count; GetOverlappedResult(handle,overlap,out count,true); started=false; }
            if(signal!=IntPtr.Zero) { CloseHandle(signal); signal=IntPtr.Zero; }
            if(bytes!=IntPtr.Zero) { Marshal.FreeHGlobal(bytes); bytes=IntPtr.Zero; }
            if(overlap!=IntPtr.Zero) { Marshal.FreeHGlobal(overlap); overlap=IntPtr.Zero; }
        }
    }

    internal sealed class Endpoint : IDisposable
    {
        public SafeFileHandle Handle;
        public int InputLength, OutputLength;
        public byte Device;
        public ushort Product;
        public CancellationToken Cancellation;
        internal static int RequestCount;
        byte softwareId = 1;
        public byte[] Query(byte feature, byte function, params byte[] args)
        {
            Cancellation.ThrowIfCancellationRequested();
            if(args.Length>16)throw new ArgumentException("HID++ payload exceeds one report.");
            Interlocked.Increment(ref RequestCount);
            byte tag = (byte)((function << 4) | softwareId);
            softwareId = (byte)(softwareId == 15 ? 1 : softwareId + 1);
            byte[] packet = new byte[OutputLength];
            packet[0]=0x11; packet[1]=Device; packet[2]=feature; packet[3]=tag;
            Array.Copy(args, 0, packet, 4, args.Length);
            using(var write=new Operation(Handle,packet.Length,packet))write.Finish(1500,Cancellation);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < deadline)
            {
                byte[] reply;
                using(var read=new Operation(Handle,InputLength,null))reply=read.Finish((int)(deadline-DateTime.UtcNow).TotalMilliseconds,Cancellation);
                int length=reply.Length;
                if (length < 7 || reply[1]!=Device) continue;
                if ((reply[2]==0xFF || reply[2]==0x8F) && reply[3]==feature && reply[4]==tag)
                    throw new IOException("HID++ error 0x"+reply[5].ToString("X2"));
                if (reply[2]!=feature || reply[3]!=tag) continue;
                byte[] payload=new byte[length-4]; Array.Copy(reply,4,payload,0,payload.Length); return payload;
            }
            throw new TimeoutException("No matching mouse response");
        }
        public void Dispose() { Handle.Dispose(); }
    }

    static void Read(Endpoint ep, string label, byte feature, byte function, params byte[] args)
    {
        try { Console.WriteLine(label+": "+BitConverter.ToString(ep.Query(feature,function,args))); }
        catch (Exception ex) { Console.WriteLine(label+": "+ex.GetBaseException().Message); }
    }
    static void Inspect(Endpoint ep)
    {
        byte[] ping=ep.Query(0,1,0,0,0xA7);
        Console.WriteLine("Mouse slot "+ep.Device+", HID++ ping: "+BitConverter.ToString(ping));
        foreach (ushort id in new ushort[]{0x0003,0x0005,0x1000,0x1001,0x1004,0x2201,0x2202,0x8061,0x8100})
        {
            byte[] f=ep.Query(0,0,(byte)(id>>8),(byte)id);
            Console.WriteLine("Feature "+id.ToString("X4")+": "+BitConverter.ToString(f));
            byte index=f[0]; if(index==0) continue;
            switch(id)
            {
                case 0x0003:
                    int entities=ep.Query(index,0)[0];
                    for(byte entity=0;entity<entities;entity++) Read(ep,"Firmware "+entity,index,1,entity);
                    break;
                case 0x0005:
                    int count=ep.Query(index,0)[0];
                    var name=new List<byte>();
                    while(name.Count<count) { byte[] part=ep.Query(index,1,(byte)name.Count); int n=Math.Min(count-name.Count,part.Length); for(int j=0;j<n;j++)name.Add(part[j]); }
                    Console.WriteLine("Mouse name: "+System.Text.Encoding.UTF8.GetString(name.ToArray())); break;
                case 0x1000: Read(ep,"Battery status",index,0); break;
                case 0x1001: Read(ep,"Battery voltage",index,0); break;
                case 0x1004: Read(ep,"Battery capabilities",index,0); Read(ep,"Unified battery",index,1); break;
                case 0x2201: Read(ep,"DPI",index,2,0); break;
                case 0x2202:
                    Read(ep,"Sensor capabilities",index,1,0);
                    Read(ep,"DPI ranges",index,2,0,0,0);
                    Read(ep,"Extended DPI",index,5,0); break;
                case 0x8061:
                    Read(ep,"Report rate capabilities",index,1);
                    Read(ep,"Current report rate",index,2); break;
                case 0x8100:
                    Read(ep,"Onboard description",index,0);
                    Read(ep,"Onboard mode",index,2);
                    Read(ep,"Active profile",index,4);
                    Read(ep,"Active DPI slot",index,0xB);
                    Read(ep,"Profile directory 0",index,5,0,0,0,0);
                    Read(ep,"Profile directory 16",index,5,0,0,0,16);
                    for(int offset=0;offset<255;offset+=16) Read(ep,"Profile bytes "+Math.Min(offset,239),index,5,0,1,0,(byte)Math.Min(offset,239));
                    break;
            }
        }
    }
    internal static IEnumerable<Endpoint> Enumerate()
    {
        Guid guid; HidD_GetHidGuid(out guid);
        IntPtr set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,0x12);
        try
        {
            for(uint i=0;;i++)
            {
                InterfaceData data=new InterfaceData(); data.Size=Marshal.SizeOf(data);
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref data))break;
                uint required; SetupDiGetDeviceInterfaceDetail(set,ref data,IntPtr.Zero,0,out required,IntPtr.Zero);
                IntPtr detail=Marshal.AllocHGlobal((int)required);
                string path;
                try {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref data,detail,required,out required,IntPtr.Zero))continue;
                    path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                } finally { Marshal.FreeHGlobal(detail); }
                if(path.IndexOf("vid_046d",StringComparison.OrdinalIgnoreCase)<0)continue;
                using(var metadata=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero))
                {
                    if(metadata.IsInvalid)continue;
                    Attributes attr=new Attributes(); attr.Size=Marshal.SizeOf(attr);
                    if(!HidD_GetAttributes(metadata,ref attr))continue;
                    IntPtr pp; if(!HidD_GetPreparsedData(metadata,out pp))continue;
                    Caps caps;
                    try { if(HidP_GetCaps(pp,out caps)<0)continue; } finally { HidD_FreePreparsedData(pp); }
                    if(caps.UsagePage<0xFF00 || caps.InputLength!=20 || caps.OutputLength!=20)continue;
                    var handle=CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
                    if(handle.IsInvalid) { handle.Dispose(); continue; }
                    yield return new Endpoint{Handle=handle,InputLength=caps.InputLength,OutputLength=caps.OutputLength,Product=attr.Product};
                }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
    public static int Main()
    {
        bool found=false;
        foreach(var ep in Enumerate())using(ep)
        {
            Console.WriteLine("Logitech endpoint PID "+ep.Product.ToString("X4"));
            foreach(byte slot in new byte[]{1,0xFF})
            {
                ep.Device=slot;
                try { Inspect(ep); found=true; break; } catch(Exception ex) { Console.WriteLine("Slot "+slot+": "+ex.GetBaseException().Message); }
            }
        }
        return found?0:1;
    }
}
