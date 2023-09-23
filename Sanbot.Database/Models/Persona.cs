namespace SanBot.Database.Models
{
    public class Persona
    {
        public Guid Id { get; set; }
        public string Handle { get; set; } = default!;
        public string Name { get; set; } = default!;
    }
}
