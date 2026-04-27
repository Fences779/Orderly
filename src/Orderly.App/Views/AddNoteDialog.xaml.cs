using System.Windows;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class AddNoteDialog : Window
{
    public AddNoteDialog(IEnumerable<ReplyTemplate>? templates = null)
    {
        InitializeComponent();
        TypeComboBox.ItemsSource = Enum.GetValues<NoteType>();
        TypeComboBox.SelectedItem = NoteType.General;
        TemplateComboBox.ItemsSource = templates?.ToList() ?? new List<ReplyTemplate>();
        Loaded += (_, _) => ContentTextBox.Focus();
    }

    public string NoteContent { get; private set; } = string.Empty;
    public NoteType SelectedNoteType { get; private set; } = NoteType.General;
    public ReplyTemplate? InsertedTemplate { get; private set; }

    private void InsertTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is not ReplyTemplate template || string.IsNullOrWhiteSpace(template.Content))
        {
            return;
        }

        var existing = ContentTextBox.Text.TrimEnd();
        ContentTextBox.Text = string.IsNullOrWhiteSpace(existing)
            ? template.Content
            : $"{existing}{Environment.NewLine}{template.Content}";
        InsertedTemplate = template;
        ContentTextBox.CaretIndex = ContentTextBox.Text.Length;
        ContentTextBox.Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var content = ContentTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            System.Windows.MessageBox.Show(this, "备注内容不能为空。", "新增备注", MessageBoxButton.OK, MessageBoxImage.Warning);
            ContentTextBox.Focus();
            return;
        }

        NoteContent = content;
        SelectedNoteType = TypeComboBox.SelectedItem is NoteType noteType ? noteType : NoteType.General;
        DialogResult = true;
    }
}
