using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Timers;
using System.Net.Http;
using System.Text.Json;
using System.Configuration;
using Timer = System.Timers.Timer;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        // PowerShell脚本内容
        private readonly string scriptEnableFirewall = @"
# 设置防火墙默认出站策略为“阻止”
Set-NetFirewallProfile -Profile Domain,Public,Private -DefaultOutboundAction Block

$ip1 = '121.48.165.91' 
$ip2 = 'your_server_ip' 
# 创建允许访问matu的出站规则
New-NetFirewallRule -DisplayName 'Allow matu $ip1' -Direction Outbound -Action Allow -RemoteAddress $ip1 -Protocol Any
New-NetFirewallRule -DisplayName 'Allow matu $ip2' -Direction Outbound -Action Allow -RemoteAddress $ip2 -Protocol Any
";

        private readonly string scriptDisableFirewall = @"
# 设置防火墙默认出站策略为“允许”
Set-NetFirewallProfile -Profile Domain,Public,Private -DefaultOutboundAction Allow

# 移除特定的防火墙规则
Get-NetFirewallRule -DisplayName 'Allow matu*' | Remove-NetFirewallRule
";

        private Timer monitorTimer;
        private readonly string[] hostsToCheck;
        private readonly string serverIp;
        private readonly int serverPort;
        private readonly string apiLogEndpoint;
        private readonly string apiKey;
        private readonly double monitorIntervalSeconds;

        private string userName;
        private string studentID;

        private static readonly HttpClient httpClient = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();

            // 读取配置
            serverIp = ConfigurationManager.AppSettings["ServerIp"];
            serverPort = int.TryParse(ConfigurationManager.AppSettings["ServerPort"], out int port) ? port : 1234;
            apiLogEndpoint = ConfigurationManager.AppSettings["ApiLogEndpoint"] ?? $"http://{serverIp}:{serverPort}/api/logs";  
            apiKey = ConfigurationManager.AppSettings["ApiKey"] ?? "your_secure_api_key"; // 替换为您的 API 密钥
            hostsToCheck = (ConfigurationManager.AppSettings["HostsToCheck"] ?? "baidu.com,aliyun.com").Split(',');
            monitorIntervalSeconds = double.TryParse(ConfigurationManager.AppSettings["MonitorIntervalSeconds"], out double interval) ? interval : 30;
        }

        // 窗口加载时触发
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 显示输入弹窗
            InputOverlay.Visibility = Visibility.Visible;
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameTextBox.Text) || string.IsNullOrWhiteSpace(StudentIDTextBox.Text))
            {
                MessageBox.Show("请输入姓名和学号。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            userName = NameTextBox.Text.Trim();
            studentID = StudentIDTextBox.Text.Trim();

            // 记录用户登录日志
            bool logSuccess = await LogAsync("INFO", $"用户登录: 姓名={userName}, 学号={studentID}");

            if (logSuccess)
            {
                // 隐藏输入弹窗并显示主内容
                InputOverlay.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show("登录日志上传失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 用户取消，退出应用程序
            Application.Current.Shutdown();
        }

        private async void ToggleFirewallButton_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                // 按钮开启状态
                var (success, output, error) = await RunPowerShellScriptAsync(scriptEnableFirewall);
                if (success)
                {
                    await StartMonitoringAsync();
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
            catch (Exception ex)
            {
                MessageBox.Show($"开启防火墙时发生异常:\n{ex.Message}", "异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ToggleFirewallButton_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                // 按钮关闭状态
                var (success, output, error) = await RunPowerShellScriptAsync(scriptDisableFirewall);
                if (success)
                {
                    await StopMonitoringAsync();
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
            catch (Exception ex)
            {
                MessageBox.Show($"关闭防火墙时发生异常:\n{ex.Message}", "异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 运行 PowerShell 脚本并捕获输出和错误信息。
        /// </summary>
        /// <param name="script">PowerShell 脚本内容。</param>
        /// <returns>返回一个元组，包含执行是否成功、标准输出和标准错误。</returns>
        private async Task<(bool success, string output, string error)> RunPowerShellScriptAsync(string script)
        {
            try
            {
                // 创建一个临时的 PowerShell 脚本文件
                string tempScriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"script_{Guid.NewGuid()}.ps1");
                await System.IO.File.WriteAllTextAsync(tempScriptPath, script, Encoding.UTF8);

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
                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    // 异步读取标准输出和错误输出
                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            outputBuilder.AppendLine(args.Data);
                    };
                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            errorBuilder.AppendLine(args.Data);
                    };

                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync();

                    // 删除临时脚本文件
                    try
                    {
                        System.IO.File.Delete(tempScriptPath);
                    }
                    catch { }

                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();

                    if (process.ExitCode == 0 && string.IsNullOrWhiteSpace(error))
                    {
                        await LogAsync("INFO", "脚本执行成功。输出: " + output);
                        return (true, output, error);
                    }
                    else
                    {
                        await LogAsync("ERROR", $"脚本执行失败，ExitCode: {process.ExitCode}。错误: {error}。输出: {output}");
                        return (false, output, error);
                    }
                }
            }
            catch (Exception ex)
            {
                await LogAsync("EXCEPTION", $"运行脚本时发生异常: {ex}");
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"运行脚本时发生异常:\n{ex}", "异常", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return (false, string.Empty, ex.ToString());
            }
        }

        private async Task StartMonitoringAsync()
        {
            if (monitorTimer == null)
            {
                monitorTimer = new Timer(monitorIntervalSeconds * 1000); // 以秒为单位
                monitorTimer.Elapsed += async (sender, e) => await MonitorTimer_Elapsed(sender, e);
                monitorTimer.AutoReset = true;
                monitorTimer.Enabled = true;

                await LogAsync("INFO", $"已启动网络监控，每 {monitorIntervalSeconds} 秒一次。");
            }
        }

        private async Task StopMonitoringAsync()
        {
            if (monitorTimer != null)
            {
                monitorTimer.Stop();
                monitorTimer.Dispose();
                monitorTimer = null;

                await LogAsync("INFO", "已停止网络监控。");
            }
        }

        private async Task MonitorTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                monitorTimer.Stop(); // 避免重入

                string message = await CheckConnectivityAsync();
                await LogAsync("INFO", $"检测到网络连通性:\n{message}");

                bool sendResult = await SendResultToServerAsync(message);
                if (sendResult)
                {
                    await LogAsync("INFO", "检测结果已成功发送到服务器。");
                }
                else
                {
                    await LogAsync("ERROR", "发送检测结果到服务器失败。");
                }
            }
            catch (Exception ex)
            {
                await LogAsync("EXCEPTION", $"网络监控时发生异常: {ex}");
            }
            finally
            {
                monitorTimer.Start(); // 重新启动计时器
            }
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

        private async Task<bool> SendResultToServerAsync(string message)
        {
            try
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    Level = "INFO",
                    UserName = userName,
                    StudentID = studentID
                };

                string jsonString = JsonSerializer.Serialize(logEntry);
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                using (var request = new HttpRequestMessage(HttpMethod.Post, apiLogEndpoint))
                {
                    request.Content = content;
                    request.Headers.Add("X-API-KEY", apiKey);

                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    else
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        await LogAsync("ERROR", $"服务器返回错误状态码: {response.StatusCode}。响应内容: {responseContent}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                await LogAsync("EXCEPTION", $"发送日志到服务器时发生异常: {ex}");
                return false;
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            await StopMonitoringAsync();
            base.OnClosed(e);
        }

        /// <summary>
        /// 记录日志到 API 接口。
        /// </summary>
        /// <param name="level">日志级别，例如 "INFO", "ERROR", "EXCEPTION"</param>
        /// <param name="message">日志消息。</param>
        /// <returns></returns>
        private async Task<bool> LogAsync(string level, string message)
        {
            try
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    Level = level,
                    UserName = userName,
                    StudentID = studentID
                };

                string jsonString = JsonSerializer.Serialize(logEntry);
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                using (var request = new HttpRequestMessage(HttpMethod.Post, apiLogEndpoint))
                {
                    request.Content = content;
                    request.Headers.Add("X-API-KEY", apiKey);

                    HttpResponseMessage response = await httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 如果日志上传失败，记录到本地文件作为备份
                        string logFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log_backup.txt");
                        string logToBackup = $"{logEntry.Timestamp}: [{logEntry.Level}] {logEntry.Message}";
                        await System.IO.File.AppendAllTextAsync(logFilePath, logToBackup + Environment.NewLine);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                // 如果在记录日志时发生异常，记录到本地文件作为备份
                string logFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log_backup.txt");
                string logToBackup = $"{DateTime.Now}: [EXCEPTION] 记录日志时发生异常: {ex}";
                await System.IO.File.AppendAllTextAsync(logFilePath, logToBackup + Environment.NewLine);
                return false;
            }
        }
    }

    /// <summary>
    /// 日志条目模型
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public string Level { get; set; } // 如 "INFO", "ERROR", "EXCEPTION"
        public string UserName { get; set; }
        public string StudentID { get; set; }
    }

    /// <summary>
    /// 扩展方法用于异步等待进程退出
    /// </summary>
    public static class ProcessExtensions
    {
        public static Task WaitForExitAsync(this Process process, int timeout = int.MaxValue)
        {
            if (process.HasExited)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(null);
            if (timeout != int.MaxValue)
            {
                var timer = new System.Timers.Timer(timeout);
                timer.Elapsed += (s, e) =>
                {
                    tcs.TrySetException(new TimeoutException("Process timed out."));
                    timer.Stop();
                    timer.Dispose();
                };
                timer.AutoReset = false;
                timer.Start();
            }
            return tcs.Task;
        }
    }
}
