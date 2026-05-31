using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class FulfillmentDetailShippingCard : System.Windows.Controls.UserControl
{
    public FulfillmentDetailShippingCard()
    {
        InitializeComponent();
    }

    private void FulfillmentInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (DataContext is MainViewModel vm && vm.UpdateStringNarrationFulfillmentCommand.CanExecute(null))
            {
                if (sender is System.Windows.Controls.TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
