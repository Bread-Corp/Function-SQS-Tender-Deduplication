using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenderDeduplication.Models
{
    /// <summary>
    /// Represents a tender from Transnet.
    /// </summary>
    public class TransnetTender : BaseTender
    {
        [Required]
        public string TenderNumber { get; set; }
    }
}
