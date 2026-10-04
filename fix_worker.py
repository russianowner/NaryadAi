import re
path = r'C:\Users\souja\source\repos\NaryadAi\NaryadAi\Components\Pages\Worker\WorkerPanel.razor.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add using Microsoft.JSInterop; if missing
if 'using Microsoft.JSInterop;' not in content:
    content = content.replace('using Microsoft.AspNetCore.Components;', 'using Microsoft.AspNetCore.Components;\nusing Microsoft.JSInterop;')

# Replace OnWorkOrderChanged completely
new_on_work_order = '''        private void OnWorkOrderChanged(WorkOrderUpdate update)
        {
            if (suppressNotifierRefresh || Interlocked.Exchange(ref refreshPending, 1) != 0)
                return;

            _ = InvokeAsync(async () =>
            {
                try
                {
                    await Task.Delay(50);
                    if (disposed || !isAuthorized) return;
                    materialItems = await DbContext.ReferenceItems.Where(r => r.Category == "Material").OrderBy(r => r.Name).ToListAsync();
                    
                    var oldTaskIds = myTasks.Select(x => x.Id).ToHashSet();
                    await LoadMyTasks();
                    
                    var newTasks = myTasks.Where(x => !oldTaskIds.Contains(x.Id)).ToList();
                    foreach (var t in newTasks)
                    {
                        var isEmergency = t.Priority == "Аварийный";
                        await JSRuntime.InvokeVoidAsync(
                            "naryadNotifications.show",
                            $"Новый наряд: {t.Equipment?.Name ?? "Оборудование"}",
                            new { body = t.Description, data = isEmergency ? "emergency" : "normal" }
                        );
                    }
                    StateHasChanged();
                }
                finally
                {
                    Interlocked.Exchange(ref refreshPending, 0);
                }
            });
        }'''

content = re.sub(r'(?s)        private void OnWorkOrderChanged\(WorkOrderUpdate update\)\s*\{.*?(?=        public void Dispose\(\))', new_on_work_order + '\n\n', content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
