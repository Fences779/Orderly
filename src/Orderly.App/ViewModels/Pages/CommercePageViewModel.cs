using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Orderly.App.ViewModels.Pages;

/// <summary>
/// Shared base for the dedicated per-page ViewModels of the nine-entry commerce shell (Req 7.1).
/// Each concrete page ViewModel obtains its business data <b>only</b> through the Commerce Service
/// Layer (Req 7.3, 7.4) — there is no legacy remote call and no legacy aggregation binding.
///
/// <para>The base owns the page-level load lifecycle and the error/empty surface the UI binds to
/// (Req 6.13, 6.14, 7.5): <see cref="IsLoading"/>, <see cref="HasError"/>,
/// <see cref="ErrorMessage"/>, <see cref="IsLoaded"/>, and <see cref="IsEmpty"/>. On a service
/// failure the load surfaces an error and retains the last known valid state without throwing or
/// falling back to any legacy service (Req 7.5). Wiring these states into the page bindings is
/// finished by task 19.3.</para>
/// </summary>
public abstract partial class CommercePageViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _hasError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _isLoaded;

    /// <summary>Creates the base and its <see cref="RefreshCommand"/>.</summary>
    protected CommercePageViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
    }

    /// <summary>
    /// A stable, neutral key identifying which navigation page this ViewModel backs. Matches the
    /// Chinese section keys declared on <see cref="MainViewModel"/>.
    /// </summary>
    public abstract string PageKey { get; }

    /// <summary>Re-runs <see cref="LoadAsync"/> from the UI (Req 6.13 retry affordance).</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// <c>true</c> when the page has loaded successfully without error and the Commerce service
    /// returned no rows, so the UI shows an explicit empty-state rather than a blank region (Req 6.14).
    /// </summary>
    public bool IsEmpty => IsLoaded && !IsLoading && !HasError && HasNoData;

    /// <summary>
    /// <c>true</c> when the page has loaded successfully and has displayable rows, so the UI shows
    /// its data region rather than a loading, error, or empty-state surface (Req 6.13, 6.14).
    /// </summary>
    public bool ShowContent => IsLoaded && !IsLoading && !HasError && !HasNoData;

    /// <summary>
    /// <c>true</c> while a load is in flight, so the UI shows its loading surface. Mirrors
    /// <see cref="IsLoading"/> as a binding-friendly name alongside the other state flags.
    /// </summary>
    public bool ShowLoading => IsLoading;

    /// <summary>
    /// Whether the most recent successful load produced no displayable rows. Concrete pages compute
    /// this from their loaded Commerce data.
    /// </summary>
    protected abstract bool HasNoData { get; }

    /// <summary>
    /// Loads the page's data through its Commerce service(s). Wraps <see cref="LoadCoreAsync"/> with
    /// the shared loading/error lifecycle: a service failure is captured into <see cref="HasError"/>
    /// and <see cref="ErrorMessage"/> while the last known valid state is retained (Req 7.5, 6.13);
    /// the application is never terminated and no legacy service is invoked as a fallback.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowContent));

        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(true);
            IsLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // A cancelled load leaves the last known valid state untouched.
        }
        catch (System.Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowContent));
        }
    }

    /// <summary>
    /// Performs the page-specific data load through the Commerce Service Layer. Implementations must
    /// not swallow service failures: any exception propagates to <see cref="LoadAsync"/>, which turns
    /// it into the page error state.
    /// </summary>
    protected abstract Task LoadCoreAsync(CancellationToken cancellationToken);

    /// <summary>Raises a change notification for <see cref="IsEmpty"/> after data is replaced.</summary>
    protected void NotifyEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowContent));
    }
}
