using System;

namespace ChronoLog
{
    public class LogEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ContextName { get; set; }
        public string Note { get; set; }
    }
}