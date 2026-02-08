using System.Windows;
using System.Windows.Controls;

namespace AIConsumptionTracker.UI;

public partial class ProgressWindow : Window
{
    public int Progress
    {
        get => (int)ProgressBar.Value;
        set => ProgressBar.Value = value;
    }

    public ProgressWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }
}
