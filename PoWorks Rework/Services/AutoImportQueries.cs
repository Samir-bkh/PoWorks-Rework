namespace PoWorks_Rework.Services
{
    public static class AutoImportQueries
    {
        public const string ActiveMeters = @"
            SELECT ""MeterId"", ""Name"", ""Active""
            FROM ""Meters""
            WHERE ""CompanyId"" = @companyId
              AND (""Name"" LIKE '%.%' OR ""Name"" LIKE 'varsets.%')
              AND ""Active"" = TRUE";

        public const string LastReadings = @"
            SELECT DISTINCT ON (""MeterId"") ""MeterId"", ""Timestamp"", ""Value""
            FROM ""MeterReadings""
            WHERE ""CompanyId"" = @companyId
            ORDER BY ""MeterId"", ""Timestamp"" DESC";

        public const string ApiSettings = @"
            SELECT ""ConnectionId"", ""ConnectionName"", ""BaseUrl"", ""ClientId"", ""ClientSecret"",
                   ""ApiKey"", ""Username"", ""Password"", ""AuthType"", ""TimeoutSeconds"",
                   ""ProjectName"", ""IsDefault"", ""IsActive"", ""EnableAutomaticImport""
            FROM ""WebServiceConnections""
            WHERE ""CompanyId"" = @companyId
              AND ""IsActive"" = TRUE
            ORDER BY ""IsDefault"" DESC, ""ConnectionId""
            LIMIT 1";
    }
}
