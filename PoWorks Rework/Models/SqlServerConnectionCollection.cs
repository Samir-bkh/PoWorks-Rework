using System.Diagnostics.Eventing.Reader;

namespace PoWorks_Rework.Models
{
    /// <summary>
    /// Manages a collection of SQL Server connections.
    /// Handles multiple database connections with default connection tracking.
    /// </summary>
    public class SqlServerConnectionCollection
    {
        /// <summary>
        /// List of SQL Server connection configurations
        /// </summary>
        public List<SqlServerSettings> Connections { get; set; } = new List<SqlServerSettings>();

        /// <summary>
        /// ID of the currently configured default connection
        /// </summary>
        public string DefaultConnectionId { get; set; } = "";

        /// <summary>
        /// Gets the default connection or falls back to first available connection.
        /// Returns null if no connections exist.
        /// </summary>
        public SqlServerSettings GetDefaultConnection()
        {
            return Connections.FirstOrDefault(c => c.ConnectionId == DefaultConnectionId)
                   ?? Connections.FirstOrDefault(c => c.IsDefault)
                   ?? Connections.FirstOrDefault();
        }

        /// <summary>
        /// Retrieves a specific connection by connection ID.
        /// Returns null if not found.
        /// </summary>
        public SqlServerSettings GetConnection(string connectionId)
        {
            return Connections.FirstOrDefault(c => c.ConnectionId == connectionId);
        }

        /// <summary>
        /// Adds a new SQL Server connection to the collection.
        /// Automatically sets as default if it's the first connection.
        /// </summary>
        public void AddConnection(SqlServerSettings connection)
        {
            if (string.IsNullOrEmpty(connection.ConnectionId))
            {
                connection.ConnectionId = Guid.NewGuid().ToString();
            }

            if (!Connections.Any())
            {
                connection.IsDefault = true;
                DefaultConnectionId = connection.ConnectionId;
            }

            Connections.Add(connection);
        }

        /// <summary>
        /// Removes a connection by its connection ID.
        /// If removed connection was default, sets a new default.
        /// </summary>
        public void RemoveConnection(string connectionId)
        {
            var connection = GetConnection(connectionId);
            if (connection != null)
            {
                Connections.Remove(connection);

                if (connection.IsDefault && Connections.Any())
                {
                    var newDefault = Connections.First();
                    newDefault.IsDefault = true;
                    DefaultConnectionId = newDefault.ConnectionId;
                }
            }
        }

        /// <summary>
        /// Sets a specific connection as the default.
        /// Updates all other connections to not be default.
        /// </summary>
        public void SetDefaultConnection(string connectionId)
        {
            foreach (var conn in Connections)
            {
                conn.IsDefault = false;
            }

            var newDefault = GetConnection(connectionId);
            if (newDefault != null)
            {
                newDefault.IsDefault = true;
                DefaultConnectionId = connectionId;
             }
        }
    }
}