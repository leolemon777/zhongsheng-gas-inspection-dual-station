namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class ModbusCommunicationLog : IModbusCommunicationLog
{
    private const int MaxEntries = 120;
    private readonly object _lock = new();
    private readonly List<ModbusCommunicationLogEntry> _entries = [];

    public event EventHandler<ModbusCommunicationLogEntry>? EntryAdded;

    public event EventHandler? Cleared;

    public IReadOnlyList<ModbusCommunicationLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Add(ModbusCommunicationLogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }
}
