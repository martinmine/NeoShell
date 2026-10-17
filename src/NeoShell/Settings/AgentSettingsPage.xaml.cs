using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NeoShell.Agents;
using NeoShell.Taskbar;

namespace NeoShell.Settings;

/// <summary>Agent access: whether agents may connect, what they may do, who is connected, and how to connect one.</summary>
internal sealed partial class AgentSettingsPage : SettingsPageView
{
    public AgentSettingsPage(Taskbars taskbars)
        : base(taskbars)
    {
        InitializeComponent();
        CommandBox.Text = SettingsPages.ClaudeCodeCommand(Environment.ProcessPath ?? "NeoShell.exe");
        Loaded += (_, _) =>
        {
            Taskbars.AgentsChanged += ShowConnections;
            ShowConnections();
        };
        Unloaded += (_, _) => Taskbars.AgentsChanged -= ShowConnections;
    }

    protected override void Load()
    {
        AccessToggle.IsOn = Current.AgentAccessEnabled;
        ReadToggle.IsOn = Current.AgentReadAllowed;
        ControlToggle.IsOn = Current.AgentControlAllowed;
        ScreenToggle.IsOn = Current.AgentScreenAllowed;
        PresenceToggle.IsOn = Current.AgentPresenceAllowed;
        foreach (ToggleSwitch toggle in (ToggleSwitch[])[ReadToggle, ControlToggle, ScreenToggle, PresenceToggle])
            toggle.IsEnabled = Current.AgentAccessEnabled;
    }

    private void ShowConnections()
    {
        IReadOnlyList<AgentConnection> connections = Taskbars.Agents?.Connections ?? [];
        ConnectionList.Children.Clear();
        foreach (AgentConnection connection in connections)
        {
            McpSession session = connection.Session;
            var disconnect = new Button
            {
                Content = "Disconnect",
                MinWidth = 120,
                IsEnabled = !session.IsRevoked,
            };
            AutomationProperties.SetAutomationId(disconnect, "AgentDisconnectButton");
            AutomationProperties.SetName(disconnect, $"Disconnect {session.ClientName}");
            disconnect.Click += (_, _) => Taskbars.Agents?.Disconnect(connection);
            ConnectionList.Children.Add(new SettingsCard
            {
                Glyph = "",
                Header = session.ClientName,
                Description = AgentConnection.Describe(connection.Agent, connection.Since, session.IsRevoked),
                Content = disconnect,
            });
        }
        NoConnectionsText.Text = SettingsPages.NoAgentsText(Current.AgentAccessEnabled);
        NoConnectionsText.Visibility = connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AccessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (IsFilling)
            return;
        Save(Current with { AgentAccessEnabled = AccessToggle.IsOn });
        // A page's own saves don't fill it again: the group switches follow the main one here.
        Reload();
        ShowConnections();
    }

    private void GroupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsFilling)
        {
            Save(Current with
            {
                AgentReadAllowed = ReadToggle.IsOn,
                AgentControlAllowed = ControlToggle.IsOn,
                AgentScreenAllowed = ScreenToggle.IsOn,
                AgentPresenceAllowed = PresenceToggle.IsOn,
            });
        }
    }
}
