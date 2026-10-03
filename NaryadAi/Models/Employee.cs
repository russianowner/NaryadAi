namespace NaryadAi.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; 
        public string Status { get; set; } = "Свободен";
        public string Brigade { get; set; } = string.Empty; 
        public int Grade { get; set; } 
        public string? PinCode { get; set; }
        public string Shift { get; set; } = string.Empty;
        public string? Login { get; set; } 
        public string? PasswordHash { get; set; } 
    }
}