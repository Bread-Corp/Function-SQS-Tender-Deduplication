using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TenderDeduplication.Models
{
    /// <summary>
    /// Represents a tender from the eTenders portal.
    /// Note: The class name 'eTender' is used to match your provided DbContext.
    /// </summary>
    public class eTender : BaseTender
    {
        [Required]
        public string TenderNumber { get; set; }

        [JsonPropertyName("closingDate")]
        public DateTime DateClosing { get; set; }
    }
}
