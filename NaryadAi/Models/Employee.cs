namespace NaryadAi.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; 
        public string Status { get; set; } = "Свободен"; 
    }
}
