using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ChronoLog
{
    public static class DatabaseHelper
    {
        // This creates a hidden file in your current user's AppData folder
        // e.g., C:\Users\Timo\AppData\Roaming\ChronoLog\chronolog.db
        private static string dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChronoLog");
        private static string dbFile = Path.Combine(dbFolder, "chronolog.db");
        private static string connectionString = $"Data Source={dbFile}";

        public static void InitializeDatabase()
        {
            // Ensure the folder exists
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            // Connect to SQLite
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // 1. Create the Entries table
                string createTableSql = @"
                    CREATE TABLE IF NOT EXISTS Entries (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        ContextName TEXT,
                        Note TEXT NOT NULL
                    );";
                connection.Execute(createTableSql);

                // 2. Create the Contexts table
                string createContextsSql = @"
                    CREATE TABLE IF NOT EXISTS Contexts (
                        Name TEXT PRIMARY KEY
                    );";
                connection.Execute(createContextsSql);

                // NO MORE SEED CODE HERE!
            }
        }

        // This is the function we will call when you press 'Enter'
        public static void SaveEntry(LogEntry entry)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                string insertSql = @"
                    INSERT INTO Entries (Timestamp, ContextName, Note) 
                    VALUES (@Timestamp, @ContextName, @Note)";

                // Dapper securely maps our C# LogEntry variables to the @SQL parameters
                connection.Execute(insertSql, entry);
            }
        }

        // Reads the latest 100 entries, sorted chronologically
        public static IEnumerable<LogEntry> GetRecentEntries()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                string querySql = @"
                    SELECT Id, Timestamp, ContextName, Note 
                    FROM Entries 
                    ORDER BY Timestamp ASC 
                    LIMIT 100";

                // Dapper maps the SQL columns directly to our LogEntry properties!
                return connection.Query<LogEntry>(querySql);
            }
        }

        public static IEnumerable<string> GetContexts()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                return connection.Query<string>("SELECT Name FROM Contexts ORDER BY Name ASC");
            }
        }

        public static void AddContext(string name)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                connection.Execute("INSERT OR IGNORE INTO Contexts (Name) VALUES (@Name)", new { Name = name });
            }
        }

        public static void DeleteContext(string name)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                connection.Execute("DELETE FROM Contexts WHERE Name = @Name", new { Name = name });

                connection.Execute("UPDATE Entries SET ContextName = 'General' WHERE ContextName = @Name", new { Name = name });
            }
        }

        public static void WipeDatabase()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // Completely destroy the tables
                connection.Execute("DROP TABLE IF EXISTS Entries");
                connection.Execute("DROP TABLE IF EXISTS Contexts");
            }

            // Rebuild the fresh skeleton and seed the default contexts
            InitializeDatabase();
        }
    }
}