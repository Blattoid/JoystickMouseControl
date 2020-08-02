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
        private void ThreadCode()
        {
            Random random = new Random();
            while (true)
            {
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
                        MessageBox.Show("Device was disconnected.");
                        continue;
                    }

                    //Now we have the state of the selected stick, we can use it to move the mouse.
                    //Values returned are 16 bit (0-65535). 

                    //Debug, just shows the thread is active
                    disp.Invoke(() =>
                    {
                        debug.Text = string.Format("Raw values\nX:{0}\nY:{1}", state.X, state.Y);
                        horizontal_bar.Value = state.X;
                        vertical_bar.Value = state.Y;
                    });

                    //Don't proceed with moving the mouse if the "Enable mouse control" box is not checked
                    if (!enable_movement) continue; //needs to be added

                    //convert from unsigned to signed, instead of 0-65535 it goes +/-32768
                    int x = state.X - (2 ^ 16 / 2);
                    int y = state.Y - (2 ^ 16 / 2);

                    //divide so this can be used for pixel adjustments to cursor
                    x /= 1000;
                    y /= 1000;

                    //TODO: mouse stuff
                }

                //Speed limit to prevent insane CPU usages
                Thread.Sleep(2);
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
                    MessageBox.Show("ERROR: Device instance GUID is empty.");
                    deviceselect.SelectedIndex = -1;
                    return;
                }
                selected_dev = new Joystick(input, instance.InstanceGuid);
                selected_dev.Acquire();
            }
        }

        //Make the values of the options available to the thread
        bool enable_movement = false;
        uint sensitivity = 1000;
        private void enable_movement_Checked(object sender, RoutedEventArgs e)
        {
            //I had never heard of a "bool?" until now.
            enable_movement = enable_movement_box.IsChecked.Value;
        }

        private void sensitivity_slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            sensitivity = (uint)sensitivity_slider.Value; //Decimal points are overrated anyway
        }

        //Guarantees the process terminates. Probably overkill.
        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }
    }
}