using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class FulfillmentDetailStepper : System.Windows.Controls.UserControl
{
    public FulfillmentDetailStepper()
    {
        InitializeComponent();
    }

    private void StepperNode_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement element && DataContext is MainViewModel vm)
        {
            var targetStatus = element.Tag as string;
            if (!string.IsNullOrEmpty(targetStatus) && vm.SelectedStringNarrationOrderDetail is not null)
            {
                vm.StringNarrationFulfillmentStatusInput = targetStatus;
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }
}
