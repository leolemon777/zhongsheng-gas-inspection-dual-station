namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class NullModbusCommunicationLog : IModbusCommunicationLog
{
    public static NullModbusCommunicationLog Instance { get; } = new();

    private NullModbusCommunicationLog()
    {
    }

    public event EventHandler<ModbusCommunicationLogEntry>? EntryAdded
    {
        add { }
        remove { }
    }

    public event EventHandler? Cleared
    {
        add { }
        remove { }
    }

    public IReadOnlyList<ModbusCommunicationLogEntry> Entries => [];

    public void Add(ModbusCommunicationLogEntry entry)
    {
    }

    public void Clear()
    {
    }
}
