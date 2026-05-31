using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using TBHStats.App.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private static IHost? _host;
    private Window? _window;

    /// <summary>
    /// Глобальная точка доступа к DI-контейнеру для Views/ViewModels.
    /// Доступна после завершения конструктора App, до OnLaunched.
    /// </summary>
    public static IServiceProvider Services
    {
        get
        {
            if (_host is null)
            {
                throw new InvalidOperationException(
                    "DI host is not initialized. Services must be accessed after App constructor completes.");
            }

            return _host.Services;
        }
    }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        _host = BuildHost();
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>
    /// Строит DI-хост. Регистрация сервисов вынесена в composition root
    /// <see cref="Composition.AddTbhStatsServices"/> (src/TBHStats.App/Services/Composition.cs).
    /// </summary>
    private static IHost BuildHost()
    {
        IHostBuilder builder = Host.CreateDefaultBuilder();

        builder.ConfigureServices(static (_, services) => services.AddTbhStatsServices());

        // Structured logging через Microsoft.Extensions.Logging; конкретные синки
        // (файл / ETW) добавляются в Polish-фазе (T047).
        builder.ConfigureLogging(static logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
        });

        return builder.Build();
    }
}
