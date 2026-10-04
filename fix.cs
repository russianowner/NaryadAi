using System.IO;
using System.Text;
using System.Text.RegularExpressions;

var path = "C:\\Users\\souja\\source\\repos\\NaryadAi\\NaryadAi\\Components\\Pages\\Worker\\WorkerPanel.razor.cs";
var content = File.ReadAllText(path, Encoding.UTF8);
content = Regex.Replace(content, @"(?s)private async Task RequestNotificationPermission\(\).*?\}", 
    "private async Task RequestNotificationPermission()\n        {\n            var granted = await JSRuntime.InvokeAsync<bool>(\"naryadNotifications.requestPermission\");\n            if (granted) Snackbar.Add(\"Уведомления включены\", Severity.Success);\n            else Snackbar.Add(\"Уведомления заблокированы в браузере\", Severity.Warning);\n        }");
File.WriteAllText(path, content, Encoding.UTF8);
