// Models/ImportExportViewModel.cs
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace PoWorks_Rework.Models
{
    public class ImportExportViewModel
    {
        public List<string> HdsTables { get; set; } = new List<string>();
        public string SelectedTable { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public int Limit { get; set; } = 1000;
        public IFormFile VarexpFile { get; set; }
        public List<string[]> VarexpRecords { get; set; } = new List<string[]>();
    }
}