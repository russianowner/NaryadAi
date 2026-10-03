using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Components.Pages.Admin
{
    public partial class Employees : ComponentBase
    {
        [Inject] private AppDbContext DbContext { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!; 

        private List<Employee> employees = new();
        private Employee newEmployee = new Employee { Role = "Worker" };
        private string searchString = "";

        // Списки для выпадающих меню
        private List<ReferenceItem> specialties = new();
        private List<ReferenceItem> shifts = new();
        private List<ReferenceItem> brigades = new();

        protected override async Task OnInitializedAsync()
        {
            await LoadDictionaries(); 
            await LoadEmployees();    
        }

        private async Task LoadDictionaries()
        {
            var allRefs = await DbContext.ReferenceItems.ToListAsync();
            specialties = allRefs.Where(r => r.Category == "Specialty").OrderBy(r => r.Name).ToList();
            shifts = allRefs.Where(r => r.Category == "Shift").OrderBy(r => r.Name).ToList();
            brigades = allRefs.Where(r => r.Category == "Brigade").OrderBy(r => r.Name).ToList();
        }

        private async Task LoadEmployees()
        {
            employees = await DbContext.Employees.OrderByDescending(e => e.Id).ToListAsync();
        }

        private async Task SaveEmployee()
        {
            if (string.IsNullOrWhiteSpace(newEmployee.FullName))
            {
                Snackbar.Add("Введите ФИО сотрудника", Severity.Warning);
                return;
            }

            DbContext.Employees.Add(newEmployee);
            await DbContext.SaveChangesAsync();

            Snackbar.Add($"Сотрудник {newEmployee.FullName} успешно добавлен", Severity.Success);

            await LoadEmployees();
            newEmployee = new Employee { Role = "Worker" };
        }

        private async Task DeleteEmployee(Employee emp)
        {
            DbContext.Employees.Remove(emp);
            await DbContext.SaveChangesAsync();
            Snackbar.Add($"Сотрудник {emp.FullName} удален", Severity.Error);
            await LoadEmployees();
        }

        private bool FilterFunc1(Employee emp) => FilterFunc(emp, searchString);

        private bool FilterFunc(Employee emp, string searchString)
        {
            if (string.IsNullOrWhiteSpace(searchString)) return true;
            if (emp.FullName.Contains(searchString, StringComparison.OrdinalIgnoreCase)) return true;
            if (emp.Specialty.Contains(searchString, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}