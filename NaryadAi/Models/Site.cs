namespace NaryadAi.Models;

public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Equipment> Equipments { get; set; } = new List<Equipment>();
}
