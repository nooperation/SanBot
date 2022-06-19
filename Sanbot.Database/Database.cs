using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SanBot.Database.Services;

namespace SanBot.Database
{
    public class Database
    {
        private ApplicationDbContext _context { get; }
        public PersonaService PersonaService { get; }

        public Database()
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
