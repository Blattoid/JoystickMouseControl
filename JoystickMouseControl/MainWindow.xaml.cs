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
using System.Windows.Forms;

namespace JoystickMouseControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly Dispatcher disp = Dispatcher.CurrentDispatcher; //magically allows thread-safe update to controls.
        readonly Thread background;
        readonly DirectInput input;
        readonly List<DeviceInstance> devices;
        Joystick selected_dev;

        public MainWindow()
        {
            InitializeComponent();

            devices = new List<DeviceInstance>();
            input = new DirectInput();

            background = new Thread(new ThreadStart(ThreadCode));
            background.Start();
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
                        RescanDevices(); //remove it from the list
                        disp.Invoke(() =>
                        {
                            //unselect then rescan automatically
                            deviceselect.SelectedIndex = -1;
                        });
                        //show an error
                        System.Windows.Forms.MessageBox.Show("Device was disconnected.");
                        continue;
                    }

                    //Now we have the state of the selected stick, we can use it to move the mouse.
                    //Values returned are 16 bit (0-65535). 

                    bool lmb = state.Buttons[0];
                    bool mmb = state.Buttons[1];
                    bool rmb = state.Buttons[2];
                    //Update UI controls
                    /*disp.Invoke(() =>
                    {
                        //debug.Text = string.Format("Raw values\nX:{0}\nY:{1}", state.X, state.Y);
                        horizontal_bar.Value = state.X;
                        vertical_bar.Value = state.Y;
                        
                    });*/
                    //TODO: Make indicators change colour
                    //lmb_indicator.Background = lmb?Brush. //6,176,37

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

                    SendMouseData(dx, dy);
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

            devices.Clear();
            devices.AddRange(dev_instances);

            deviceselect.Items.Clear();
            foreach (DeviceInstance device in devices) { deviceselect.Items.Add(device.ProductName); }
        }

        private void deviceselect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //update variables when choosing a device
            deviceselect_SelectedIndex = deviceselect.SelectedIndex;
            if (deviceselect_SelectedIndex != -1)
            {
                var instance = devices[deviceselect_SelectedIndex];
                if (instance.InstanceGuid == Guid.Empty)
                {
                    System.Windows.Forms.MessageBox.Show("ERROR: Device instance GUID is empty.");
                    deviceselect.SelectedIndex = -1;
                    return;
                }
                selected_dev = new Joystick(input, instance.InstanceGuid);
                selected_dev.Acquire();
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

        bool lmb_prev;
        bool mmb_prev;
        bool rmb_prev;
        public void SendMouseData(int X, int Y, bool lmb = false, bool mmb = false, bool rmb = false)
        {
            //Perform OR operations to all the flags depending on the requested inputs
            //But only if an input has changed since last time (prevents flooding api with excess flags)
            int FLAGS = MOUSEEVENTF_MOVE;
            if (lmb != lmb_prev)
            {
                if (lmb) FLAGS |= MOUSEEVENTF_LEFTDOWN;
                else FLAGS |= MOUSEEVENTF_LEFTUP;
                lmb_prev = lmb;
            }
            if (mmb != mmb_prev)
            {
                if (mmb) FLAGS |= MOUSEEVENTF_MIDDLEDOWN;
                else FLAGS |= MOUSEEVENTF_MIDDLEUP;
                mmb_prev = mmb;
            }
            if (rmb != rmb_prev)
            {
                if (rmb) FLAGS |= MOUSEEVENTF_RIGHTDOWN;
                else FLAGS |= MOUSEEVENTF_RIGHTUP;
                rmb_prev = rmb;
            }

            mouse_event(FLAGS, X, Y, 0, 0);
        }
    }
}