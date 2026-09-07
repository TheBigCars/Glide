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

internal static class PreferenceStore
{
    static string FilePath { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Glide","appearance.txt"); } }
    internal static string[] Load()
    {
        try { return File.Exists(FilePath)?File.ReadAllLines(FilePath):new string[0]; } catch { return new string[0]; }
    }
    internal static void Save(string accent,string font)
    {
        try { string directory=System.IO.Path.GetDirectoryName(FilePath);Directory.CreateDirectory(directory);File.WriteAllLines(FilePath,new[]{accent??"Mint",font??"Segoe UI"}); } catch { }
    }
}

internal sealed partial class Controller
{
    readonly Window window;
    readonly TextBlock battery, charge, connection, profile, min, max, status, firmware, published, firmwareStatus, checkedText, readTime, deviceName, connectionType, firmwareDevice, updateProgress;
    readonly TextBox input, profileName;
    readonly Slider slider;
    readonly ComboBox profiles, polling, theme, font;
    readonly Button save, refresh, activate, rename, check, pollingSave, updateFirmware;
    readonly CheckBox animate;
    readonly FrameworkElement art, scene;
    readonly ScaleTransform scale;
    readonly TranslateTransform translation;
    readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    internal readonly TaskCompletionSource<bool> Ready = new TaskCompletionSource<bool>();
    MouseState current;
    FirmwareCheck currentFirmwareCheck;
    bool busy, writing, updating, closed, closeRequested, online, loadingPreferences;
    public bool IsBusy { get { return busy; } }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int length);

