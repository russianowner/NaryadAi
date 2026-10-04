import re
path = r'C:\Users\souja\source\repos\NaryadAi\NaryadAi\Components\Pages\Worker\WorkerPanel.razor.cs'
with open(path, 'r', encoding='utf-8', errors='replace') as f:
    content = f.read()
content = re.sub(r'(?s)private async Task RequestNotificationPermission\(\).*?\}', 
    'private async Task RequestNotificationPermission()\n        {\n            var granted = await JSRuntime.InvokeAsync<bool>(\"naryadNotifications.requestPermission\");\n            if (granted) Snackbar.Add(\"Уведомления включены\", Severity.Success);\n            else Snackbar.Add(\"Уведомления заблокированы в браузере\", Severity.Warning);\n        }', 
    content)
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
