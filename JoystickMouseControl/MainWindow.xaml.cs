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

        public MainWindow()
        {
            InitializeComponent();

            //Initialise device list and DirectInput object
            devices = new List<DeviceInstance>();
            input = new DirectInput();

            //Setup the control list
            control_list.ShowGridLines = false;
            for (int i = 0; i < 5; i++)
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
            string[] tooltips = new string[] { "LMB", "RMB", "MMB", "Alt-Tab", "Shift-\nAlt-Tab" };
            for (int i = 0; i < 5; i++)
            {
                Label text = new Label { Content = tooltips[i] };
                Grid.SetRow(text, i);
                Grid.SetColumn(text, 0);
                control_list.Children.Add(text);

                ComboBox list = new ComboBox();
                Grid.SetRow(list, i);
                Grid.SetColumn(list, 1);
                control_list.Children.Add(list);
            }

            //Start the thread responsible for all asynchronous operations (moving the mouse, updating UI)
            background = new Thread(new ThreadStart(ThreadCode));
            background.Start();
        }

        private void List_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(e.OriginalSource.ToString());
        }

        int deviceselect_SelectedIndex = -1;
        double x = 0;
        double y = 0;
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
                    bool lmb = state.Buttons[0];
                    bool rmb = state.Buttons[1];
                    bool mmb = state.Buttons[2];

                    //Update UI controls
                    disp.Invoke(() =>
                    {
                        horizontal_bar.Value = state.X;
                        vertical_bar.Value = state.Y;
                        lmb_indicator.Background = lmb ? new SolidColorBrush(Color.FromArgb(255, 6, 176, 37)) : SystemColors.ControlLightBrush;
                        rmb_indicator.Background = rmb ? new SolidColorBrush(Color.FromArgb(255, 6, 176, 37)) : SystemColors.ControlLightBrush;
                        mmb_indicator.Background = mmb ? new SolidColorBrush(Color.FromArgb(255, 6, 176, 37)) : SystemColors.ControlLightBrush;
                    });

                    //Don't proceed with moving the mouse if the "Enable mouse control" box is not checked
                    if (!enable_movement) continue;

                    //Read raw x/y values (0 to 65535) and convert them to signed integers (-32768 to 32768)
                    double raw_x = state.X - (int)Math.Pow(2, 16) / 2;
                    double raw_y = state.Y - (int)Math.Pow(2, 16) / 2;

                    //Adjust for sensitivity. //TODO: Make this a logarithmic curve //TODO_2: Get good at mathematics
                    x += raw_x / (10000 - sensitivity);
                    y += raw_y / (10000 - sensitivity);

                    //Debug, shows raw joystick data
                    disp.Invoke(() =>
                    {
                        debug.Text = string.Format("Raw values\nX:{0}\nY:{1}\n\nraw_x:{2}\nraw_y:{3}\n\nx:{4}\ny:{5}", state.X, state.Y, raw_x, raw_y, x, y);
                    });

                    //Get delta x and delta y from the whole numbers in x and y but leave the remainders behind. The remainders accumulate to make movement smoother
                    int dx = (int)x;
                    x -= dx;
                    int dy = (int)y;
                    y -= dy;

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
                    DEBUG += "\n" + thing.GetType().ToString() + "\n";
                    if (thing.GetType().ToString().Contains("ComboBox"))
                    {
                        ComboBox list = (ComboBox)thing;
                        list.Items.Clear();
                        for (int i = 1; i <= selected_dev.Capabilities.ButtonCount; i++)
                        {
                            list.Items.Add(i.ToString());
                        }
                    }
                }
            }
        }

        //Make the values of the options available to the thread
        bool enable_movement = false;
        double sensitivity = 1000;
        private void enable_movement_Checked(object sender, RoutedEventArgs e)
        {
            //I had never heard of a "bool?" until now.
            enable_movement = enable_movement_box.IsChecked.Value;
        }
        private void sensitivity_slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            sensitivity = sensitivity_slider.Value;
        }

        //Guarantees the process terminates. Probably overkill.
        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
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