    public Controller(Window w)
    {
        window = w;
        battery=Find<TextBlock>("BatteryText"); charge=Find<TextBlock>("ChargeText");
        connection=Find<TextBlock>("ConnectionText"); profile=Find<TextBlock>("ProfileText");
        deviceName=Find<TextBlock>("DeviceNameText");connectionType=Find<TextBlock>("ConnectionTypeText");firmwareDevice=Find<TextBlock>("FirmwareDeviceText");updateProgress=Find<TextBlock>("UpdateProgressText");
        min=Find<TextBlock>("MinText"); max=Find<TextBlock>("MaxText"); status=Find<TextBlock>("StatusText");
        firmware=Find<TextBlock>("FirmwareText"); published=Find<TextBlock>("PublishedText");
        firmwareStatus=Find<TextBlock>("FirmwareStatus"); checkedText=Find<TextBlock>("CheckedText"); readTime=Find<TextBlock>("ReadTimeText");
        input=Find<TextBox>("DpiInput"); profileName=Find<TextBox>("ProfileNameInput");
        slider=Find<Slider>("DpiSlider"); profiles=Find<ComboBox>("ProfilePicker"); polling=Find<ComboBox>("PollingPicker"); theme=Find<ComboBox>("ThemePicker"); font=Find<ComboBox>("FontPicker");
        save=Find<Button>("SaveButton"); refresh=Find<Button>("RefreshButton"); activate=Find<Button>("ActivateButton");
        rename=Find<Button>("RenameButton"); check=Find<Button>("CheckUpdatesButton"); pollingSave=Find<Button>("PollingSaveButton");updateFirmware=Find<Button>("UpdateFirmwareButton");
        animate=Find<CheckBox>("AnimateToggle"); art=Find<FrameworkElement>("MouseArt"); scene=Find<FrameworkElement>("MouseScene");
        scale=Find<ScaleTransform>("MouseScale"); translation=Find<TranslateTransform>("MouseFloat");
        window.SourceInitialized += delegate { int dark=1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,20,ref dark,4); };
        window.Loaded += async delegate { await Refresh(); Ready.TrySetResult(true); };
        window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
        {
            closeRequested=true;
            StopAnimation();
            if(busy)
            {
                e.Cancel=true;
                if(writing) status.Text="Finishing the save, then closing completely…";
                else lifetime.Cancel();
            }
        };
        window.Closed += delegate { closed=true; lifetime.Cancel(); StopAnimation(); lifetime.Dispose(); };
        window.Deactivated += delegate { StopAnimation(); };
        window.StateChanged += delegate { if(window.WindowState==WindowState.Minimized)StopAnimation(); };
        animate.Checked += delegate { };
        animate.Unchecked += delegate { StopAnimation(); };
        scene.MouseEnter += delegate { if(MotionAllowed()){scale.BeginAnimation(ScaleTransform.ScaleXProperty,Motion(1.045,0.18));scale.BeginAnimation(ScaleTransform.ScaleYProperty,Motion(1.045,0.18));translation.BeginAnimation(TranslateTransform.YProperty,Motion(-10,0.18));} };
        scene.MouseLeave += delegate { scale.BeginAnimation(ScaleTransform.ScaleXProperty,Motion(1,0.18));scale.BeginAnimation(ScaleTransform.ScaleYProperty,Motion(1,0.18));translation.BeginAnimation(TranslateTransform.YProperty,Motion(0,0.18)); };
        refresh.Click += async delegate { await Refresh(); };
        save.Click += async delegate { await Save(); };
        rename.Click += async delegate { await Save(); };
        activate.Click += async delegate { await Activate(); };
        check.Click += async delegate { await CheckFirmware(); };
        updateFirmware.Click += async delegate { await InstallFirmware(); };
        pollingSave.Click += async delegate { await SavePolling(); };
        Find<Button>("SettingsButton").Click += delegate { var tabs=Find<TabControl>("SettingsTabs");tabs.SelectedIndex=tabs.SelectedIndex==2?0:2; };
        theme.SelectionChanged += delegate { ApplyTheme(); SavePreferences(); };
        font.SelectionChanged += delegate { ApplyFont(); SavePreferences(); };
        LoadPreferences();
        Find<Button>("OfficialUpdaterButton").Click += delegate
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FirmwareUpdates.UpdaterUrl){UseShellExecute=true}); }
            catch(Exception ex) { firmwareStatus.Text="Could not open Logitech's page: "+ex.Message; }
        };
        slider.ValueChanged += delegate
        {
            if(updating || current==null)return;
            int index=Math.Max(0,Math.Min(current.Values.Length-1,(int)Math.Round(slider.Value)));
            input.Text=current.Values[index].ToString();
        };
        input.TextChanged += delegate
        {
            if(updating || current==null)return;
            int value;
            if(int.TryParse(input.Text,out value))
            { updating=true; slider.Value=Nearest(current.Values,value); updating=false; }
            UpdateDraft();
        };
        profileName.TextChanged += delegate { if(!updating)UpdateDraft(); };
        profiles.SelectionChanged += delegate { if(!updating)UpdateDraft(); };
        polling.SelectionChanged += delegate { if(!updating)UpdateDraft(); };
        input.PreviewKeyDown += async delegate(object sender,KeyEventArgs e)
        { if(e.Key==Key.Enter && save.IsEnabled){e.Handled=true; await Save();} };
        window.PreviewKeyDown += delegate(object sender,KeyEventArgs e)
        { if(e.Key==Key.Escape && current!=null && !busy){Display(current,true);UpdateDraft();e.Handled=true;} };
    }
    T Find<T>(string name) where T:class { return window.FindName(name) as T; }
    bool MotionAllowed() { return !closed && !closeRequested && animate.IsChecked==true && window.IsActive && window.WindowState!=WindowState.Minimized && SystemParameters.ClientAreaAnimation; }
    static DoubleAnimation Motion(double value,double seconds)
    {
        var a=new DoubleAnimation(value,TimeSpan.FromSeconds(seconds)){EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        Timeline.SetDesiredFrameRate(a,30); return a;
    }
    void StartAnimation()
    {
        // Deliberately empty: the mouse hover is Glide's only animation.
    }
    void StopAnimation()
    {
        translation.BeginAnimation(TranslateTransform.YProperty,null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
    }
    internal static int Nearest(int[] values,int value)
    {
        int index=Array.BinarySearch(values,value);if(index>=0)return index;index=~index;
        if(index==0)return 0;if(index==values.Length)return values.Length-1;
        return (long)value-values[index-1]<=(long)values[index]-value?index-1:index;
    }
    bool HasDraft()
    { return current!=null && (input.Text!=current.Dpi.ToString() || profileName.Text.Trim()!=current.ProfileName); }
    void EnableControls()
    {
        bool available=!busy && online && current!=null;
        refresh.IsEnabled=!busy;
        check.IsEnabled=available && !string.IsNullOrEmpty(current.Firmware);
        updateFirmware.IsEnabled=available && currentFirmwareCheck!=null && currentFirmwareCheck.CanInstall;
        input.IsEnabled=slider.IsEnabled=profileName.IsEnabled=polling.IsEnabled=available && current.CanSave;
        profiles.IsEnabled=available && current.Profiles.Length>0;
        save.IsEnabled=rename.IsEnabled=activate.IsEnabled=pollingSave.IsEnabled=false;
    }
    void UpdateDraft()
    {
        EnableControls();
        if(busy || !online || current==null)return;
        if(!current.CanSave){status.Text=current.SaveUnavailable;return;}
        int value;
        if(!int.TryParse(input.Text,out value)){status.Text="Enter a whole number for DPI. Esc resets edits.";return;}
        if(Array.BinarySearch(current.Values,value)<0)
        {status.Text="Unsupported DPI. Nearest: "+current.Values[Nearest(current.Values,value)]+". Esc resets edits.";return;}
        string name=profileName.Text.Trim();
        if(name.Length<1 || name.Length>23 || name.Any(char.IsControl))
        {status.Text="Use a profile name of 1–23 characters.";return;}
        bool nameChanged=name!=current.ProfileName;
        bool changed=value!=current.Dpi || nameChanged;
        save.Content=nameChanged?"Save changes":"Save DPI";
        rename.Content=nameChanged && value!=current.Dpi?"Save changes":"Rename";
        save.IsEnabled=changed;rename.IsEnabled=nameChanged;
        var selected=profiles.SelectedItem as MouseProfile;
        activate.IsEnabled=!changed && selected!=null && selected.Sector!=current.Profile;
        pollingSave.IsEnabled=!changed && polling.SelectedIndex>=0 && polling.SelectedIndex!=current.PollingIndex;
        status.Text=changed?"Unsaved changes · Esc to reset": "Saved on mouse · no background app needed";
        if(selected!=null && selected.Sector!=current.Profile)
            status.Text=changed?"Save or reset your edits before switching profiles.":"Ready to use "+selected.Name+".";
    }
    void Display(MouseState next,bool replaceDraft)
    {
        string previousKey=current==null?null:current.DeviceKey;
        string previousFirmware=current==null?null:current.Firmware;
        current=next; online=true;
        battery.Text=next.Battery<0?"—":next.Battery+"%"; charge.Text=next.ChargeText;
        connection.Text=next.Connection;profile.Text="Active · "+next.Profile;
        deviceName.Text=next.Model;connectionType.Text=next.Connection=="Wireless"?"LIGHTSPEED":"USB";firmwareDevice.Text=next.Model;
        firmware.Text=string.IsNullOrEmpty(next.Firmware)?"Unavailable":next.Firmware;
        if(previousKey!=null && (previousKey!=next.DeviceKey || previousFirmware!=next.Firmware)){currentFirmwareCheck=null;published.Text="Not checked";firmwareStatus.Text="No automatic update checks.";checkedText.Text="";updateProgress.Text="Run a check to verify update availability for this exact device.";}
        updating=true;
        slider.Maximum=next.Values.Length-1;
        min.Text=next.Values.First().ToString("N0");max.Text=next.Values.Last().ToString("N0");
        if(replaceDraft){input.Text=next.Dpi.ToString();profileName.Text=next.ProfileName??"";}
        int draft;if(int.TryParse(input.Text,out draft))slider.Value=Nearest(next.Values,draft);
        profiles.ItemsSource=next.Profiles;
        profiles.SelectedItem=next.Profiles.FirstOrDefault(p=>p.Sector==next.Profile);
        polling.SelectedIndex=next.PollingIndex;
        updating=false;
        readTime.Text="Read "+DateTime.Now.ToString("h:mm tt")+" · manual refresh";
    }
    void Begin(bool writes,string text)
    {busy=true;writing=writes;EnableControls();status.Text=text;}
    void Finish()
    {
        busy=writing=false;
        if(closed)return;
        EnableControls();
        if(closeRequested)window.Close();
    }
}
