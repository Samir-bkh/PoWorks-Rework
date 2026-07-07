using System.Diagnostics.Eventing.Reader;

namespace PoWorks_Rework.Models
{
    public class SqlServerConnectionCollection
    {
        public List<SqlServerSettings> Connections { get; set; } = new List<SqlServerSettings>();
        public string DefaultConnectionId { get; set; } = "";

        public SqlServerSettings GetDefaultConnection()
        {
            return Connections.FirstOrDefault(c => c.ConnectionId == DefaultConnectionId)
                   ?? Connections.FirstOrDefault(c => c.IsDefault)
                   ?? Connections.FirstOrDefault();
        }

        public SqlServerSettings GetConnection(string connectionId)
        {
            return Connections.FirstOrDefault(c => c.ConnectionId == connectionId);
        }

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