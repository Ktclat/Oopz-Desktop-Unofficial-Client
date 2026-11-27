using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Oopz.Native
{
    internal sealed class MicrophoneWindow : Window
    {
        private readonly ComboBox devices = new ComboBox { MinWidth = 200 };
        private readonly Button refresh = new Button { Content = "刷新设备" };
        private readonly Button test = new Button { Content = "开始测试" };
        private readonly ProgressBar level = new ProgressBar { Minimum = -60, Maximum = 0, Value = -60, Height = 12 };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        private readonly Stopwatch elapsed = new Stopwatch();
        private WaveCapture capture;
        private int highest;
        private bool received;

        public MicrophoneWindow()
        {
            Title = "麦克风自检";
            Width = 450;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new Label { Content = "输入设备", Target = devices, Padding = new Thickness(0, 0, 0, 5) });
            var row = new DockPanel();
            refresh.Margin = new Thickness(8, 0, 0, 0);
            refresh.Padding = new Thickness(8, 4, 8, 4);
            DockPanel.SetDock(refresh, Dock.Right);
            row.Children.Add(refresh);
            row.Children.Add(devices);
            panel.Children.Add(row);
            level.Margin = new Thickness(0, 18, 0, 8);
            AutomationProperties.SetName(level, "输入电平（dBFS）");
            panel.Children.Add(level);
            panel.Children.Add(status);
            test.Margin = new Thickness(0, 16, 0, 0);
            test.Padding = new Thickness(12, 5, 12, 5);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(test);
            Content = panel;
            refresh.Click += delegate { RefreshDevices(); };
            test.Click += delegate { if (capture == null) Start(); else Finish(); };
            timer.Tick += delegate { UpdateLevel(); };
            Closed += delegate { StopCapture(); };
            RefreshDevices();
        }

        private void RefreshDevices()
        {
            var selected = devices.SelectedItem as InputDevice;
            var inputs = WaveCapture.Inputs();
            devices.ItemsSource = inputs;
            int index = selected == null ? -1 : inputs.FindIndex(d => d.Id == selected.Id && d.Name == selected.Name);
            devices.SelectedIndex = index >= 0 ? index : (inputs.Count == 0 ? -1 : 0);
            test.IsEnabled = inputs.Count > 0;
            status.Text = inputs.Count == 0 ? "没有可用的输入设备。连接麦克风后点击「刷新设备」。"
                : "点击「开始测试」后说话。10 秒后自动停止，声音只在本机用于计算电平。";
        }

        private void Start()
        {
            var input = devices.SelectedItem as InputDevice;
            if (input == null) return;
            var matching = WaveCapture.Inputs().Exists(d => d.Id == input.Id && d.Name == input.Name);
            if (!matching)
            {
                RefreshDevices();
                status.Text = "输入设备已变更，请重新选择后开始测试。";
                return;
            }
            try { capture = new WaveCapture(input.Id); }
            catch (InvalidOperationException ex)
            {
                status.Text = "无法使用麦克风。请检查设备连接和 Windows 的桌面应用麦克风权限。" + ex.Message;
                return;
            }
            highest = 0;
            received = false;
            devices.IsEnabled = refresh.IsEnabled = false;
            test.Content = "停止测试";
            status.Text = "正在监听输入，剩余 10 秒";
            elapsed.Restart();
            timer.Start();
        }

        private static double Decibels(int peak)
        {
            return peak == 0 ? -60 : Math.Max(-60, 20 * Math.Log10(peak / 32768.0));
        }

        private void UpdateLevel()
        {
            if (capture == null) return;
            try
            {
                int peak = capture.ReadPeak();
                if (peak >= 0)
                {
                    received = true;
                    highest = Math.Max(highest, peak);
                    level.Value = Decibels(peak);
                }
                int left = 10 - (int)elapsed.Elapsed.TotalSeconds;
                if (left <= 0) { Finish(); return; }
                status.Text = String.Format("正在监听输入，剩余 {0} 秒", left);
            }
            catch (InvalidOperationException ex)
            {
                StopCapture();
                status.Text = "读取中断。请检查麦克风并刷新设备。" + ex.Message;
            }
        }

        private void Finish()
        {
            StopCapture();
            status.Text = !received ? "未收到音频数据。请检查设备连接或输入权限。"
                : highest == 0 ? "本次输入为静音。请检查设备的静音开关和输入音量。"
                : String.Format("测试结束，最高输入电平 {0:0.0} dBFS。", 20 * Math.Log10(highest / 32768.0));
        }

        private void StopCapture()
        {
            timer.Stop();
            if (capture != null) { capture.Dispose(); capture = null; }
            level.Value = level.Minimum;
            devices.IsEnabled = refresh.IsEnabled = true;
            test.Content = "开始测试";
        }

        private sealed class InputDevice
        {
            public uint Id;
            public string Name;
            public override string ToString() { return Name; }
        }

        // WinMM returns completed buffers to the caller. No native callbacks run on WPF's thread.
        private sealed class WaveCapture : IDisposable
        {
            private IntPtr handle;
            private readonly List<Buffer> buffers = new List<Buffer>();
            private readonly short[] samples;
            private readonly object gate = new object();
            private System.Threading.Timer cutoff;
            private bool stopping;
            private int retryDelay = 100;
            private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(WaveHeader));

            public static List<InputDevice> Inputs()
            {
                var result = new List<InputDevice>();
                uint count = waveInGetNumDevs();
                if (count > 0) result.Add(new InputDevice { Id = UInt32.MaxValue, Name = "系统默认输入设备" });
                for (uint id = 0; id < count; id++)
                {
                    DeviceCaps caps;
                    if (waveInGetDevCapsW(new UIntPtr(id), out caps, (uint)Marshal.SizeOf(typeof(DeviceCaps))) == 0)
                        result.Add(new InputDevice { Id = id, Name = caps.Name });
                }
                return result;
            }

            public WaveCapture(uint device)
            {
                var format = new WaveFormat { Tag = 1, Channels = 1, Align = 2, Bits = 16 };
                uint error = 32; // WAVERR_BADFORMAT
                foreach (uint rate in new uint[] { 48000, 44100, 16000, 8000 })
                {
                    format.Rate = rate;
                    format.BytesPerSecond = rate * 2;
                    error = waveInOpen(out handle, device, ref format, IntPtr.Zero, IntPtr.Zero, 0);
                    if (error != 32) break;
                }
                Check(error);
                try
                {
                    samples = new short[format.Rate / 20]; // 50 ms of mono, signed 16-bit PCM.
                    for (int i = 0; i < 4; i++)
                    {
                        var buffer = new Buffer();
                        buffers.Add(buffer);
                        buffer.Data = Marshal.AllocHGlobal(samples.Length * 2);
                        buffer.Header = Marshal.AllocHGlobal((int)HeaderSize);
                        var header = new WaveHeader { Data = buffer.Data, Length = (uint)(samples.Length * 2) };
                        Marshal.StructureToPtr(header, buffer.Header, false);
                        Check(waveInPrepareHeader(handle, buffer.Header, HeaderSize));
                        buffer.Prepared = true;
                        Check(waveInAddBuffer(handle, buffer.Header, HeaderSize));
                    }
                    Check(waveInStart(handle));
                    // Release the device even when the UI thread cannot dispatch its next tick.
                    cutoff = new System.Threading.Timer(_ => Dispose(), null, 10000, System.Threading.Timeout.Infinite);
                }
                catch { Dispose(); throw; }
            }

            public int ReadPeak()
            {
                lock (gate)
                {
                    int peak = -1;
                    if (handle == IntPtr.Zero || stopping) return peak;
                    foreach (var buffer in buffers)
                    {
                        var header = (WaveHeader)Marshal.PtrToStructure(buffer.Header, typeof(WaveHeader));
                        if ((header.Flags & 1) == 0) continue; // WHDR_DONE
                        int count = (int)Math.Min(header.BytesRecorded, header.Length) / 2;
                        Marshal.Copy(buffer.Data, samples, 0, count);
                        for (int i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs((int)samples[i]));
                        Check(waveInAddBuffer(handle, buffer.Header, HeaderSize));
                    }
                    return peak;
                }
            }

            public void Dispose()
            {
                lock (gate)
                {
                    if (handle == IntPtr.Zero) return;
                    stopping = true;
                    waveInReset(handle); // Returns all queued buffers to the application.
                    foreach (var buffer in buffers)
                    {
                        if (!buffer.Prepared) continue;
                        if (waveInUnprepareHeader(handle, buffer.Header, HeaderSize) == 33)
                        {
                            // Some drivers finish reset asynchronously. Retain their buffers until returned.
                            if (cutoff == null)
                                cutoff = new System.Threading.Timer(_ => Dispose(), null, retryDelay, System.Threading.Timeout.Infinite);
                            else
                                cutoff.Change(retryDelay, System.Threading.Timeout.Infinite);
                            retryDelay = Math.Min(retryDelay * 2, 5000);
                            return;
                        }
                        buffer.Prepared = false;
                    }
                    waveInClose(handle);
                    if (cutoff != null) cutoff.Dispose();
                    foreach (var buffer in buffers)
                    {
                        if (buffer.Header != IntPtr.Zero) Marshal.FreeHGlobal(buffer.Header);
                        if (buffer.Data != IntPtr.Zero) Marshal.FreeHGlobal(buffer.Data);
                    }
                    handle = IntPtr.Zero;
                }
            }

            private static void Check(uint error)
            {
                if (error == 0) return;
                var text = new StringBuilder(256);
                waveInGetErrorTextW(error, text, (uint)text.Capacity);
                throw new InvalidOperationException(text.Length > 0 ? text.ToString() : "Windows 音频错误 " + error);
            }

            private sealed class Buffer { public IntPtr Header, Data; public bool Prepared; }
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct DeviceCaps
            {
                public ushort Manufacturer, Product;
                public uint Version;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
                public uint Formats;
                public ushort Channels, Reserved;
            }
            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            private struct WaveFormat
            {
                public ushort Tag, Channels;
                public uint Rate, BytesPerSecond;
                public ushort Align, Bits, ExtraSize;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct WaveHeader
            {
                public IntPtr Data;
                public uint Length, BytesRecorded;
                public IntPtr User;
                public uint Flags, Loops;
                public IntPtr Next, Reserved;
            }
            [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
            [DllImport("winmm.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
            private static extern uint waveInGetDevCapsW(UIntPtr device, out DeviceCaps caps, uint size);
            [DllImport("winmm.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
            private static extern uint waveInGetErrorTextW(uint error, StringBuilder text, uint size);
            [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
            [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
            [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
            [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
            [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
            [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
            [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);
        }
    }
}
