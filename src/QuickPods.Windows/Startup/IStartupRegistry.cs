namespace QuickPods.Windows.Startup;

public interface IStartupRegistry
{
    string? ReadCommand(string valueName);

    void WriteCommand(string valueName, string command);

    void DeleteCommand(string valueName);
}
