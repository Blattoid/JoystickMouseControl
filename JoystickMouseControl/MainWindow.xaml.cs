using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using SharpDX;
using SharpDX.DirectInput;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace JoystickMouseControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly Dispatcher disp = Dispatcher.CurrentDispatcher; //Magically allows thread-safe update to controls.
        readonly Thread background;
        readonly DirectInput input;
        readonly List<DeviceInstance> devices;
        Joystick selected_dev;
        readonly int[] button_map = new int[] { -1, -1, -1, -1, -1, -1 }; //-1 means unbound, 0 and greater is the index into the button array of joystick state

        public MainWindow()
        {
            InitializeComponent();

            //Initialise device list and DirectInput object
            devices = new List<DeviceInstance>();
            input = new DirectInput();

            //Setup the control list
            string[] tooltips = new string[] { "LMB", "RMB", "MMB", "Alt-Tab", "Copy", "Paste" };
            control_list.ShowGridLines = false;
            for (int i = 0; i < tooltips.Length; i++)
            {
                RowDefinition row = new RowDefinition { Height = GridLength.Auto };
                control_list.RowDefinitions.Add(row);
            }
            for (int i = 0; i < 2; i++)
            {
                ColumnDefinition column = new ColumnDefinition { Width = GridLength.Auto };
                control_list.ColumnDefinitions.Add(new ColumnDefinition());
            }
            //Add the controls to each cell
            for (int i = 0; i < tooltips.Length; i++)
            {
                Label text = new Label { Content = tooltips[i] };
                Grid.SetRow(text, i);
                Grid.SetColumn(text, 0);
                control_list.Children.Add(text);

                ComboBox list = new ComboBox
                {
                    Tag = i, //This is used in the event handler to identify the list
                };
                list.SelectionChanged += Option_SelectionChanged;

                Grid.SetRow(list, i);
                Grid.SetColumn(list, 1);
                control_list.Children.Add(list);
            }

            //Load the custom button map and apply it if there is one
            var custom_map = Properties.Settings.Default.button_map;
            if (custom_map != null)
            {
                button_map = custom_map;
                should_reset_button_config = false;
            }

            //Start the thread responsible for all asynchronous operations (moving the mouse, updating UI)
            background = new Thread(new ThreadStart(ThreadCode));
            background.Start();
        }

        private void Option_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Read the selected entry and determine which list was updated.
            ComboBox list = (ComboBox)sender;
            int option_id = (int)list.Tag;
            int entry = list.SelectedIndex - 1; //Subtract 1 to account for the "None" option
            button_map[option_id] = entry; //Update the button map
            //Copy the button map to settings. This is saved on program exit
            Properties.Settings.Default.button_map = button_map;
        }

        int deviceselect_SelectedIndex = -1;
        double x = 0;
        double y = 0;
        bool alt_tab_prev = false;
        bool copy_prev = false;
        bool paste_prev = false;
        private void ThreadCode()
        {
            Random random = new Random();
            while (true)
            {
                //Speed limit to prevent insane CPU usages
                Thread.Sleep(2);

                if (selected_dev != null)
                {
                    //selected_dev.Poll(); //Request data from the joystick
                    //Attempt to read the current state
                    JoystickState state;
                    try
                    {
                        state = selected_dev.GetCurrentState(); //Now read its current state (what the condition of all the axes and buttons are)
                    }
                    catch (SharpDXException)
                    {
                        //In the event of a read fail, the device has been disconnected. 
                        //Set the selected device to null since it doesn't exist anymore
                        selected_dev = null;
                        RescanDevices(); //Remove the missing device from our lists
                        disp.Invoke(() =>
                        {
                            //Unselect whatever is selected
                            deviceselect.SelectedIndex = -1;
                        });
                        //Show an error
                        MessageBox.Show("Device was disconnected.");
                        continue;
                    }

                    //Now we have the state of the selected stick, we can use it to move the mouse.
                    //Values returned are 16 bit (0-65535). 

                    if (enable_keyboard)
                    {
                        //Process alt-tab, copy and paste buttons
                        bool alt_tab = button_map[3] < 0 ? false : state.Buttons[button_map[3]];
                        if (alt_tab == true && alt_tab != alt_tab_prev) System.Windows.Forms.SendKeys.SendWait("%{TAB}");
                        alt_tab_prev = alt_tab;
                        bool copy = button_map[4] < 0 ? false : state.Buttons[button_map[4]];
                        if (copy == true && copy != copy_prev) System.Windows.Forms.SendKeys.SendWait("^c");
                        copy_prev = copy;
                        bool paste = button_map[5] < 0 ? false : state.Buttons[button_map[5]];
                        if (paste == true && paste != paste_prev) System.Windows.Forms.SendKeys.SendWait("^v");
                        paste_prev = paste;
                    }

                    //Read the joystick buttons according to the button map, but only if the stored value is greater than -1
                    bool lmb = button_map[0] < 0 ? false : state.Buttons[button_map[0]];
                    bool rmb = button_map[1] < 0 ? false : state.Buttons[button_map[1]];
                    bool mmb = button_map[2] < 0 ? false : state.Buttons[button_map[2]];

                    //Update UI controls
                    disp.Invoke(() =>
                    {
                        if (!enable_mouse || enable_keyboard) //Some instructions once the joystick is selected
                            debug.Text = "Connected.\n\nSetup button controls in the left panel, then enable control when ready.";

                        horizontal_bar.Value = state.X;
                        vertical_bar.Value = state.Y;
                        lmb_indicator.Background = lmb ? new SolidColorBrush(Color.FromArgb(255, 6, 176, 37)) : SystemColors.ControlLightBrush;
                        rmb_indicator.Background = rmb ? new SolidColorBrush(Color.FromArgb(255, 6, 176, 37)) : SystemColors.ControlLightBrush;
                        mmb_indicator.Background = mmb ? new SolidColorBrush(Color.FromArgb(255, 6, 176, 37)) : SystemColors.ControlLightBrush;
                    });

                    //Don't proceed with moving the mouse if the "Enable mouse control" box is not checked
                    if (!enable_mouse) continue;

                    //Read raw x/y values (0 to 65535) and convert them to signed integers (-32768 to 32768)
                    double raw_x = state.X - (int)Math.Pow(2, 16) / 2;
                    double raw_y = state.Y - (int)Math.Pow(2, 16) / 2;

                    //If x or y somehow become NaN, reset them.
                    if (double.IsNaN(x)) x = 0;
                    if (double.IsNaN(x)) y = 0;

                    double sensitivity = nightmare_mode ? sensitivity_factor * 0.5 : sensitivity_factor; //In nightmare mode, decrease the sensitivity.
                    if (use_exponential_curve)
                    {
                        //Exponential curve instead of linear
                        const int JOY_MIN = -32768;
                        const int JOY_MAX = 32768;
                        double a = Math.Pow(Map(raw_x, JOY_MIN, JOY_MAX, -sensitivity, sensitivity), 2);
                        if (raw_x >= 0) x += a;
                        else x -= a;
                        a = Math.Pow(Map(raw_y, JOY_MIN, JOY_MAX, -sensitivity, sensitivity), 2);
                        if (raw_y >= 0) y += a;
                        else y -= a;
                    }
                    else
                    {
                        //Masochism linear curve.
                        x += raw_x / (10000 - sensitivity * 300);
                        y += raw_y / (10000 - sensitivity * 300);
                    }

                    //Debug, shows raw joystick data
                    disp.Invoke(() =>
                    {
                        debug.Text = string.Format("Raw values\nX:{0}\nY:{1}\n\nraw_x:{2}\nraw_y:{3}\n\nx:{4}\ny:{5}", state.X, state.Y, raw_x, raw_y, x, y);
                    });

                    //Get delta x and delta y from the whole numbers in x and y ... 
                    int dx = (int)x;
                    int dy = (int)y;
                    if (!nightmare_mode)
                    {
                        // ... but leave the remainders behind. The remainders accumulate to make movement smoother
                        x -= dx;
                        y -= dy;
                    }
                    else
                    {
                        //Nightmare mode, values can accumulate.
                        //Clamp the values so it doesn't get too insane.
                        dx = Math.Max(-3, Math.Min(dx, 3));
                        dy = Math.Max(-3, Math.Min(dy, 3));
                        x = Math.Max(-3, Math.Min(x, 3));
                        y = Math.Max(-3, Math.Min(y, 3));
                    }

                    //Finally, send this data to Windows.
                    SendMouseData(dx, dy, lmb, rmb, mmb);
                }
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            RescanDevices();
            deviceselect.IsDropDownOpen = true; //open the list automatically
        }
        void RescanDevices()
        {
            //clear the list of devices and repopulate when scanning, both for the array and the deviceselect list of names
            var input = new DirectInput();
            var dev_instances = input.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly);

            //Populate the array
            devices.Clear();
            devices.AddRange(dev_instances);

            //Add the names to the list of devices
            deviceselect.Items.Clear();
            foreach (DeviceInstance device in devices) { deviceselect.Items.Add(device.ProductName); }
        }

        bool should_reset_button_config = true; //Only set to false when the first device is selected, or if the constructor loads settings
        private void deviceselect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Update variables when choosing a device
            deviceselect_SelectedIndex = deviceselect.SelectedIndex;
            if (deviceselect_SelectedIndex != -1)
            {
                //Grab and initialise the selected joystick
                var instance = devices[deviceselect_SelectedIndex];
                if (instance.InstanceGuid == Guid.Empty)
                {
                    MessageBox.Show("ERROR: Device instance GUID is empty.");
                    deviceselect.SelectedIndex = -1;
                    return;
                }
                selected_dev = new Joystick(input, instance.InstanceGuid);
                selected_dev.Acquire();

                //Update the listboxes in the control list to reflect the capabilities of the selected stick
                foreach (UIElement thing in control_list.Children)
                {
                    if (thing.GetType().ToString().Contains("ComboBox"))
                    {
                        ComboBox list = (ComboBox)thing;
                        list.Items.Clear();
                        list.Items.Add("None");
                        if (should_reset_button_config) list.SelectedIndex = 0; //Automatically select none, but only on the first run.
                        else list.SelectedIndex = button_map[(int)list.Tag] + 1;
                        for (int i = 1; i <= selected_dev.Capabilities.ButtonCount; i++)
                        {
                            list.Items.Add(i.ToString());
                        }
                    }
                }
                should_reset_button_config = false;
            }
        }

        //Make the values of the options available to the thread
        bool enable_mouse = false;
        private void enable_mouse_Change(object sender, RoutedEventArgs e)
        {
            //I had never heard of a "bool?" until now.
            enable_mouse = enable_mouse_box.IsChecked.Value;
        }

        bool enable_keyboard = false;
        private void enable_keyboard_Change(object sender, RoutedEventArgs e)
        {
            enable_keyboard = enable_keyboard_box.IsChecked.Value;
        }

        bool use_exponential_curve = true;
        private void exponential_curve_Change(object sender, RoutedEventArgs e)
        {
            use_exponential_curve = exponential_curve_box.IsChecked.Value;
        }

        bool nightmare_mode = false;
        private void nightmare_mode_Change(object sender, RoutedEventArgs e)
        {
            nightmare_mode = nightmare_mode_box.IsChecked.Value;
        }

        double sensitivity_factor = 1.5;
        private void sensitivity_slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            sensitivity_factor = sensitivity_slider.Value;
        }

        //Saves the config then terminates the process
        private void Window_Closed(object sender, EventArgs e)
        {
            Properties.Settings.Default.Save();
            Environment.Exit(0);
        }

        //Port of the Arduino's map function because it's so darn useful
        double Map(double value, double fromSource, double toSource, double fromTarget, double toTarget)
        {
            return (value - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
        }

        //Mouse movement code
        //Adapted from:
        //https://social.msdn.microsoft.com/Forums/en-US/83650dd5-baf6-4028-a4af-6c91ef464412/is-there-a-way-to-programatically-hold-down-the-mouse-buttons?forum=csharpgeneral
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo); //Source used long instead of int which is bad https://stackoverflow.com/questions/9855438/a-call-to-a-pinvoke-function-has-unbalanced-the-stack
        private const int MOUSEEVENTF_MOVE = 0x01;

        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;

        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;

        private const int MOUSEEVENTF_MIDDLEDOWN = 0x20;
        private const int MOUSEEVENTF_MIDDLEUP = 0x40;

        private bool lmb_prev;
        private bool mmb_prev;
        private bool rmb_prev;
        public void SendMouseData(int X, int Y, bool lmb = false, bool rmb = false, bool mmb = false)
        {
            int FLAGS = MOUSEEVENTF_MOVE; //Set the relative movement flag, otherwise the mouse will not move.
            //Perform OR operations to all the flags depending on the requested inputs
            //But only if an input has changed since last time (prevents flooding api with excess flags)
            if (lmb != lmb_prev)
            {
                //The mismatch between lmb and lmb_prev indicates a change in state of lmb,
                //So we need to set the flag to say whether the button is being released or held.
                FLAGS |= lmb ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                lmb_prev = lmb; //Remember our action
            }
            if (rmb != rmb_prev)
            {
                FLAGS |= rmb ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                rmb_prev = rmb;
            }
            if (mmb != mmb_prev)
            {
                FLAGS |= mmb ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                mmb_prev = mmb;
            }

            mouse_event(FLAGS, X, Y, 0, 0);
        }
    }
}