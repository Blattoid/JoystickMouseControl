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

using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Threading;

namespace JoystickMouseControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Dispatcher disp = Dispatcher.CurrentDispatcher; //magically allows thread-safe update to controls. I don't understand how or why it works.
        private readonly Thread background;

        public MainWindow()
        {
            InitializeComponent();
            background = new Thread(new ThreadStart(ThreadCode));
            background.Start();
        }

        private void ThreadCode()
        {
            Random random = new Random();
            while (true)
            {
                //We have to this terribleness to make UI changes.
                disp.Invoke(
                () =>
                {
                    test.Value = random.Next(1, 100);
                });
                
                Thread.Sleep(500);
            }
        }
    }
}
