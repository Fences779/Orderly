using System.Globalization;
using System.Windows;

namespace Orderly.App.Views;

public partial class SubmitPriceChangeRequestDialog : Window
{
    public SubmitPriceChangeRequestDialog(decimal currentPrice)
    {
        InitializeComponent();
        CurrentPriceText.Text = $"¥{currentPrice:F2}";
    }

    public decimal ProposedPrice { get; private set; }

    public string? Reason { get; private set; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var priceText = ProposedPriceTextBox.Text.Trim();
        if (!decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.CurrentCulture, out var proposedPrice)
            && !decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out proposedPrice))
        {
            System.Windows.MessageBox.Show(this, "建议新售价必须是有效金额。", "提交改价申请", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var reason = ReasonTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            System.Windows.MessageBox.Show(this, "改价原因不能为空。", "提交改价申请", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProposedPrice = proposedPrice;
        Reason = reason;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
