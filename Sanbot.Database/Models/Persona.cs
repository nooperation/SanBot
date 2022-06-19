using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanBot.Database.Models
{
    public class Persona
    {
        public Guid Id { get; set; }
        public string Handle { get; set; } = default!;
        public string Name { get; set; } = default!;
    }
}
