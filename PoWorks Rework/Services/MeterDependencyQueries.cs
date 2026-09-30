namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Set-based SQL used to evaluate whether meters can be permanently deleted.
    /// The bulk query scans each dependency table once for the selected meter IDs
    /// instead of issuing one multi-count query per meter.
    /// </summary>
    public static class MeterDependencyQueries
    {
        public const string BatchSummary = @"
            WITH selected AS (
                SELECT UNNEST(@MeterIds) AS ""MeterId""
            ),
            raw_readings AS (
                SELECT ""MeterId"", COUNT(*) AS ""DependencyCount""
                FROM ""MeterReadings""
                WHERE ""CompanyId"" = @CompanyId
                  AND ""MeterId"" = ANY(@MeterIds)
                GROUP BY ""MeterId""
            ),
            daily_readings AS (
                SELECT ""MeterId"", COUNT(*) AS ""DependencyCount""
                FROM ""MeterReadingsDaily""
                WHERE ""CompanyId"" = @CompanyId
                  AND ""MeterId"" = ANY(@MeterIds)
                GROUP BY ""MeterId""
            ),
            monthly_readings AS (
                SELECT ""MeterId"", COUNT(*) AS ""DependencyCount""
                FROM ""MeterReadingsMonthly""
                WHERE ""CompanyId"" = @CompanyId
                  AND ""MeterId"" = ANY(@MeterIds)
                GROUP BY ""MeterId""
            ),
            yearly_readings AS (
                SELECT ""MeterId"", COUNT(*) AS ""DependencyCount""
                FROM ""MeterReadingsYearly""
                WHERE ""CompanyId"" = @CompanyId
                  AND ""MeterId"" = ANY(@MeterIds)
                GROUP BY ""MeterId""
            ),
            bill_lines AS (
                SELECT ""MeterId"", COUNT(*) AS ""DependencyCount""
                FROM ""BillLineItems""
                WHERE ""MeterId"" = ANY(@MeterIds)
                GROUP BY ""MeterId""
            ),
            child_meters AS (
                SELECT ""ParentId"" AS ""MeterId"", COUNT(*) AS ""DependencyCount""
                FROM ""Meters""
                WHERE ""CompanyId"" = @CompanyId
                  AND ""ParentId"" = ANY(@MeterIds)
                GROUP BY ""ParentId""
            )
            SELECT
                s.""MeterId"",
                COALESCE(r.""DependencyCount"", 0) AS ""RawReadingCount"",
                COALESCE(d.""DependencyCount"", 0) AS ""DailyReadingCount"",
                COALESCE(m.""DependencyCount"", 0) AS ""MonthlyReadingCount"",
                COALESCE(y.""DependencyCount"", 0) AS ""YearlyReadingCount"",
                COALESCE(b.""DependencyCount"", 0) AS ""BillLineCount"",
                COALESCE(c.""DependencyCount"", 0) AS ""ChildMeterCount""
            FROM selected s
            LEFT JOIN raw_readings r ON r.""MeterId"" = s.""MeterId""
            LEFT JOIN daily_readings d ON d.""MeterId"" = s.""MeterId""
            LEFT JOIN monthly_readings m ON m.""MeterId"" = s.""MeterId""
            LEFT JOIN yearly_readings y ON y.""MeterId"" = s.""MeterId""
            LEFT JOIN bill_lines b ON b.""MeterId"" = s.""MeterId""
            LEFT JOIN child_meters c ON c.""MeterId"" = s.""MeterId""
            ORDER BY s.""MeterId"";";
    }
}
