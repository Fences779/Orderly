using System.Windows;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class AddCustomerDialog : Window
{
    public AddCustomerDialog()
    {
        InitializeComponent();
        StatusComboBox.ItemsSource = Enum.GetValues<CustomerStatus>();
        StatusComboBox.SelectedItem = CustomerStatus.Active;
        PriorityComboBox.ItemsSource = Enum.GetValues<CustomerPriority>();
        PriorityComboBox.SelectedItem = CustomerPriority.Normal;
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public Customer Customer { get; private set; } = new();

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "客户姓名不能为空。", "新增客户", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameTextBox.Focus();
            return;
        }

        Customer = new Customer
        {
            Name = name,
            ContactHandle = ContactTextBox.Text.Trim(),
            SourcePlatform = SourceTextBox.Text.Trim(),
            Remark = RemarkTextBox.Text.Trim(),
            Status = StatusComboBox.SelectedItem is CustomerStatus status ? status : CustomerStatus.Active,
            Priority = PriorityComboBox.SelectedItem is CustomerPriority priority ? priority : CustomerPriority.Normal
        };

        DialogResult = true;
    }
}
