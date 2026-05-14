using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ChronoLog
{
    public static class DatabaseHelper
    {
        // Simple palette of visually distinct colors (hex)
        private static readonly string[] ColorPalette = new[]
        {
            "#FF6B6B",
            "#FFD93D",
            "#6BCB77",
            "#4D96FF",
            "#9B59B6",
            "#FF7BAC",
            "#FF9F1C",
            "#2EC4B6",
            "#8ECAE6",
            "#E76F51",
            "#A0C4FF",
            "#BDB2FF"
        };

        private static string AllocateColor(string name, IEnumerable<string> usedColors)
        {
            var used = new HashSet<string>(usedColors ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            // Choose first unused from palette
            foreach (var c in ColorPalette)
            {
                if (!used.Contains(c)) return c;
            }

            // Fallback: hash the name into palette
            int idx = Math.Abs(name.GetHashCode()) % ColorPalette.Length;
            return ColorPalette[idx];
        }
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

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // 1. Entries table (Remains the same)
                string createEntriesSql = @"
            CREATE TABLE IF NOT EXISTS Entries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                ContextName TEXT,
                Note TEXT NOT NULL,
                Done INTEGER
            );";
                connection.Execute(createEntriesSql);

                // 2. MODIFIED Contexts table
                // We add 'Id' as the PK and make 'Name' UNIQUE so you can't have duplicates.
                string createContextsSql = @"
            CREATE TABLE IF NOT EXISTS Contexts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT UNIQUE,
                Color TEXT
            );";
                connection.Execute(createContextsSql);

                // (The PRAGMA color check is no longer strictly needed if you Factory Reset, 
                // but we'll leave the Archive logic below)

                // 3. Ensure 'Archive' exists
                // We use INSERT OR IGNORE based on the Name (which is now UNIQUE)
                connection.Execute("INSERT OR IGNORE INTO Contexts (Name, Color) VALUES (@Name, @Color)",
                    new { Name = "Archive", Color = (string?)null });

                connection.Execute("UPDATE Contexts SET Color = NULL WHERE Name = 'Archive'");
            }
        }

        // This is the function we will call when you press 'Enter'
        public static void SaveEntry(LogEntry entry)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // Ensure Done column exists (for older DBs)
                var cols = connection.Query<dynamic>("PRAGMA table_info('Entries')");
                bool hasDone = false;
                foreach (var c in cols)
                {
                    if (c.name == "Done") { hasDone = true; break; }
                }
                if (!hasDone)
                {
                    connection.Execute("ALTER TABLE Entries ADD COLUMN Done INTEGER");
                }

                string insertSql = @"
                    INSERT INTO Entries (Timestamp, ContextName, Note, Done) 
                    VALUES (@Timestamp, @ContextName, @Note, @Done)";

                // Store timestamp in ISO8601 to ensure correct lexical ordering
                var parameters = new
                {
                    Timestamp = entry.Timestamp.ToString("o"),
                    ContextName = entry.ContextName,
                    Note = entry.Note,
                    Done = entry.Done.HasValue ? (entry.Done.Value ? 1 : 0) : (int?)null
                };

                connection.Execute(insertSql, parameters);
            }
        }

        // Reads the latest 100 entries, sorted chronologically
        public static IEnumerable<LogEntry> GetRecentEntries()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                string querySql = @"
                    SELECT e.Id, e.Timestamp, e.ContextName, e.Note, c.Color as Color
                    FROM Entries e
                    LEFT JOIN Contexts c ON e.ContextName = c.Name
                    ORDER BY e.Timestamp ASC
                    LIMIT 100";

                // If the DB has a Done column, include it in the selection so Dapper maps it.
                var cols = connection.Query<dynamic>("PRAGMA table_info('Entries')");
                bool hasDone = false;
                foreach (var c in cols)
                {
                    if (c.name == "Done") { hasDone = true; break; }
                }

                if (hasDone)
                {
                    querySql = @"
                    SELECT e.Id, e.Timestamp, e.ContextName, e.Note, e.Done, c.Color as Color
                    FROM Entries e
                    LEFT JOIN Contexts c ON e.ContextName = c.Name
                    ORDER BY e.Timestamp ASC
                    LIMIT 100";
                }

                // Dapper maps the SQL columns directly to our LogEntry properties!
                var results = connection.Query<LogEntry>(querySql);

                return results;
            }
        }

        public static void UpdateEntryDone(int id, bool done)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // Ensure column exists
                var cols = connection.Query<dynamic>("PRAGMA table_info('Entries')");
                bool hasDone = false;
                foreach (var c in cols)
                {
                    if (c.name == "Done") { hasDone = true; break; }
                }
                if (!hasDone)
                {
                    connection.Execute("ALTER TABLE Entries ADD COLUMN Done INTEGER");
                }

                connection.Execute("UPDATE Entries SET Done = @Done WHERE Id = @Id", new { Done = done ? 1 : 0, Id = id });
            }
        }

        public class ContextRecord
        {
            public int Id { get; set; } // Add this property
            public string Name { get; set; } = string.Empty;
            public string? Color { get; set; }
        }

        public static IEnumerable<ContextRecord> GetContexts()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var contexts = connection.Query<ContextRecord>("SELECT Id, Name, Color FROM Contexts ORDER BY Id ASC");

                // If any context has null Color, assign one from the palette and persist it
                // Build a set of used colors, but do NOT consider the built-in 'Archive' context.
                // Archive should intentionally have no color (so it appears neutral in the UI).
                var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in contexts)
                {
                    if (c.Name == "Archive") continue;
                    if (!string.IsNullOrEmpty(c.Color)) usedColors.Add(c.Color);
                }

                // Assign colors only to non-Archive contexts that are missing a color.
                foreach (var c in contexts)
                {
                    if (c.Name == "Archive") continue;
                    if (string.IsNullOrEmpty(c.Color))
                    {
                        string assigned = AllocateColor(c.Name, usedColors);
                        c.Color = assigned;
                        usedColors.Add(assigned);
                        connection.Execute("UPDATE Contexts SET Color = @Color WHERE Name = @Name", new { Color = assigned, Name = c.Name });
                    }
                }

                return contexts;
            }
        }

        public static string AddContext(string name)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                // Determine used colors
                var used = connection.Query<string>("SELECT Color FROM Contexts WHERE Color IS NOT NULL");
                string assigned = AllocateColor(name, used);

                connection.Execute("INSERT OR IGNORE INTO Contexts (Name, Color) VALUES (@Name, @Color)", new { Name = name, Color = assigned });
                return assigned;
            }
        }

        public static void DeleteContext(string name)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                connection.Execute("DELETE FROM Contexts WHERE Name = @Name", new { Name = name });

                // Move entries into the 'Archive' bucket when their context is deleted
                connection.Execute("UPDATE Entries SET ContextName = 'Archive' WHERE ContextName = @Name", new { Name = name });
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