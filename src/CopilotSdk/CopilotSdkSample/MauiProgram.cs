using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.CopilotSdk;

namespace CopilotSdkSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register the Copilot SDK IChatClient
        builder.Services.AddCopilotSdkChatClient(config =>
        {
            config.Model = "gpt-4.1";
            config.UseLoggedInUser = true;
            config.CliPath = "/opt/homebrew/bin/copilot";
            config.SystemMessage = "You are a helpful assistant. Be concise.";
        });

        // Register sample tools
        builder.Services.AddSingleton<IList<AITool>>(sp =>
        [
            AIFunctionFactory.Create(
                ([Description("The city name")] string city) =>
                    $"Weather in {city}: 22°C, Sunny with light clouds.",
                name: "get_weather",
                description: "Get the current weather for a city"),
            AIFunctionFactory.Create(
                () => DateTime.Now.ToString("F"),
                name: "get_current_time",
                description: "Get the current date and time"),
        ]);

        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
