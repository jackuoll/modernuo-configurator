namespace ModernUOConfigurator.ViewModels;

public class ExpansionPreset
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string RawJson { get; init; } = "";

    public override string ToString() => Name;
}
