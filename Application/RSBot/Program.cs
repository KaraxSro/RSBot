using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommandLine;
using CommandLine.Text;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Views;

namespace RSBot;

internal static class Program
{
    public static string AssemblyTitle = Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyProductAttribute>()
        ?.Product;

    public static string AssemblyVersion = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    public static string AssemblyDescription = Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyDescriptionAttribute>()
        ?.Description;

    public class CommandLineOptions
    {
        [Option('c', "character", Required = false, HelpText = "Set the character name to use.")]
        public string Character { get; set; }

        [Option('p', "profile", Required = false, HelpText = "Set the profile name to use.")]
        public string Profile { get; set; }

        [Option("launch-client", Required = false, HelpText = "Start with client")]
        public bool LaunchClient { get; set; }

        [Option("launch-clientless", Required = false, HelpText = "Start clientless")]
        public bool LaunchClientless { get; set; }
    }

    private static void DisplayHelp(ParserResult<CommandLineOptions> result)
    {
        var helpText = HelpText.AutoBuild(
            result,
            h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.AddDashesToOption = true;
                return HelpText.DefaultParsingErrorsHandler(result, h);
            }
        );
        MessageBox.Show(
            helpText,
            AssemblyTitle + " " + AssemblyVersion,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    [STAThread]
    private static void Main(string[] args)
    {
        RegisterUnhandledExceptionLogging();

        var parser = new Parser(with => with.HelpWriter = Console.Out);
        var parserResult = parser.ParseArguments<CommandLineOptions>(args);

        parserResult
            .WithParsed(options =>
            {
                RunOptions(options);
            })
            .WithNotParsed(errs =>
            {
                DisplayHelp(parserResult);
                Environment.Exit(1);
            });

        //CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // We need "." instead of "," while saving float numbers
        // Also client data is "." based float digit numbers
        CultureInfo.CurrentCulture = new CultureInfo("en-US");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        using Main mainForm = new Main();
        EventManager.ConfigureUiThread(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        using SplashScreen splashScreen = new(mainForm);

        splashScreen.ShowDialog();

        splashScreen.Dispose();
        Application.Run(mainForm);
    }

    private static void RegisterUnhandledExceptionLogging()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            Log.Fatal(new Exception("Unhandled Windows Forms thread exception", eventArgs.Exception));
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var exception = eventArgs.ExceptionObject as Exception
                ?? new Exception($"Unhandled non-Exception object: {eventArgs.ExceptionObject}");
            Log.Fatal(
                new Exception(
                    $"Unhandled AppDomain exception; terminating={eventArgs.IsTerminating}",
                    exception
                )
            );
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Fatal(new Exception("Unobserved background task exception", eventArgs.Exception));
            eventArgs.SetObserved();
        };
    }

    private static void RunOptions(CommandLineOptions options)
    {
        if (options.LaunchClient)
        {
            Kernel.LaunchMode = "client";
            Log.Debug("Launching with client dictated by launch paramaters");
        }
        else if (options.LaunchClientless)
        {
            Kernel.LaunchMode = "clientless";
            Log.Debug("Launching client as clientless dictated by launch paramaters");
        }

        if (!string.IsNullOrEmpty(options.Profile))
        {
            var profile = options.Profile;
            if (ProfileManager.ProfileExists(profile))
                ProfileManager.SetSelectedProfile(profile);
            else
                ProfileManager.Add(profile);

            ProfileManager.IsProfileLoadedByArgs = true;
            Log.Debug($"Selected profile by args: {profile}");
        }

        if (!string.IsNullOrEmpty(options.Character))
        {
            var character = options.Character;
            ProfileManager.SelectedCharacter = character;
            Log.Debug($"Selected character by args: {character}");
        }
    }
}
