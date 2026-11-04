namespace RightMenuManager.Models;

public sealed class ScopeNavItem : ObservableObject
{
    public ScopeNavItem(ScopeDefinition definition)
    {
        Definition = definition;
    }

    public ScopeDefinition Definition { get; }

    public MenuScope Scope => Definition.Scope;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string Glyph => Definition.Glyph;

    private int _count;
    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }
}
