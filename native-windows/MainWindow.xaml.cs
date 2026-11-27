using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Oopz.Native
{
    public partial class MainWindow : Window
    {
        const int LiveHistoryLimit = 250;
        AdapterSession adapter;
        CancellationTokenSource connection;
        readonly Dictionary<string, ChatChannel> liveChannels = new Dictionary<string, ChatChannel>();
        ChatChannel selected;
        string account;
        int port = 6700;
        bool online, sending, changingChannel, importing, closing;

        public MainWindow()
        {
            InitializeComponent();
            Closed += delegate { closing = true; EndConnection(); };
        }

        void EndConnection()
        {
            var old = connection;
            connection = null;
            online = false;
            sending = false;
            if (old != null) old.Cancel();
            if (adapter != null) adapter.Dispose();
            adapter = null;
            if (old != null) old.Dispose();
            liveChannels.Clear();
            ConnectButton.Content = "连接适配器…";
            UpdateComposer();
        }

        async void Connect_Click(object sender, RoutedEventArgs args)
        {
            if (connection != null)
            {
                EndConnection();
                ShowAreas(new List<ChatArea>());
                Status.Text = "已断开";
                return;
            }
            var dialog = new ConnectionDialog(port) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            port = dialog.Port;
            var current = new CancellationTokenSource();
            var cancel = current.Token;
            connection = current;
            Status.Text = "正在连接本机适配器…";
            ConnectButton.Content = "取消连接";
            try
            {
                var session = new AdapterSession(port, dialog.Token);
                adapter = session;
                var user = await session.GetAccountAsync(cancel);
                var channels = await session.GetChannelsAsync(cancel);
                if (connection != current) return;
                account = user;
                var areas = new List<ChatArea>();
                foreach (var group in channels.GroupBy(c => c.Area))
                {
                    var area = new ChatArea { Name = String.IsNullOrWhiteSpace(group.Key) ? "我的频道" : group.Key, Channels = new List<ChatChannel>() };
                    foreach (var channel in group)
                    {
                        if (liveChannels.ContainsKey(channel.Id)) continue;
                        var view = new ChatChannel { Id = channel.Id, Label = channel.Name };
                        liveChannels.Add(channel.Id, view);
                        area.Channels.Add(view);
                    }
                    if (area.Channels.Count != 0) areas.Add(area);
                }
                ShowAreas(areas);
                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var listening = session.ListenAsync(message => Dispatcher.BeginInvoke(new Action(delegate {
                    if (connection == current) AddMessage(message);
                })), cancel, () => started.TrySetResult(true));
                var first = await Task.WhenAny(started.Task, listening, Task.Delay(TimeSpan.FromSeconds(10), cancel));
                if (connection != current) return;
                if (first == listening) { await listening; throw new InvalidOperationException("适配器的实时事件连接已关闭。"); }
                if (first != started.Task) throw new TimeoutException("连接实时事件超时。请检查适配器是否启用了正向 WebSocket。");
                ObserveEvents(listening, current);
                var recent = await session.GetRecentAsync(cancel);
                if (connection != current) return;
                foreach (var message in recent) AddMessage(message);
                online = true;
                ConnectButton.Content = "断开连接";
                Status.Text = "已连接 · " + account + (areas.Count == 0 ? " · 此账号没有可用频道" : "");
                UpdateComposer();
                UpdateEmpty();
            }
            catch (Exception ex)
            {
                if (connection != current) return;
                EndConnection();
                Status.Text = "连接失败：" + ex.Message;
            }
        }

        async void ObserveEvents(Task listening, CancellationTokenSource current)
        {
            string reason = "事件连接已关闭。";
            try { await listening; }
            catch (Exception ex) { reason = ex.Message; }
            if (connection != current || closing) return;
            EndConnection();
            Status.Text = "已断开，当前显示已接收的消息。重新连接可继续收发。" + reason;
        }

        void AddMessage(LiveMessage message)
        {
            ChatChannel channel;
            if (!liveChannels.TryGetValue(message.ChannelId, out channel)) return;
            channel.Append(new ChatLine { Id = message.Id, Author = message.Author, Text = message.Text, Time = message.Timestamp }, LiveHistoryLimit);
            if (selected == channel) UpdateEmpty();
        }

        async void Import_Click(object sender, RoutedEventArgs args)
        {
            if (importing) return;
            var dialog = new OpenFileDialog { Title = "打开本机 Oopz 网页记录", Filter = "网页网络记录 (*.har)|*.har", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            if (connection != null && MessageBox.Show(this, "打开记录会断开本机适配器。继续吗？", "打开网页记录",
                MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            importing = true;
            ConnectButton.IsEnabled = false;
            try
            {
                var snapshot = await Task.Run(() => OopzCapture.Load(dialog.FileName));
                if (closing) return;
                var areas = new List<ChatArea>();
                foreach (var a in snapshot.Areas)
                {
                    var area = new ChatArea { Name = a.Name, Channels = new List<ChatChannel>() };
                    foreach (var c in a.Channels)
                    {
                        bool voice = c.Type == "VOICE" || c.Type == "AUDIO";
                        var channel = new ChatChannel { Id = c.Id, Label = c.Name, IsVoice = voice };
                        foreach (var m in c.Messages)
                            channel.Lines.Add(new ChatLine { Id = m.Id, Author = m.Author, Text = m.Text, Time = m.Timestamp });
                        area.Channels.Add(channel);
                    }
                    areas.Add(area);
                }
                EndConnection();
                ShowAreas(areas);
                Status.Text = "本机网页记录 · 只读 · " + areas.Count + " 个域";
            }
            catch (Exception ex) { if (!closing) MessageBox.Show(this, ex.Message, "无法打开网页记录", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { importing = false; if (!closing) ConnectButton.IsEnabled = true; }
        }

        void ShowAreas(List<ChatArea> areas)
        {
            Areas.ItemsSource = areas;
            Areas.SelectedIndex = areas.Count == 0 ? -1 : 0;
            if (areas.Count == 0) ShowChannels(null);
        }
        void Area_Changed(object sender, SelectionChangedEventArgs args) { ShowChannels(Areas.SelectedItem as ChatArea); }
        void ShowChannels(ChatArea area)
        {
            Channels.ItemsSource = area == null ? null : area.Channels;
            if (area != null && area.Channels.Count != 0) Channels.SelectedIndex = 0;
            else SelectChannel(null);
        }
        void Channel_Changed(object sender, SelectionChangedEventArgs args) { SelectChannel(Channels.SelectedItem as ChatChannel); }
        void SelectChannel(ChatChannel channel)
        {
            if (selected != null) selected.Draft = Composer.Text;
            selected = channel;
            Timeline.ItemsSource = channel == null ? null : channel.Lines;
            changingChannel = true;
            Composer.Text = channel == null ? "" : channel.Draft ?? "";
            changingChannel = false;
            RoomTitle.Text = channel == null ? "消息" : channel.Label;
            if (channel != null && channel.Lines.Count > 0) Timeline.ScrollIntoView(channel.Lines[channel.Lines.Count-1]);
            UpdateEmpty();
            UpdateComposer();
        }
        void UpdateEmpty()
        {
            Empty.Visibility = selected != null && selected.Lines.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            Empty.Text = selected == null ? (Areas.Items.Count > 0 ? "这个域没有可显示的频道。" : "连接本机适配器以收发频道消息，或打开自己保存的 Oopz 网页记录。")
                : selected.IsVoice ? "语音频道 · 当前版本未接入通话。" : "没有可显示的消息。";
        }
        void UpdateComposer()
        {
            if (ComposeBar == null) return;
            ComposeBar.Visibility = online && selected != null && !selected.IsVoice ? Visibility.Visible : Visibility.Collapsed;
            SendMessage.IsEnabled = online && !sending && !String.IsNullOrWhiteSpace(Composer.Text);
        }
        void Composer_Changed(object sender, TextChangedEventArgs args) { if (!changingChannel) UpdateComposer(); }
        void Composer_KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && SendMessage.IsEnabled)
            { args.Handled = true; Send_Click(sender, args); }
        }
        async void Send_Click(object sender, RoutedEventArgs args)
        {
            if (!online || sending || selected == null) return;
            var channel = selected;
            var text = Composer.Text;
            if (String.IsNullOrWhiteSpace(text)) return;
            var current = connection;
            sending = true;
            UpdateComposer();
            try
            {
                var id = await adapter.SendAsync(channel.Id, text, current.Token);
                if (connection != current) return;
                channel.Append(new ChatLine { Id = id, Author = account, Text = text, Time = DateTimeOffset.Now }, LiveHistoryLimit);
                if (selected == channel)
                {
                    if (Composer.Text == text) Composer.Clear();
                    UpdateEmpty();
                    if (channel.Lines.Count > 0) Timeline.ScrollIntoView(channel.Lines[channel.Lines.Count - 1]);
                }
                else if (channel.Draft == text) channel.Draft = "";
                Status.Text = "已连接 · " + account;
            }
            catch (Exception ex)
            {
                if (connection == current) Status.Text = "未收到发送确认，请先核对频道消息。 " + ex.Message;
            }
            finally { if (connection == current) { sending = false; UpdateComposer(); } }
        }
        void Mic_Click(object sender, RoutedEventArgs args) { new MicrophoneWindow { Owner = this }.ShowDialog(); }
    }
}
