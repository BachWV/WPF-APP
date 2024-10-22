
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