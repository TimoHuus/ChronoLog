using System;

namespace ChronoLog
{
    public class LogEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ContextName { get; set; } = string.Empty;
        private string _note = string.Empty;
        public string Note
        {
            get => _note;
            set
            {
                var incoming = value ?? string.Empty;
                // Detect and remove the #TODO token (case-insensitive), keep the rest of the note.
                var hasTodoToken = System.Text.RegularExpressions.Regex.IsMatch(incoming, "#TODO\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (hasTodoToken)
                {
                    incoming = System.Text.RegularExpressions.Regex.Replace(incoming, "#TODO\\b", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    incoming = incoming.Trim();
                }

                _note = incoming;
                UpdateDoneForTodo(hasTodoToken);
            }
        }
        // If null => not a TODO note. If false => TODO created but not acknowledged. If true => acknowledged (can't revert through API).
        public bool? Done { get; private set; }

        // Convenience property for UI: true when this entry is a TODO (regardless of acknowledged state)
        public bool IsTodo => Done.HasValue;
        public string? Color { get; set; }

        private void UpdateDoneForTodo(bool hasTodoToken)
        {
            if (hasTodoToken)
            {
                // If the note is a TODO and not already acknowledged, ensure Done is false so UI shows an unchecked box.
                if (Done != true)
                    Done = false;
            }
            else
            {
                // If the note no longer contains a TODO token, do not overwrite an already-acknowledged state.
                // If it wasn't acknowledged (false), clear the flag so it's treated as a normal note.
                if (Done == false)
                    Done = null;
            }
        }

        // Attempts to acknowledge the TODO. Returns true if state changed to acknowledged.
        // Only allowed when this entry is a TODO and not already acknowledged. Once acknowledged it cannot be undone.
        public bool TryAcknowledge()
        {
            if (Done == false)
            {
                Done = true;
                return true;
            }

            return false;
        }
    }
}