using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Timers;
using Timer = System.Timers.Timer;

namespace FirewallControlApp
{
    public partial class MainWindow : Window
    {
        // PowerShell脚本内容
        private readonly string scriptEnableFirewall = @"
# 设置防火墙默认出站策略为“阻止”
Set-NetFirewallProfile -Profile Domain,Public,Private -DefaultOutboundAction Block

$ip = '121.48.165.91' 

# 创建允许访问matu的出站规则
New-NetFirewallRule -DisplayName 'Allow matu $ip' -Direction Outbound -Action Allow -RemoteAddress $ip -Protocol Any
";

        private readonly string scriptDisableFirewall = @"
# 设置防火墙默认出站策略为“允许”
Set-NetFirewallProfile -Profile Domain,Public,Private -DefaultOutboundAction Allow

# 移除特定的防火墙规则
Get-NetFirewallRule -DisplayName 'Allow matu*' | Remove-NetFirewallRule
";

        private Timer monitorTimer;
        private readonly string[] hostsToCheck = { "baidu.com", "aliyun.com" };
        private readonly string serverIp = "120.24.176.24";
        private readonly int serverPort = 8080; // 请替换为实际端口号

        private readonly string logFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ToggleFirewallButton_Checked(object sender, RoutedEventArgs e)
        {
            // 按钮开启状态
            var (success, output, error) = await RunPowerShellScriptAsync(scriptEnableFirewall);
            if (success)
            {
                StartMonitoring();
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = "状态：开启";
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"脚本执行失败:\n{error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async void ToggleFirewallButton_Unchecked(object sender, RoutedEventArgs e)
        {
            // 按钮关闭状态
            var (success, output, error) = await RunPowerShellScriptAsync(scriptDisableFirewall);
            if (success)
            {
                StopMonitoring();
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = "状态：关闭";
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"脚本执行失败:\n{error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        /// <summary>
        /// 运行 PowerShell 脚本并捕获输出和错误信息。
        /// </summary>
        /// <param name="script">PowerShell 脚本内容。</param>
        /// <returns>返回一个元组，包含执行是否成功、标准输出和标准错误。</returns>
        private async Task<(bool success, string output, string error)> RunPowerShellScriptAsync(string script)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 创建一个临时的 PowerShell 脚本文件
                    string tempScriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"script_{Guid.NewGuid()}.ps1");
                    System.IO.File.WriteAllText(tempScriptPath, script, Encoding.UTF8);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas" // 以管理员权限运行
                    };

                    using (Process process = new Process())
                    {
                        process.StartInfo = psi;
                        process.Start();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        process.WaitForExit();

                        // 删除临时脚本文件
                        try
                        {
                            System.IO.File.Delete(tempScriptPath);
                            Log("脚本删除成功。");
                        }
                        catch { }

                        if (process.ExitCode == 0 && string.IsNullOrWhiteSpace(error))
                        {
                            Log("脚本执行成功。");
                            Log($"输出: {output}");
                            return (true, output, error);
                        }
                        else
                        {
                            Log($"脚本执行失败，ExitCode: {process.ExitCode}");
                            Log($"错误: {error}");
                            Log($"输出: {output}");
                            return (false, output, error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"运行脚本时发生异常: {ex.ToString()}");
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"运行脚本时发生异常:\n{ex.ToString()}", "异常", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                    return (false, string.Empty, ex.ToString());
                }
            });
        }

        private void StartMonitoring()
        {
            monitorTimer = new Timer(30000); // 30秒
            monitorTimer.Elapsed += MonitorTimer_Elapsed;
            monitorTimer.AutoReset = true;
            monitorTimer.Enabled = true;

            Log($"已启动网络监控，每 30 秒一次。");
        }

        private void StopMonitoring()
        {
            if (monitorTimer != null)
            {
                monitorTimer.Stop();
                monitorTimer.Dispose();
                monitorTimer = null;

                Log("已停止网络监控。");
            }
        }

        private async void MonitorTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            monitorTimer.Stop(); // 避免重入

            string message = await CheckConnectivityAsync();
            Log($"检测到网络连通性:\n{message}");

            bool sendResult = await SendResultToServerAsync(serverIp, serverPort, message);
            if (sendResult)
            {
                Log("检测结果已成功发送到服务器。");
            }
            else
            {
                Log("发送检测结果到服务器失败。");
            }

            monitorTimer.Start(); // 重新启动计时器
        }

        private Task<string> CheckConnectivityAsync()
        {
            return Task.Run(() =>
            {
                StringBuilder sb = new StringBuilder();

                foreach (var host in hostsToCheck)
                {
                    bool isConnected = PingHost(host);
                    sb.AppendLine($"{host}: {(isConnected ? "Connected" : "Disconnected")}");
                }

                return sb.ToString();
            });
        }

        private bool PingHost(string host)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(host, 1000); // 超时1秒
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private Task<bool> SendResultToServerAsync(string serverIp, int port, string message)
        {
            return Task.Run(() =>
            {
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        var task = client.ConnectAsync(serverIp, port);
                        bool connected = task.Wait(2000); // 2秒超时
                        if (!connected)
                            return false;

                        NetworkStream stream = client.GetStream();
                        byte[] data = Encoding.UTF8.GetBytes(message);
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopMonitoring();
        }

        /// <summary>
        /// 记录日志到 log.txt 文件。
        /// </summary>
        /// <param name="message">日志消息。</param>
        private void Log(string message)
        {
            try
            {
                string logEntry = $"{DateTime.Now}: {message}";
                System.IO.File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
            catch
            {
                // 忽略日志写入失败的情况
            }
        }
    }
}
