using System.Windows;
using KeyMate.Core;

namespace KeyMate;

public partial class EditorWindow : Window
{
    private readonly Snippet original;
    private readonly IEnumerable<Snippet> existing;
    public Snippet? Result { get; private set; }
    public EditorWindow(Snippet? snippet, IEnumerable<Snippet> entries)
    {
        InitializeComponent(); original = snippet ?? new(); existing = entries;
        if (snippet is not null) Heading.Text = Title = "대치 항목 수정";
        ShortcutBox.Text = original.Shortcut; ExpansionBox.Text = original.Expansion;
        DescriptionBox.Text = original.Description; CaseBox.IsChecked = original.CaseSensitive;
        BoundaryBox.IsChecked = original.WordBoundary; Loaded += (_, _) => ShortcutBox.Focus();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var candidate = original with { Shortcut = ShortcutBox.Text.Trim(), Expansion = ExpansionBox.Text,
            Description = DescriptionBox.Text.Trim(), CaseSensitive = CaseBox.IsChecked == true, WordBoundary = BoundaryBox.IsChecked == true };
        var error = Rules.Validate(candidate, existing);
        if (error is not null) { ErrorText.Text = error; return; }
        Result = candidate; DialogResult = true;
    }
}
