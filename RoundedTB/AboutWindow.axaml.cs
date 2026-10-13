using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace RoundedTB
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
#if !DEBUG
            bannerCan.IsVisible = false;
            bannerMst.IsVisible = true;
            subtitleBlock.Text = "RoundedTB";
#endif
        }

        private void okButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void configButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(App.Main.configPath) { UseShellExecute = true });
        }

        private void logButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(App.Main.logPath) { UseShellExecute = true });
        }
    }
}
