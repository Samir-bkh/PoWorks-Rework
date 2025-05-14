// Models/GeneralSettingsViewModel.cs
namespace PoWorks_Rework.Models
{
    public class GeneralSettingsViewModel
    {
        public DatabaseSettings PostgreSql { get; set; } = new DatabaseSettings();
        public SqlServerSettings SqlServer { get; set; } = new SqlServerSettings();
    }
}