using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class AddPriceAdjustmentDialog : Window
{
    public AddPriceAdjustmentDialog(decimal originalAmount)
    {
        InitializeComponent();
        var amountText = originalAmount.ToString("0.##", CultureInfo.CurrentCulture);
        OriginalAmountTextBox.Text = amountText;
        AdjustedAmountTextBox.Text = amountText;
        
        StatusComboBox.ItemsSource = System.Enum.GetValues(typeof(PriceAdjustmentStatus));
        StatusComboBox.SelectedItem = PriceAdjustmentStatus.PendingApproval;
        
        Loaded += (_, _) => AdjustedAmountTextBox.Focus();
    }

    public decimal OriginalAmount { get; private set; }
    public decimal AdjustedAmount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public PriceAdjustmentStatus Status { get; private set; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseAmount(OriginalAmountTextBox.Text, out var originalAmount))
        {
            System.Windows.MessageBox.Show(this, "原价必须是有效金额。", "新增改价", MessageBoxButton.OK, MessageBoxImage.Warning);
            OriginalAmountTextBox.Focus();
            return;
        }

        if (!TryParseAmount(AdjustedAmountTextBox.Text, out var adjustedAmount))
        {
            System.Windows.MessageBox.Show(this, "调整后价格必须是有效金额。", "新增改价", MessageBoxButton.OK, MessageBoxImage.Warning);
            AdjustedAmountTextBox.Focus();
            return;
        }

        var reason = ReasonTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            System.Windows.MessageBox.Show(this, "改价原因不能为空。", "新增改价", MessageBoxButton.OK, MessageBoxImage.Warning);
            ReasonTextBox.Focus();
            return;
        }

        OriginalAmount = originalAmount;
        AdjustedAmount = adjustedAmount;
        Reason = reason;
        Status = (PriceAdjustmentStatus)StatusComboBox.SelectedItem;
        DialogResult = true;
    }

    private static bool TryParseAmount(string text, out decimal amount)
    {
        return decimal.TryParse(
            text.Trim(),
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
            CultureInfo.CurrentCulture,
            out amount);
    }
}
