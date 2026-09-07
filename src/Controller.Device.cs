using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

internal sealed partial class Controller
{
    public async Task Refresh()
    {
        if(busy || closed || closeRequested)return;
        bool preserve=HasDraft();
        Begin(false,"Reading mouse…");
        try
        {
            var result=await Task.Run(()=>{using(var mouse=MouseDevice.Connect(lifetime.Token))return mouse.Read();});
            if(closeRequested)return;
            // Discard drafts when the selected hardware/profile changed externally.
            bool same=current!=null && current.DeviceKey==result.DeviceKey && current.Profile==result.Profile;
            Display(result,!preserve || !same);busy=false;UpdateDraft();
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(closeRequested)return;
            online=false;battery.Text="—";charge.Text="Battery unavailable";connection.Text="Unavailable";deviceName.Text="No supported mouse";connectionType.Text="—";firmwareDevice.Text="Not detected";
            firmware.Text="Unavailable";status.Text=ex.GetBaseException().Message;
        }
        finally {Finish();}
        if(!closeRequested && online)UpdateDraft();
    }
    async Task Save()
    {
        int value;
        if(busy || !save.IsEnabled || current==null || !int.TryParse(input.Text,out value))return;
        MouseState expected=current;string name=profileName.Text.Trim();
        Begin(true,"Saving to mouse…");
        try
        {
            var result=await Task.Run(()=>{using(var mouse=MouseDevice.Connect())return mouse.Save(value,expected,name);});
            Display(result,true);status.Text="Saved and verified · "+result.Dpi+" DPI · "+result.ProfileName;
        }
        catch(Exception ex){status.Text="Save not verified. Refresh to check the mouse.";ShowError(ex);}
        finally{Finish();}
    }
    async Task Activate()
    {
        var selected=profiles.SelectedItem as MouseProfile;
        if(busy || !activate.IsEnabled || selected==null || HasDraft())return;
        MouseState expected=current;
        Begin(true,"Selecting "+selected.Name+"…");
        try
        {
            var result=await Task.Run(()=>{using(var mouse=MouseDevice.Connect())return mouse.Activate(selected.Sector,expected);});
            Display(result,true);status.Text="Using "+result.ProfileName+" · "+result.Dpi+" DPI";
        }
        catch(Exception ex){status.Text="Profile selection not verified. Refresh to check the mouse.";ShowError(ex);}
        finally{Finish();}
    }
    async Task SavePolling()
    {
        if(busy || !pollingSave.IsEnabled || current==null)return;
        int index=polling.SelectedIndex;MouseState expected=current;
        Begin(true,"Saving wireless polling rate…");
        try { var result=await Task.Run(()=>{using(var mouse=MouseDevice.Connect())return mouse.Save(expected.Dpi,expected,expected.ProfileName,index);});Display(result,true);status.Text="Polling rate saved and verified · "+((ComboBoxItem)polling.Items[index]).Content; }
        catch(Exception ex){status.Text="Polling-rate save was not verified.";ShowError(ex);} finally{Finish();}
    }
    void ApplyTheme()
    {
        string[] colors={"#AFDCCE","#75BFFF","#B79CFF","#F3B65C","#E58AAE","#B84A4A"};string[] soft={"#1B2925","#182630","#241F31","#2E2518","#2D2026","#2B1B1B"};int i=Math.Max(0,theme.SelectedIndex);i=Math.Min(i,colors.Length-1);
        var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));brush.Freeze();
        var softBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(soft[i]));softBrush.Freeze();
        window.Resources["AccentBrush"]=brush;window.Resources["AccentSoftBrush"]=softBrush;
        Find<Ellipse>("MouseLed").Fill=brush;
    }
    void ApplyFont()
    {
        string name=font.SelectedItem as string;if(!string.IsNullOrEmpty(name))window.FontFamily=new FontFamily(name);
    }
    string ThemeName()
    {
        var item=theme.SelectedItem as ComboBoxItem;return item==null?"Mint":item.Content.ToString();
    }
    void SavePreferences()
    {
        if(!loadingPreferences)PreferenceStore.Save(ThemeName(),font.SelectedItem as string);
    }
    void LoadPreferences()
    {
        loadingPreferences=true;
        string[] desired={"Segoe UI","Inter","Arial","Roboto","Open Sans","Verdana","Tahoma","Trebuchet MS","Georgia","Consolas","Bahnschrift","Calibri"};
        var installed=Fonts.SystemFontFamilies.Select(f=>f.Source).ToArray();
        foreach(string name in desired)if(installed.Any(x=>string.Equals(x,name,StringComparison.OrdinalIgnoreCase)))font.Items.Add(name);
        string[] saved=PreferenceStore.Load();string accent=saved.Length>0?saved[0]:"Mint",fontName=saved.Length>1?saved[1]:"Segoe UI";
        int accentIndex=0;for(int i=0;i<theme.Items.Count;i++){var item=theme.Items[i] as ComboBoxItem;if(item!=null && string.Equals(item.Content.ToString(),accent,StringComparison.OrdinalIgnoreCase)){accentIndex=i;break;}}
        theme.SelectedIndex=accentIndex;int fontIndex=font.Items.IndexOf(fontName);font.SelectedIndex=fontIndex>=0?fontIndex:Math.Max(0,font.Items.IndexOf("Segoe UI"));
        ApplyTheme();ApplyFont();loadingPreferences=false;
    }
    void ShowError(Exception ex)
    {
        online=false;
        if(!closeRequested)MessageBox.Show(window,ex.Message,"Mouse settings were not confirmed",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    public async Task CheckFirmware()
    {
        if(busy || !online || current==null || string.IsNullOrEmpty(current.Firmware))return;
        Begin(false,"Checking Logitech's release notes…");
        firmwareStatus.Text="Checking official release notes…";published.Text="Checking…";checkedText.Text="";
        try
        {
            var checkedDevice=await FirmwareSupportRegistry.Check(current,lifetime.Token);var result=checkedDevice.Latest;
            if(closeRequested)return;
            currentFirmwareCheck=checkedDevice;
            published.Text=result.Published;firmwareStatus.Text=result.Summary;
            updateProgress.Text=checkedDevice.InstallStatus;
            checkedText.Text="Checked "+result.CheckedAt.ToString("MMM d, yyyy · h:mm tt")+" · support.logi.com";
            status.Text="Update check complete. No device settings changed.";
        }
        catch(Exception ex)
        {
            if(closeRequested)return;
            currentFirmwareCheck=null;published.Text="Unknown";firmwareStatus.Text="Latest firmware could not be verified.";updateProgress.Text="Firmware update is unavailable until a verified check succeeds.";
            checkedText.Text=ex.GetBaseException().Message;status.Text="Update check unavailable; try again later.";
        }
        finally{Finish();}
    }
    async Task InstallFirmware()
    {
        if(busy || current==null || currentFirmwareCheck==null || !currentFirmwareCheck.CanInstall || currentFirmwareCheck.Support.Install==null)return;
        var support=currentFirmwareCheck.Support;
        if(!support.Matches(current)){updateProgress.Text="Connected device changed. Check again before updating.";return;}
        if(current.Battery<support.MinimumBattery){updateProgress.Text="Charge the mouse to at least "+support.MinimumBattery+"% before updating.";return;}
        Begin(true,"Updating firmware… Do not disconnect the mouse or receiver.");
        try
        {
            var progress=new Progress<string>(message=>updateProgress.Text=message);
            await support.Install(current,progress,lifetime.Token);
            updateProgress.Text="Firmware update completed successfully. Refreshing device…";status.Text="Firmware update completed.";
        }
        catch(Exception ex){updateProgress.Text="Firmware update failed: "+ex.GetBaseException().Message;status.Text="Firmware update failed. Keep the mouse connected.";}
        finally{Finish();}
    }
}
