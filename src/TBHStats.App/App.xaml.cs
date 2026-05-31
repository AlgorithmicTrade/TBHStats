using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using TBHStats.App.Services;
using TBHStats.App.Services.Logging;
using TBHStats.App.ViewModels;
using TBHStats.Data;
using TBHStats_App.Views;

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

        // Глобальный перехват необработанных исключений UI-потока: логируем полный стек и
        // НЕ даём одному сбою (например, в окне калибровки) уронить весь виджет.
        UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            ILogger<App> logger = Services.GetRequiredService<ILogger<App>>();
            logger.LogError(e.Exception, "Необработанное исключение UI-потока: {Message}", e.Message);
        }
        catch
        {
            // логгер недоступен — глотать нельзя молча, но и падать из обработчика нельзя
        }

        // Помечаем обработанным, чтобы процесс не завершался аварийно.
        e.Handled = true;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI требует активировать окно синхронно в OnLaunched.
        // Создаём виджет и активируем немедленно; init БД + старт оркестратора — асинхронно после.
        _window = new WidgetWindow();
        _window.Activate();

        // Фоновая инициализация: миграции БД + сидинг + старт оркестратора.
        _ = InitializeAsync();
    }

    /// <summary>
    /// Инициализирует БД (миграции + сидинг) и запускает фоновый оркестратор.
    /// Вызывается асинхронно после активации WidgetWindow.
    /// </summary>
    private static async Task InitializeAsync()
    {
        ILogger<App> logger = Services.GetRequiredService<ILogger<App>>();

        // 1. Применить миграции EF Core и сидинг игровых механик.
        try
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            TbhStatsDbContext db = scope.ServiceProvider.GetRequiredService<TbhStatsDbContext>();
            await DatabaseInitializer.InitializeAsync(db).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка инициализации базы данных.");
        }

        // 2. Запустить фоновую петлю оркестратора.
        try
        {
            await Services
                .GetRequiredService<IStatsOrchestrator>()
                .StartAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка запуска StatsOrchestrator.");
        }
    }

    /// <summary>
    /// Возвращает путь к директории логов TBHStats.
    /// Базовый каталог совпадает с каталогом БД: <c>%LOCALAPPDATA%\TBHStats\logs\</c>.
    /// Директория создаётся <see cref="FileLoggerProvider"/> при первой записи.
    /// </summary>
    private static string GetLogDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TBHStats", "logs");
    }

    /// <summary>
    /// Строит DI-хост. Регистрация сервисов вынесена в composition root
    /// <see cref="Composition.AddTbhStatsServices"/> (src/TBHStats.App/Services/Composition.cs).
    /// </summary>
    private static IHost BuildHost()
    {
        IHostBuilder builder = Host.CreateDefaultBuilder();

        builder.ConfigureServices(static (_, services) => services.AddTbhStatsServices());

        // Structured logging: Debug-синк (для разработки) + локальный файловый синк (T047).
        // Файловый синк: %LOCALAPPDATA%\TBHStats\logs\tbhstats-YYYY-MM-DD.log
        // Минимальный уровень для файла — Information (Debug в файл не пишем, чтобы не раздувать).
        // Путь к лог-директории не логируется на уровне Info/Warning во избежание PII (username в пути).
        string logDirectory = GetLogDirectory();
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddDebug();
            logging.SetMinimumLevel(LogLevel.Debug);

            // Вычисляем каталог логов от того же базового каталога, что и БД (%LOCALAPPDATA%\TBHStats\).
            logging.AddProvider(new FileLoggerProvider(logDirectory, LogLevel.Information));
        });

        return builder.Build();
    }
}
