using System.Windows;
using CoreRipperX.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CoreRipperX.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
    }
}
