namespace ZhongshengGasInspectionHmi.UI.Services;

public interface IModbusCommunicationLog
{
    event EventHandler<ModbusCommunicationLogEntry>? EntryAdded;

    event EventHandler? Cleared;

    IReadOnlyList<ModbusCommunicationLogEntry> Entries { get; }

    void Add(ModbusCommunicationLogEntry entry);

    void Clear();
}
