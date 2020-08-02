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

namespace JoystickMouseControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly Dispatcher disp = Dispatcher.CurrentDispatcher; //magically allows thread-safe update to controls. I don't understand how or why it works.
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
        private void ThreadCode()
        {
            Random random = new Random();
            while (true)
            {
                if (selected_dev != null)
                {
                    selected_dev.Poll(); //request data from the joystick
                    //attempt to read the current state
                    JoystickState state;
                    try
                    {
                        state = selected_dev.GetCurrentState(); //now read its current state (what the condition of all the axes and buttons are)
                    }
                    catch (SharpDXException)
                    {
                        //in the event of a read fail, the device has been disconnected. 
                        //set it to null since it doesn't exist anymore
                        selected_dev = null;
                        RescanDevices(); //remove it from the list
                        disp.Invoke(() =>
                        {
                            //unselect then rescan automatically
                            deviceselect.SelectedIndex = -1;
                        });
                        //show an error
                        MessageBox.Show("Device was disconnected.");
                        continue;
                    }

                    //Debug, just shows the thread is active
                    disp.Invoke(() =>
                    {
                        test.Value = random.Next(1, 50);
                    });
                }
                else
                {
                    disp.Invoke(() =>
                    {
                        test.Value = random.Next(50, 100);
                    });
                }

                Thread.Sleep(500);
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            RescanDevices();
            deviceselect.IsDropDownOpen = true; //open the list automatically
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
                    MessageBox.Show("ERROR: Device instance GUID is empty.");
                    deviceselect.SelectedIndex = -1;
                    return;
                }
                selected_dev = new Joystick(input, instance.InstanceGuid);
                selected_dev.Acquire();
            }
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

        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }
    }
}