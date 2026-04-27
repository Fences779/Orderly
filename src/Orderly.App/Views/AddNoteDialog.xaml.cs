using System.Windows;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class AddNoteDialog : Window
{
    public AddNoteDialog()
    {
        InitializeComponent();
        TypeComboBox.ItemsSource = Enum.GetValues<NoteType>();
        TypeComboBox.SelectedItem = NoteType.General;
        Loaded += (_, _) => ContentTextBox.Focus();
    }

    public string NoteContent { get; private set; } = string.Empty;
    public NoteType SelectedNoteType { get; private set; } = NoteType.General;

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
