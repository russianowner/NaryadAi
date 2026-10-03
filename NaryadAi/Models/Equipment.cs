namespace NaryadAi.Models
{
    public class Equipment
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string InventoryNumber { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty; 
        public string Type { get; set; } = string.Empty;
        public string Criticality { get; set; } = "Высокая";
    }
}