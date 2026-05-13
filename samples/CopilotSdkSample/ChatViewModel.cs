using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace CopilotSdkSample;

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _tools;
    private bool _isBusy;

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }

    public bool IsNotBusy => !_isBusy;

    public ChatViewModel(IChatClient chatClient, IList<AITool> tools)
    {
        _chatClient = chatClient;
        _tools = tools;
    }

    public async Task SendMessageAsync(string text)
    {
        if (IsBusy) return;

        Messages.Add(ChatMessageItem.User(text));
        IsBusy = true;

        try
        {
            var assistantItem = ChatMessageItem.Assistant("");
            Messages.Add(assistantItem);

            await foreach (var update in _chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, text)],
                new ChatOptions { Tools = _tools }))
            {
                foreach (var content in update.Contents.OfType<TextContent>())
                    assistantItem.Text += content.Text;
            }

            if (string.IsNullOrWhiteSpace(assistantItem.Text))
                assistantItem.Text = "(no response)";
        }
        catch (Exception ex)
        {
            Messages.Add(ChatMessageItem.Assistant($"Error: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ChatMessageItem : INotifyPropertyChanged
{
    private string _text = "";

    public bool IsUser { get; init; }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    public Color BackgroundColor => IsUser
        ? Color.FromArgb("#512BD4")
        : Color.FromArgb("#E0E0E0");

    public Color TextColor => IsUser
        ? Colors.White
        : Colors.Black;

    public LayoutOptions Alignment => IsUser
        ? LayoutOptions.End
        : LayoutOptions.Start;

    public static ChatMessageItem User(string text) => new() { Text = text, IsUser = true };
    public static ChatMessageItem Assistant(string text) => new() { Text = text, IsUser = false };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
