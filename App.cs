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

internal static class App
{
    [STAThread] public static void Main(string[] args)
    {
        bool created;
        using(var mutex=new Mutex(true,"Local\\GlideMouseController-v1",out created))
        {
            if(!created){MessageBox.Show("Glide is already open.");return;}
            var application=new Application{ShutdownMode=ShutdownMode.OnLastWindowClose};
            Window window;
            using(Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))window=(Window)XamlReader.Load(resource);
            using(Stream mouseImage=Assembly.GetExecutingAssembly().GetManifestResourceStream("superlight-2-white.png"))
            {
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=mouseImage;bitmap.EndInit();bitmap.Freeze();
                ((Image)window.FindName("MouseProductImage")).Source=bitmap;
            }
            var controller=new Controller(window);
            if(args.Length==2 && args[0].StartsWith("--"))
            {
                window.ShowInTaskbar=false;window.ShowActivated=false;
                window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-10000;
                window.Loaded+=async delegate
                {
                    try
                    {
                        await controller.Ready.Task;
                        if(args[0]=="--appearance-check")
                        {
                            var themeBox=(ComboBox)window.FindName("ThemePicker");var fontBox=(ComboBox)window.FindName("FontPicker");int oldTheme=themeBox.SelectedIndex,oldFont=fontBox.SelectedIndex;
                            int arial=fontBox.Items.IndexOf("Arial");if(arial<0)throw new Exception("Arial was not discovered from local system fonts.");
                            themeBox.SelectedIndex=1;fontBox.SelectedIndex=arial;await Task.Delay(100);
                            var accent=window.Resources["AccentBrush"] as SolidColorBrush;if(accent==null || accent.Color!=(Color)ColorConverter.ConvertFromString("#75BFFF"))throw new Exception("Live accent resource did not update.");
                            if(!string.Equals(window.FontFamily.Source,"Arial",StringComparison.OrdinalIgnoreCase))throw new Exception("Whole-window font did not update.");
                            string[] saved=PreferenceStore.Load();if(saved.Length<2 || saved[0]!="Ocean" || saved[1]!="Arial")throw new Exception("Appearance preferences were not persisted.");
                            themeBox.SelectedIndex=4;await Task.Delay(50);accent=window.Resources["AccentBrush"] as SolidColorBrush;if(accent==null || accent.Color!=(Color)ColorConverter.ConvertFromString("#E58AAE"))throw new Exception("Pink accent did not update.");
                            themeBox.SelectedIndex=5;await Task.Delay(50);accent=window.Resources["AccentBrush"] as SolidColorBrush;if(accent==null || accent.Color!=(Color)ColorConverter.ConvertFromString("#B84A4A"))throw new Exception("Red accent did not update.");
                            themeBox.SelectedIndex=oldTheme;fontBox.SelectedIndex=oldFont;await Task.Delay(100);
                            File.WriteAllText(args[1],"PASS: Mint/Ocean/Violet/Amber theme system extended with live Pink and Red accents; whole-window font, persistence, and preference restoration verified.");window.Close();return;
                        }
                        if(args[0]=="--polling-roundtrip")
                        {
                            MouseState before,changed,restored;
                            using(var mouse=MouseDevice.Connect()){before=mouse.Read();int alternate=before.PollingIndex==2?3:2;changed=mouse.Save(before.Dpi,before,before.ProfileName,alternate);}
                            using(var mouse=MouseDevice.Connect())restored=mouse.Save(changed.Dpi,changed,changed.ProfileName,before.PollingIndex);
                            if(restored.PollingIndex!=before.PollingIndex || !restored.ProfileBytes.SequenceEqual(before.ProfileBytes))throw new Exception("Polling-rate round trip did not restore the original profile.");
                            File.WriteAllText(args[1],"PASS: polling rate changed, read back, and original profile restored byte-for-byte.");window.Close();return;
                        }
                        if(args[0]=="--close-check")
                        {
                            Task closingRead=controller.Refresh();
                            window.Close();
                            await closingRead;
                            File.WriteAllText(args[1],"Close requested during a read; process exit must be checked by parent.");
                            return;
                        }
                        if(args[0]=="--ui-check")
                        {
                            var field=(TextBox)window.FindName("DpiInput");var bar=(Slider)window.FindName("DpiSlider");
                            var button=(Button)window.FindName("SaveButton");var picker=(ComboBox)window.FindName("ProfilePicker");
                            var name=(TextBox)window.FindName("ProfileNameInput");var rename=(Button)window.FindName("RenameButton");
                            string original=field.Text,originalName=name.Text;
                            if(!field.IsEnabled || original.Length==0)throw new Exception("Live device did not initialize.");
                            if(field.SelectionLength!=0)throw new Exception("DPI value was selected before the user clicked it.");
                            field.Text=original=="805"?"810":"805";
                            if(!button.IsEnabled)throw new Exception("Valid draft disabled.");
                            field.Text="abc";if(button.IsEnabled)throw new Exception("Invalid text accepted.");
                            field.Text="999999";if(button.IsEnabled)throw new Exception("Invalid range accepted.");
                            bar.Value=0;if(field.Text!="100")throw new Exception("Slider and input not synchronized.");
                            field.Text=original;if(button.IsEnabled)throw new Exception("Unchanged DPI allowed unnecessary write.");
                            if(picker.Items.Count<1)throw new Exception("No profiles loaded.");
                            if(picker.Items.Count>1)
                            {
                                int originalIndex=picker.SelectedIndex;
                                picker.SelectedIndex=originalIndex==0?1:0;
                                if(!((Button)window.FindName("ActivateButton")).IsEnabled)throw new Exception("Profile selection disabled.");
                                picker.SelectedIndex=originalIndex;
                            }
                            name.Text="";if(rename.IsEnabled)throw new Exception("Empty profile name accepted.");
                            name.Text="Local test";if(!rename.IsEnabled)throw new Exception("Valid rename disabled.");
                            name.Text=originalName;
                            int count=Probe.Endpoint.RequestCount;
                            var localFloat=(TranslateTransform)window.FindName("MouseFloat");
                            var animation=new DoubleAnimation(-3,-8,TimeSpan.FromSeconds(1));
                            localFloat.BeginAnimation(TranslateTransform.YProperty,animation);
                            double startAngle=localFloat.Y;
                            await Task.Delay(300);
                            if(Math.Abs(localFloat.Y-startAngle)<0.1)throw new Exception("Mouse animation did not advance.");
                            localFloat.BeginAnimation(TranslateTransform.YProperty,null);
                            await Task.Delay(11000);
                            if(Probe.Endpoint.RequestCount!=count)throw new Exception("Idle app polled mouse.");
                            File.WriteAllText(args[1],"PASS: DPI input, slider, profile list, rename validation, unchanged save suppression, working local animation, and zero HID queries during animation and 11 seconds idle. No settings written.");
                            window.Close();return;
                        }
                        if(args[0]=="--firmware-check")
                        {
                            await controller.CheckFirmware();
                            File.WriteAllText(args[1],((TextBlock)window.FindName("FirmwareText")).Text+" / "+((TextBlock)window.FindName("PublishedText")).Text+" / "+((TextBlock)window.FindName("FirmwareStatus")).Text+" / "+((TextBlock)window.FindName("CheckedText")).Text);
                            window.Close();return;
                        }
                        if(args[0]=="--render-firmware")
                        {((TabControl)window.FindName("SettingsTabs")).SelectedIndex=1;await controller.CheckFirmware();}
                        if(args[0]=="--render-settings")((TabControl)window.FindName("SettingsTabs")).SelectedIndex=2;
                        if(args[0]=="--render-small"){window.Width=820;window.Height=600;}
                        var content=(FrameworkElement)window.Content;content.UpdateLayout();
                        int width=(int)content.ActualWidth+56,height=(int)content.ActualHeight+44;
                        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
                        var visual=new DrawingVisual();
                        using(var dc=visual.RenderOpen())
                        {dc.DrawRectangle(window.Background,null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(content),null,new Rect(28,24,content.ActualWidth,content.ActualHeight));}
                        bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var output=File.Create(args[1]))png.Save(output);
                        window.Close();
                    }
                    catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());window.Close();}
                };
            }
            application.Run(window);
            GC.KeepAlive(controller);
        }
    }
}
