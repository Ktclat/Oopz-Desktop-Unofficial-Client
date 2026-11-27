using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Oopz.Native
{
    internal sealed class ConnectionDialog : Window
    {
        readonly TextBox port = new TextBox();
        readonly PasswordBox token = new PasswordBox();
        public int Port { get; private set; }
        public string Token { get { return token.Password; } }

        public ConnectionDialog(int previousPort)
        {
            Title = "连接本机 Oopz 适配器";
            Width = 400;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            var panel = new StackPanel { Margin = new Thickness(22) };
            Content = panel;
            panel.Children.Add(new TextBlock { Text = "连接已登录的 OneBot v11 服务（127.0.0.1）。", TextWrapping = TextWrapping.Wrap });
            var portLabel = new Label { Content = "端口", Target = port };
            panel.Children.Add(portLabel);
            port.MaxLength = 5;
            port.Text = previousPort.ToString(CultureInfo.InvariantCulture);
            AutomationProperties.SetAutomationId(port, "AdapterPort");
            AutomationProperties.SetLabeledBy(port, portLabel);
            panel.Children.Add(port);
            var tokenLabel = new Label { Content = "本机适配器令牌（如已设置）", Target = token };
            panel.Children.Add(tokenLabel);
            AutomationProperties.SetAutomationId(token, "AdapterToken");
            AutomationProperties.SetLabeledBy(token, tokenLabel);
            panel.Children.Add(token);
            var error = new TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed };
            panel.Children.Add(error);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(actions);
            actions.Children.Add(new Button { Content = "取消", IsCancel = true, Padding = new Thickness(12, 4, 12, 4) });
            var connect = new Button { Content = "连接", IsDefault = true, Margin = new Thickness(8,0,0,0), Padding = new Thickness(12,4,12,4) };
            AutomationProperties.SetAutomationId(connect, "ConfirmConnect");
            actions.Children.Add(connect);
            connect.Click += delegate {
                int value;
                if (!int.TryParse(port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1 || value > 65535)
                {
                    error.Text = "请输入 1 至 65535 的端口号。"; port.Focus(); return;
                }
                Port = value;
                DialogResult = true;
            };
            Loaded += delegate { port.Focus(); port.SelectAll(); };
        }
    }
}
