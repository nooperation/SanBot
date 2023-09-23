using SanBot.Database.Data;
using SanBot.Database.Services;

namespace SanBot.Database
{
    public class PersonaDatabase
    {
        private ApplicationDbContext _context { get; }
        public PersonaService PersonaService { get; }

        public PersonaDatabase()
        {
            _context = new ApplicationDbContext();
            _context.Database.EnsureCreated();

            PersonaService = new PersonaService(_context);
        }

        private void Shutdown()
        {
            _context.Dispose();
        }
    }
}
