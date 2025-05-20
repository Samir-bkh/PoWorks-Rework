using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;

namespace PoWorks_Rework.Models
{
    // Model for an individual HDS meter to be imported
    public class HDSMeterItem
    {
        public string HdsMeterName { get; set; } = "";
        public string Unit { get; set; } = "";
        public string ParentMeterId { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string Type { get; set; } = "Main";
        public bool Active { get; set; } = true;
        public bool IsSelected { get; set; } = true;
    }

    // Main view model for the HDS meter selection modal
    public class HDSMeterSelectionViewModel
    {
        public List<HDSMeterItem> HdsMeters { get; set; } = new List<HDSMeterItem>();
        public List<SelectListItem> ParentMeterOptions { get; set; } = new List<SelectListItem>();
        public string TableName { get; set; } = "";
        public bool SkipExisting { get; set; } = true;
        public bool UpdateExisting { get; set; } = false;
    }

    // Model for meter import request
    public class ImportMetersRequest
    {
        public List<HDSMeterItem> Meters { get; set; } = new List<HDSMeterItem>();
        public string TableName { get; set; } = "";
        public ImportOptions Options { get; set; } = new ImportOptions();
    }

    // Import options
    public class ImportOptions
    {
        public bool SkipExisting { get; set; } = true;
        public bool UpdateExisting { get; set; } = false;
        public bool CreateMissingParents { get; set; } = false;
        public bool CreateMissingTenants { get; set; } = false;

        // Reading import options
        public bool ImportReadings { get; set; } = true;
        public string ReadingsStartDate { get; set; }
        public string ReadingsEndDate { get; set; }
        public int ReadingsLimit { get; set; } = 1000;
    }

    // Response for import operation
    public class ImportMetersResponse
    {
        public bool Success { get; set; }
        public int ImportedCount { get; set; }
        public int ErrorCount { get; set; }
        public int ImportedReadings { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<string> ImportedMeters { get; set; } = new List<string>();
        public List<string> ErrorMeters { get; set; } = new List<string>();
    }
}