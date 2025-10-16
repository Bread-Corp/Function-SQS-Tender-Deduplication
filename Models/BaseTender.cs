using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenderDeduplication.Models
{
    /// <summary>
    /// Base class for all tender types, mapping to the BaseTender table.
    /// </summary>
    public abstract class BaseTender
    {
        [Key]
        public Guid TenderID { get; set; }

        [Required]
        public string Source { get; set; }
    }
}
