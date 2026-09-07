using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal enum FirmwareProtocol { Unavailable, LogitechHidPlusPlusDfu }

internal sealed class FirmwareDeviceSupport
{
    public string Model;
    public ushort VendorId;
    public ushort[] ProductIds;
    public int MinimumBattery;
    public FirmwareProtocol Protocol;
    public string UnavailableReason;
    public Func<string,CancellationToken,Task<FirmwareResult>> LatestFirmware;
    public Func<MouseState,IProgress<string>,CancellationToken,Task> Install;
    public bool Matches(MouseState state) { return state!=null && state.VendorId==VendorId && ProductIds.Contains(state.ProductId) && string.Equals(state.Model,Model,StringComparison.OrdinalIgnoreCase); }
}

internal sealed class FirmwareCheck
{
    public FirmwareDeviceSupport Support;
    public FirmwareResult Latest;
    public bool UpdateAvailable;
    public bool CanInstall;
    public string InstallStatus;
}

internal static class FirmwareSupportRegistry
{
    static readonly FirmwareDeviceSupport[] Devices={
        new FirmwareDeviceSupport{
            Model="PRO X SUPERLIGHT 2",VendorId=0x046D,ProductIds=new ushort[]{0xC54D,0xC09B},MinimumBattery=30,
            Protocol=FirmwareProtocol.Unavailable,
            UnavailableReason="Direct flashing is unavailable: no authenticated standalone package source and verified Superlight 2 flashing protocol are implemented.",
            LatestFirmware=(installed,token)=>FirmwareUpdates.Check(installed,token),Install=null
        }
    };
    internal static FirmwareDeviceSupport Find(MouseState state) { return Devices.FirstOrDefault(d=>d.Matches(state)); }
    internal static async Task<FirmwareCheck> Check(MouseState state,CancellationToken token)
    {
        var support=Find(state);if(support==null)throw new InvalidOperationException("This connected mouse is not in Glide's firmware support registry.");
        var latest=await support.LatestFirmware(state.Firmware,token);Version current,published;
        bool available=Version.TryParse(state.Firmware,out current)&&Version.TryParse(latest.Published,out published)&&current<published;
        bool protocol=support.Protocol!=FirmwareProtocol.Unavailable&&support.Install!=null;
        bool battery=state.Battery>=support.MinimumBattery;
        string reason=!available?"No newer published firmware is available.":!battery?"Charge the mouse to at least "+support.MinimumBattery+"% before updating.":!protocol?support.UnavailableReason:"Ready. Do not disconnect the mouse or receiver during the update.";
        return new FirmwareCheck{Support=support,Latest=latest,UpdateAvailable=available,CanInstall=available&&battery&&protocol,InstallStatus=reason};
    }
}
