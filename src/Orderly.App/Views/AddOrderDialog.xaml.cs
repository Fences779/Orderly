using System.Globalization;
using System.Windows;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class AddOrderDialog : Window
{
    public AddOrderDialog(IEnumerable<Customer> customers, Customer? selectedCustomer)
    {
        InitializeComponent();
        var customerList = customers.ToList();
        CustomerComboBox.ItemsSource = customerList;
        CustomerComboBox.SelectedItem = selectedCustomer is null
            ? customerList.FirstOrDefault()
            : customerList.FirstOrDefault(customer => customer.Id == selectedCustomer.Id) ?? customerList.FirstOrDefault();
        StatusComboBox.ItemsSource = Enum.GetValues<OrderStatus>();
        StatusComboBox.SelectedItem = OrderStatus.PendingCommunication;
        NextFollowUpTextBox.Text = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        Loaded += (_, _) => TitleTextBox.Focus();
    }

    public Customer? SelectedCustomer { get; private set; }
    public string OrderTitle { get; private set; } = string.Empty;
    public string Requirement { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PendingCommunication;
    public DateTime? NextFollowUpAt { get; private set; }
    public string Remark { get; private set; } = string.Empty;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (CustomerComboBox.SelectedItem is not Customer customer)
        {
            System.Windows.MessageBox.Show(this, "请选择客户。", "创建订单", MessageBoxButton.OK, MessageBoxImage.Warning);
            CustomerComboBox.Focus();
            return;
        }

        var title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            System.Windows.MessageBox.Show(this, "订单标题不能为空。", "创建订单", MessageBoxButton.OK, MessageBoxImage.Warning);
            TitleTextBox.Focus();
            return;
        }

        var amountText = AmountTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(amountText) &&
            !decimal.TryParse(amountText, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture, out var amount))
        {
            System.Windows.MessageBox.Show(this, "金额必须是有效数字。", "创建订单", MessageBoxButton.OK, MessageBoxImage.Warning);
            AmountTextBox.Focus();
            return;
        }

        DateTime? nextFollowUpAt = null;
        var nextFollowUpText = NextFollowUpTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(nextFollowUpText))
        {
            if (!DateTime.TryParse(nextFollowUpText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsedFollowUpAt))
            {
                System.Windows.MessageBox.Show(this, "下次跟进时间格式不正确。", "创建订单", MessageBoxButton.OK, MessageBoxImage.Warning);
                NextFollowUpTextBox.Focus();
                return;
            }

            nextFollowUpAt = parsedFollowUpAt;
        }

        SelectedCustomer = customer;
        OrderTitle = title;
        Requirement = RequirementTextBox.Text.Trim();
        Amount = string.IsNullOrWhiteSpace(amountText)
            ? 0
            : decimal.Parse(amountText, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.CurrentCulture);
        Status = StatusComboBox.SelectedItem is OrderStatus status ? status : OrderStatus.PendingCommunication;
        NextFollowUpAt = nextFollowUpAt;
        Remark = RemarkTextBox.Text.Trim();
        DialogResult = true;
    }
}
