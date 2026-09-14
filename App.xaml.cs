using System.Windows;

namespace ICloudReminderSticky;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "ICloudReminderSticky.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("iCloud 提醒便利贴已经在运行。\n可以在右下角托盘找到它。", "iCloud 提醒便利贴");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
