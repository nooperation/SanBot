using Microsoft.EntityFrameworkCore;
using SanBot.Database.Models;
using SanBot.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanBot.Database.Services
{
    public class PersonaService
    {
        private readonly ApplicationDbContext _context;

        public PersonaService(ApplicationDbContext context)
        {
            this._context = context;
        }

        public class PersonaDto
        {
            public PersonaDto()
            {
                Name = default!;
                Handle = default!;
            }
            public PersonaDto(Persona persona)
            {
                this.Id = persona.Id;
                this.Handle = persona.Handle;
                this.Name = persona.Name;
            }

            public Guid Id { get; set; }
            public string Handle { get; set; }
            public string Name { get; set; }
        }
        public async Task UpdatePersonaAsync(Guid personaId, string handle, string name)
        {
            var persona = await _context.Personas
                .Where(n => n.Id == personaId)
                .FirstOrDefaultAsync();
            if (persona != null)
            {
                persona.Handle = handle;
                persona.Name = name;
            }
            else
            {
                await _context.Personas.AddAsync(new Persona()
                {
                    Id = personaId,
                    Handle = handle,
                    Name = name
                });
            }

            await _context.SaveChangesAsync();
            return;
        }

        public async Task<PersonaDto?> GetPersona(Guid personaId)
        {
            var persona = await _context.Personas.FindAsync(personaId);
            if (persona == null)
            {
                return null;
            }

            return new PersonaDto(persona);
        }
    }
}
