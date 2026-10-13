using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoundedTB
{
    /// <summary>
    /// Interaction logic for Infobox.xaml
    /// </summary>
    public partial class Infobox : Window
    {
        public Infobox()
        {
            InitializeComponent();
        }

        private void okButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
