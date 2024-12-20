
禁止你访问互联网！只能访问特定的网站！
采用先进的dotnet8.0和Material Design设计！

非常简易的后端，只有一个文件，不需要数据库！

```python
from flask import Flask, request, jsonify
import os
from datetime import datetime
app = Flask(__name__)
BASE_LOG_DIR = "logs"

# 确保基础日志目录存在
os.makedirs(BASE_LOG_DIR, exist_ok=True)

@app.route('/api/logs', methods=['POST'])
def receive_log():
    try:
        data = request.get_json()
        if not data:
            return jsonify({"error": "No JSON data provided"}), 400

        timestamp = data.get('Timestamp')
        message = data.get('Message')
        level = data.get('Level')
        user_name = data.get('UserName')
        student_id = data.get('StudentID')
        if not all([timestamp, message, level, user_name, student_id]):
            return jsonify({"error": "Missing required log fields"}), 400
        student_dir = os.path.join(BASE_LOG_DIR, student_id)
        os.makedirs(student_dir, exist_ok=True)
        log_file_path = os.path.join(student_dir, 'logs.txt')
        log_entry = f"{timestamp}: [{level}] {user_name} - {message}\n"
        with open(log_file_path, 'a', encoding='utf-8') as f:
            f.write(log_entry)
        return jsonify({"status": "success"}), 200

    except Exception as e:
        return jsonify({"error": str(e)}), 500

if __name__ == '__main__':
    # 运行 Flask 应用，监听所有 IP，端口 1234
    app.run(host='0.0.0.0', port=1234)

```

# 桌面使用说明

1.下载release包

https://github.com/BachWV/WPF-APP/releases

2.放在任意位置。双击打开。需要管理员权限运行。

![img](https://r2.csapp.fun/2024/12/asynccode.png)

填写姓名学号后点击确定。

考试时，将按钮拨到右侧。然后打开码图(matu.uestc.edu.cn)，或者通过IP直接访问121.48.165.91

![](https://r2.csapp.fun/2024/12/20241220171028.png)

考试结束，请拨回按钮。注意一定要拨回按钮。

# 缘由

如何让Windows下只允许访问matu而无法访问互联网

## 可行方案一

使用Windows Defender 防火墙，设置防火墙默认出站策略为阻止，即无法访问任何一个网站，随后创建一条新规则允许对matu.uestc.edu.cn 的ip的访问。

配置防火墙的方案可以使用图形化界面，也可以以管理员的方式运行以下内容的ps1文件。

```Bash
# 设置防火墙默认出站策略为“阻止”
Set-NetFirewallProfile -Profile Domain,Public,Private -DefaultOutboundAction Block

$ip = "121.48.165.91" 

# 创建允许访问matu的出站规则

New-NetFirewallRule -DisplayName "Allow matu $ip" -Direction Outbound -Action Allow -RemoteAddress $ip -Protocol Any 
```

在完成以后，浏览器访问其他页面将提示“您被禁止访问互联网”。

![](https://r2.csapp.fun/2024/12/20241220171151.png)

而matu.uestc.edu.cn可以正常访问。

在完成答题后，使用以下脚本允许出站策略，删除已经创建的matu传出规则。

```Bash
Set-NetFirewallProfile -Profile Domain,Public,Private -DefaultOutboundAction Allow
Get-NetFirewallRule -DisplayName "Allow matu*" | Remove-NetFirewallRule
```

该方案在我的电脑上验证可行。

缺陷:

1. 同学关闭Windows Defender防火墙，使得防火墙的所有规则无效。
2. 同学在个人电脑上使用虚拟机或其他容器，例如Hyper-V创建虚拟交换机，绕开Windows本机网络。又比如使用Vmware虚拟机创建虚拟网卡，在虚拟机中可以正常访问网络。
3. 同学使用某些加速器、vpn，同样会创建虚拟网卡，不受防火墙控制。

## 可行方案二

代理同学们电脑的所有流量，所有允许对matu.uestc.edu.cn 的访问。

本方案使用常见的vpn软件Clash代理所有流量。

需要下载Clash for Windows。

打开Profiles界面，在Download from a url 里填入http://120.24.176.162/power/matu.yaml 

![](https://r2.csapp.fun/2024/12/20241220171219.png)

matu.yaml文件内容如下

```YAML
port: 7890
socks-port: 7891
allow-lan: true
mode: rule
log-level: info

# 启用DNS设置，防止DNS泄漏
dns:
  enable: true
  listen: 0.0.0.0:53
  nameserver:
    - 114.114.114.114
    - 223.5.5.5

proxies: []

# 定义代理组
proxy-groups:
  - name: "Default"
    type: select
    proxies:
      - DIRECT
      - REJECT

# 规则设置
rules:
  - DOMAIN-SUFFIX,matu.uestc.edu.cn,DIRECT
  - MATCH,REJECT
```

说明：

**rules**：

- `DOMAIN-SUFFIX,``matu.uestc.edu.cn``,DIRECT`：对于域名后缀为matu.uestc.edu.cn的请求，使用`DIRECT`直接连接。
- `MATCH,REJECT`：对于所有其他请求，使用`REJECT`拒绝连接。

浏览器中访问其他网站提示：

![](https://r2.csapp.fun/2024/12/20241220171242.png)

而matu可以正常访问。

该方案在我的电脑上验证可行。

缺陷:

1. 并未代理所有流量，只按照规则过滤了域名，基于udp的流量无法限制。经过测试QQ聊天，QQ内置浏览器（QQ内置游戏无法使用,内置小程序部分正常），微信，飞书等软件仍可以正常使用。为了更严格的限制，需要启用tun模式，还未实验，或使用更严格的规则。
2. 同学可以修改代理软件的配置文档，解除访问互联网的限制

### Q&A

#### Q1:如何让同学们自愿运行clash，阻止自身的网络连接？

答：可以将matu网站限制公网访问，通过反向代理等方式使得只可以通过clash访问matu网站，这样达到了强制同学们运行clash的目的。

#### Q2:是否可以使用其他代理软件：

答：可以使用类似的Squid代理服务器。 

#### Q3:是否可以将clash放到远端服务器，让同学们直接将Windows代理服务器指向互联网上的clash?

答：这样同学们本机将无需安装clash。我曾经试过类似的方案，但是这种方式易被防火墙拦截，HTTP代理流量可能无法直接在互联网上传输。在同一个交换机下，我认为这么做也许可行，也是最好的方案。

## 可行方案三

类似软路由方案，全部代理流量。在Windows上层使用openwrt等操作系统，并配置流量规则。由于Windows在打开虚拟化后，也是在Hyper-V层运行的虚拟机。

在Hyper-V管理器中，配置创建虚拟交换机，并打通和Windows主机的通信。

该方案和可行方案二类似，需要在Windows上设置代理服务器指向openwrt。

另一种方式：创建虚拟机镜像或者docker镜像，在Windows的下层制作虚拟机（容器），通过反向代理等方式使得只可以通过虚拟机镜像或者docker镜像访问matu网站，强制同学们运行定制虚拟机镜像或者docker镜像。

这些方式较为复杂，还未实验。