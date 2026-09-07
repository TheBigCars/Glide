using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

internal sealed class MouseProfile
{
    public int Sector;
    public string Name;
    public override string ToString() { return Sector + "  ·  " + Name; }
}

internal sealed class MouseState
{
    public int Battery, BatteryStatus, Dpi, DpiY, Lod, Profile, Slot, PollingIndex = -1;
    public string Connection, SaveUnavailable;
    public int[] Values;
    public byte[] ProfileBytes;
    public MouseProfile[] Profiles = new MouseProfile[0];
    public string ProfileName, Firmware, DeviceKey, Model;
    public ushort VendorId, ProductId;
    public bool CanSave { get { return SaveUnavailable == null; } }
    public string ChargeText
    {
        get
        {
            switch (BatteryStatus)
            {
                case 0: return "Not charging";
                case 1: case 2: case 4: return "Charging";
                case 3: return "Fully charged";
                case 5: return "Battery error";
                case 6: return "Temperature warning";
                default: return "Charging status unknown";
            }
        }
    }
}

internal sealed class MouseDevice : IDisposable
{
    readonly Probe.Endpoint ep;
    readonly byte battery, dpi, onboard, reportRate;
    MouseDevice(Probe.Endpoint endpoint)
    {
        ep = endpoint;
        battery = Feature(0x1004); dpi = Feature(0x2202); onboard = Feature(0x8100); reportRate = Feature(0x8061);
        if (battery == 0 || dpi == 0 || onboard == 0) throw new IOException("This mouse firmware is not supported yet.");
    }
    byte Feature(ushort id) { return ep.Query(0, 0, (byte)(id >> 8), (byte)id)[0]; }
    public static MouseDevice Connect(CancellationToken cancellation = default(CancellationToken))
    {
        foreach (var endpoint in Probe.Enumerate())
        {
            bool keep = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                endpoint.Cancellation = cancellation;
                // Limit v0.1 to this model's known wired and LIGHTSPEED identifiers.
                if (endpoint.Product != 0xC54D && endpoint.Product != 0xC09B) continue;
                endpoint.Device = endpoint.Product == 0xC09B ? (byte)255 : (byte)1;
                byte[] ping = endpoint.Query(0, 1, 0, 0, 0xA7);
                if (ping[0] < 2 || ping[2] != 0xA7) continue;
                byte nameFeature = endpoint.Query(0, 0, 0, 5)[0];
                if (nameFeature == 0) continue;
                int length = endpoint.Query(nameFeature, 0)[0];
                if (length < 1 || length > 100) continue;
                var name = new List<byte>();
                while (name.Count < length) name.AddRange(endpoint.Query(nameFeature, 1, (byte)name.Count).Take(length - name.Count));
                if (Encoding.UTF8.GetString(name.ToArray()) != "PRO X 2") continue;
                var mouse = new MouseDevice(endpoint); keep = true; return mouse;
            }
            catch (IOException) { }
            catch (TimeoutException) { }
            finally { if (!keep) endpoint.Dispose(); }
        }
        throw new IOException("Mouse unavailable. Move it to wake it, or check the receiver or cable.");
    }
    public MouseState Read()
    {
        var state = new MouseState();
        state.Model="PRO X SUPERLIGHT 2";state.VendorId=0x046D;state.ProductId=ep.Product;
        byte[] b = ep.Query(battery, 1), d = ep.Query(dpi, 5, 0);
        state.Battery = b[0] <= 100 ? b[0] : -1; state.BatteryStatus = b[2];
        state.Dpi = BE(d, 1); if (state.Dpi == 0) state.Dpi = BE(d, 3);
        state.DpiY = BE(d, 5); if (state.DpiY == 0) state.DpiY = BE(d, 7);
        state.Lod = d[9];
        state.Connection = ep.Device == 255 ? "USB cable" : "Wireless";
        state.Values = ReadValues();
        if(reportRate!=0) state.PollingIndex=ep.Query(reportRate,2)[0];
        byte[] description = ep.Query(onboard, 0);
        byte firmwareFeature=Feature(0x0003);
        if(firmwareFeature!=0)
        {
            byte[] info=ep.Query(firmwareFeature,0);
            state.DeviceKey=BitConverter.ToString(info.Skip(1).Take(4).ToArray());
            for(byte entity=0;entity<Math.Min((int)info[0],8);entity++)
            {
                byte[] version=ep.Query(firmwareFeature,1,entity);
                if((version[0]&15)==0)state.Firmware=DecodeFirmware(version);
            }
        }
        state.Profile = BE(ep.Query(onboard, 4), 0);
        state.Slot = ep.Query(onboard, 11)[0];
        if (ep.Query(onboard, 2)[0] != 1) state.SaveUnavailable = "Enable onboard memory in Onboard Memory Manager first.";
        else if (description[0] != 1 || description[1] != 7 || BE(description, 7) != 255)
            state.SaveUnavailable = "This onboard profile format is not supported yet.";
        else if (state.Profile < 1 || state.Profile >= description[6] || state.Slot > 4)
            state.SaveUnavailable = "The active profile cannot be edited yet.";
        else
        {
            state.ProfileBytes = ReadSector(state.Profile);
            state.ProfileName=DecodeName(state.ProfileBytes.Skip(160).Take(48).ToArray(),state.Profile);
            state.Profiles=ReadProfiles(description);
            try { ValidateProfile(state); }
            catch (InvalidDataException ex) { state.SaveUnavailable = ex.Message; }
        }
        if (state.BatteryStatus == 5 || state.BatteryStatus == 6 || state.Battery < 10)
            state.SaveUnavailable = "Charge the mouse before saving DPI.";
        return state;
    }
    internal static string DecodeFirmware(byte[] data)
    {
        // Logitech's human-readable firmware fields use hexadecimal digit strings.
        return (data[4]==0?"0":data[4].ToString("X2").TrimStart('0')) + "." + (data[5]==0?"0":data[5].ToString("X2").TrimStart('0')) + "." + (BE(data,6)==0?"0":BE(data,6).ToString("X4").TrimStart('0'));
    }
    static string DecodeName(byte[] data,int sector)
    {
        string text=Encoding.Unicode.GetString(data).Split('\0')[0].Trim('\uFFFF',' ');
        return string.IsNullOrWhiteSpace(text)?"Profile "+sector:text;
    }
    MouseProfile[] ReadProfiles(byte[] description)
    {
        byte[] directory=ReadSector(0);
        if(Crc(directory,253)!=BE(directory,253))throw new InvalidDataException("Profile directory checksum failed.");
        var result=new List<MouseProfile>();
        for(int entry=0;entry<Math.Min((int)description[3],16);entry++)
        {
            int offset=entry*4, sector=BE(directory,offset);
            if(sector==65535)break;
            if(sector<1 || sector>=description[6] || directory[offset+2]!=1)continue;
            var name=new List<byte>();
            for(int start=160;start<208;start+=16)name.AddRange(ep.Query(onboard,5,(byte)(sector>>8),(byte)sector,0,(byte)start));
            result.Add(new MouseProfile{Sector=sector,Name=DecodeName(name.ToArray(),sector)});
        }
        return result.ToArray();
    }
    public MouseState Activate(int sector, MouseState expected)
    {
        MouseState before=Read();
        if(string.IsNullOrEmpty(before.DeviceKey) || before.DeviceKey!=expected.DeviceKey || before.Profile!=expected.Profile)throw new IOException("The active mouse or profile changed. Refresh first.");
        if(!before.CanSave)throw new IOException(before.SaveUnavailable);
        if(!before.Profiles.Any(p=>p.Sector==sector))throw new IOException("That onboard profile is no longer enabled.");
        byte[] target=ReadSector(sector);
        if(Crc(target,253)!=BE(target,253) || target[2]>4)throw new InvalidDataException("Selected profile failed validation.");
        if(sector==before.Profile)return before;
        ep.Query(onboard,3,(byte)(sector>>8),(byte)sector);
        ep.Query(onboard,10,target[2]);
        var after=Read();
        if(after.Profile!=sector || after.Dpi!=LE(target,4+5*target[2]) || after.DpiY!=LE(target,6+5*target[2]))throw new IOException("Profile activation could not be verified. Refresh to check the mouse.");
        return after;
    }
    int[] ReadValues()
    {
        int[] x = ReadAxis(0), y = ReadAxis(1);
        int[] both = x.Intersect(y).OrderBy(v => v).ToArray();
        if (both.Length == 0) throw new InvalidDataException("The mouse returned no supported DPI values.");
        return both;
    }
    int[] ReadAxis(byte axis)
    {
        var raw = new List<byte>();
        for (byte page = 0; page < 32; page++)
        {
            byte[] reply = ep.Query(dpi, 2, 0, axis, page);
            raw.AddRange(reply.Skip(3));
            if (raw.Count >= 2 && raw[raw.Count - 1] == 0 && raw[raw.Count - 2] == 0) return DecodeValues(raw.ToArray());
        }
        throw new InvalidDataException("The DPI range response was incomplete.");
    }
    internal static int[] DecodeValues(byte[] data)
    {
        var values = new List<int>();
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            int n = BE(data, i); if (n == 0) return values.Distinct().OrderBy(v => v).ToArray();
            if ((n & 0xE000) == 0xE000)
            {
                int step = n & 0x1FFF;
                if (step == 0 || values.Count == 0 || i + 3 >= data.Length) throw new InvalidDataException("Invalid DPI range.");
                int end = BE(data, i + 2), start = values[values.Count - 1];
                if (end <= start) throw new InvalidDataException("Invalid DPI range bounds.");
                for (int v = start + step; v <= end; v += step) values.Add(v);
                i += 2;
            }
            else values.Add(n);
        }
        throw new InvalidDataException("Unterminated DPI range.");
    }
    byte[] ReadSector(int sector)
    {
        byte[] result = new byte[255];
        for (int position = 0; position < 255; position += 16)
        {
            int offset = Math.Min(position, 239);
            byte[] chunk = ep.Query(onboard, 5, (byte)(sector >> 8), (byte)sector, 0, (byte)offset);
            Array.Copy(chunk, 0, result, offset, 16);
        }
        return result;
    }
    internal static void ValidateProfile(MouseState state)
    {
        byte[] p = state.ProfileBytes;
        if (p == null || p.Length != 255 || Crc(p, 253) != BE(p, 253)) throw new InvalidDataException("Profile checksum did not match. Nothing was changed.");
        if (state.Slot < 0 || state.Slot > 4 || p[2] != state.Slot)
            throw new InvalidDataException("Select the default DPI stage in Onboard Memory Manager first.");
        int offset = 4 + 5 * state.Slot;
        if (LE(p, offset) != state.Dpi || LE(p, offset + 2) != state.DpiY || p[offset + 4] != state.Lod)
            throw new InvalidDataException("The active DPI and saved profile differ. Reopen the app after checking Onboard Memory Manager.");
    }
    internal static byte[] PatchProfile(MouseState state, int value)
    {
        ValidateProfile(state);
        if (!state.Values.Contains(value)) throw new ArgumentException("Choose a DPI value supported by the mouse.");
        var result = (byte[])state.ProfileBytes.Clone();
        int offset = 4 + 5 * state.Slot;
        result[offset] = result[offset + 2] = (byte)value;
        result[offset + 1] = result[offset + 3] = (byte)(value >> 8);
        int crc = Crc(result, 253); result[253] = (byte)(crc >> 8); result[254] = (byte)crc;
        return result;
    }
    public MouseState Save(int value, MouseState expected, string name = null, int? pollingIndex = null)
    {
        MouseState current = Read();
        if (!current.CanSave) throw new InvalidOperationException(current.SaveUnavailable);
        if (string.IsNullOrEmpty(current.DeviceKey) || current.DeviceKey != expected.DeviceKey || current.Profile != expected.Profile || current.Slot != expected.Slot || !current.ProfileBytes.SequenceEqual(expected.ProfileBytes))
            throw new InvalidOperationException("Mouse settings changed elsewhere. Refresh before saving.");
        bool renameOnly = value == current.Dpi && name != null && name != current.ProfileName;
        byte[] patched;
        if(renameOnly){ValidateProfile(current);patched=(byte[])current.ProfileBytes.Clone();}
        else patched = PatchProfile(current, value);
        if(pollingIndex.HasValue)
        {
            int index=pollingIndex.Value;
            if(reportRate==0 || index<0 || index>6)throw new ArgumentException("That polling rate is not supported.");
            byte[] caps=ep.Query(reportRate,1);
            if((caps[1]&(1<<index))==0)throw new ArgumentException("That polling rate is not supported by this mouse.");
            patched[1]=(byte)index;
            int rateCrc=Crc(patched,253);patched[253]=(byte)(rateCrc>>8);patched[254]=(byte)rateCrc;
        }
        if(name!=null && name!=current.ProfileName)
        {
            name=name.Trim();
            if(name.Length<1 || name.Length>23 || name.Any(char.IsControl))throw new ArgumentException("Profile names must have 1–23 characters and no control characters.");
            Array.Clear(patched,160,48);
            byte[] encoded=Encoding.Unicode.GetBytes(name);Array.Copy(encoded,0,patched,160,encoded.Length);
            int crc=Crc(patched,253);patched[253]=(byte)(crc>>8);patched[254]=(byte)crc;
        }
        if (patched.SequenceEqual(current.ProfileBytes)) return current;
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
        Directory.CreateDirectory(directory);
        string backup = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-profile-" + current.Profile + "-" + Guid.NewGuid().ToString("N") + ".bin");
        using (var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(current.ProfileBytes, 0, current.ProfileBytes.Length); file.Flush(true); }
        if (!ReadSector(current.Profile).SequenceEqual(current.ProfileBytes)) throw new IOException("Profile changed before saving. Refresh and try again.");
        try
        {
            ep.Query(onboard, 6, (byte)(current.Profile >> 8), (byte)current.Profile, 0, 0, 0, 255);
            for (int offset = 0; offset < 255; offset += 16)
            {
                byte[] chunk = new byte[16]; Array.Copy(patched, offset, chunk, 0, Math.Min(16, 255 - offset));
                ep.Query(onboard, 7, chunk);
            }
            ep.Query(onboard, 8);
            if (!ReadSector(current.Profile).SequenceEqual(patched)) throw new IOException("Saved profile did not match the requested DPI.");
            ep.Query(onboard, 3, (byte)(current.Profile >> 8), (byte)current.Profile);
            ep.Query(onboard, 10, (byte)current.Slot);
            MouseState verified = Read();
            if (verified.Dpi != value || verified.DpiY != (renameOnly ? current.DpiY : value) || (pollingIndex.HasValue && verified.PollingIndex!=pollingIndex.Value) || verified.ProfileBytes==null || !verified.ProfileBytes.SequenceEqual(patched))
                throw new IOException("The profile was written but active DPI verification failed.");
            return verified;
        }
        catch (Exception ex)
        {
            throw new IOException("Save could not be verified. Keep the mouse connected. Original profile backup: " + backup + ". " + ex.Message, ex);
        }
    }
    internal static int BE(byte[] b, int offset) { return (b[offset] << 8) | b[offset + 1]; }
    internal static int LE(byte[] b, int offset) { return b[offset] | (b[offset + 1] << 8); }
    internal static int Crc(byte[] bytes, int length)
    {
        int crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= bytes[i] << 8;
            for (int bit = 0; bit < 8; bit++) crc = ((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1) & 0xFFFF;
        }
        return crc;
    }
    public void Dispose() { ep.Dispose(); }
}
