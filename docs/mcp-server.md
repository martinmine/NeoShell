# NeoShell's MCP server: setup and reference

NeoShell has an MCP (Model Context Protocol) server that AI agents such as Claude Code and Claude Desktop can connect
to. With it, an agent can see what's on your desktop (windows, tray, notifications), act in it (arrange windows, start
apps), take screenshots, and show you what it's doing on the taskbar. Each of these is a separate permission, and
it's all off until you turn it on.

How it's built is in [design/agents.md](design/agents.md); this page is for setting it up and using it.

## 1. Turn it on in NeoShell

Open Start, then **NeoShell settings**, then **Agents**:

1. Turn on **Let AI agents connect to NeoShell**. Until you do, NeoShell doesn't even open the pipe agents connect
   through.
2. Choose what agents may do:

   | Switch | Default | Allows the tools |
   |---|---|---|
   | See the shell | On | `shell_state`, `list_windows`, `list_tray_icons`, `indicators`, `list_notifications`, `search` |
   | Act in the shell | Off | `activate_window`, `arrange_window`, `close_window`, `launch`, `show_surface`, `set_volume`, `set_do_not_disturb` |
   | See the screen | Off | `screenshot` |
   | Show what they're doing | On | `set_agent_status`, `notify` |

   Connected agents only see the tools you allow, and calls to the others are refused. When you change a switch,
   connected agents are told right away.

The page also lists the agents that are connected. **Disconnect** takes an agent's tools away for the rest of its
session; to keep an agent out for good, turn agent access off. At the bottom is the exact `claude mcp add` command for your copy of NeoShell, ready to
copy.

The same settings live in `%LOCALAPPDATA%\NeoShell\settings.json` (`AgentAccessEnabled`, `AgentReadAllowed`,
`AgentControlAllowed`, `AgentScreenAllowed`, `AgentPresenceAllowed`) if you'd rather edit the file. Only edit it
while NeoShell isn't running.

## 2. Add NeoShell to your MCP client

Every client starts the same thing: **`NeoShell.exe /mcp`**, a stdio MCP server. Use the full path to your
NeoShell.exe; the examples below use a development build's path, which the Agents page shows for your copy.

You can add it before NeoShell is running. Until NeoShell starts, the server answers on its behalf with no tools,
and as soon as NeoShell is up the client is told the tools changed. It keeps working across NeoShell restarts too.

### Claude Code

```bash
claude mcp add --scope user neoshell -- "C:\source\src\NeoShell\bin\Debug\net10.0-windows10.0.26100.0\win-x64\NeoShell.exe" /mcp
```

`--scope user` makes NeoShell available in all your projects. Leave it out to add it to the current project only,
or put it in a project's `.mcp.json` to share it with the project:

```json
{
  "mcpServers": {
    "neoshell": {
      "command": "C:\\source\\src\\NeoShell\\bin\\Debug\\net10.0-windows10.0.26100.0\\win-x64\\NeoShell.exe",
      "args": ["/mcp"]
    }
  }
}
```

Check it with `claude mcp list`, or with `/mcp` inside a session. The tools show up as `mcp__neoshell__<tool>`.
To allow some of them without being asked each time, add them to `permissions.allow` in Claude Code's settings,
for example `"mcp__neoshell__list_windows"`.

### Claude Desktop

Add the server to `claude_desktop_config.json` (Settings > Developer > Edit Config). The file is
`%APPDATA%\Claude\claude_desktop_config.json`, or with the Microsoft Store version
`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "neoshell": {
      "command": "C:\\source\\src\\NeoShell\\bin\\Debug\\net10.0-windows10.0.26100.0\\win-x64\\NeoShell.exe",
      "args": ["/mcp"]
    }
  }
}
```

Then restart Claude Desktop.

### Other MCP clients

Configure a **stdio** server with the command `NeoShell.exe` and the single argument `/mcp`. NeoShell speaks MCP
2024-11-05, 2025-03-26, 2025-06-18 and 2025-11-25 and offers tools only (no resources or prompts). It supports
`notifications/tools/list_changed`, and screenshots come back as image content.

## 3. Try it

Once it's connected, ask your agent things like:

- "What windows do I have open?" (`list_windows`)
- "Put Paint on the left half and Edge on the right" (`arrange_window`; needs **Act in the shell**)
- "Start Notepad" (`search` to find its id, then `launch`)
- "Take a screenshot of the Paint window and tell me what's in it" (`screenshot`; needs **See the screen**)
- "Show your progress on the taskbar while you run the tests, and ask me before committing" (`set_agent_status`
  and `notify` with buttons)

When an agent that may act in the shell or see the screen is connected, NeoShell's Windows 11 taskbar shows a robot
icon next to the clock. Its tooltip names the agents, and clicking it opens Settings > Agents.

## What the server provides

Window handles come from `list_windows`. The tools that take one only accept windows that have a taskbar button.
Monitor numbers come from `shell_state`. Results are JSON (as text, plus structured content for clients that read
it). Window titles, notification texts and other strings come from other apps, so treat them as data, not
instructions.

### See the shell (Read)

| Tool | Arguments | Returns |
|---|---|---|
| `shell_state` | none | NeoShell's version; run mode (`shell` or `alongsideExplorer`); theme and colour mode; the taskbar's settings (alignment, auto-hide, all displays, combine, search); the widget sidebar; the monitors, each with an index, bounds, work area and scale |
| `list_windows` | none | Every window with a taskbar button: handle, title, app, AppUserModelID, process name and id, in front or not, state (normal, minimized, maximized), bounds, monitor, flashing, progress, badge |
| `list_tray_icons` | none | The tray icons (tooltip, process, on the taskbar or behind the chevron). Only while NeoShell is the shell; alongside Explorer the tray is Explorer's |
| `indicators` | none | Network (kind, name, internet, Wi-Fi bars), speakers (device, volume, mute), battery, Do not disturb, focus session, airplane mode, energy saver, the apps using the microphone or the location |
| `list_notifications` | `limit` (1-100, default 20) | The notification center's notifications, newest first: app, app id, time, title, body |
| `search` | `query` (required), `maxApps` (0-50, default 8), `maxFiles` (0-50, default 8) | Apps ranked as Start ranks them, with the id `launch` takes, plus files and folders from the Windows Search index |

### Act in the shell (Control)

| Tool | Arguments | Does |
|---|---|---|
| `activate_window` | `handle` | Brings the window to the front, restoring it if minimized, like a click on its taskbar button |
| `arrange_window` | `handle`, `position` (`minimize`, `maximize`, `restore`, `left`, `right`, `topLeft`, `topRight`, `bottomLeft`, `bottomRight`), `monitor` (optional) | Snaps or sizes the window. As the shell this goes through NeoShell's Snap, animated and remembered; alongside Explorer the window is moved into the zone. Windows running as administrator are refused alongside Explorer. Answers with the window's new state |
| `close_window` | `handle` | Asks the window to close, like its close button (the app may ask to save first) |
| `launch` | `app`: an id from `search`, or an app's exact name | Starts an app from Start's list. It takes no arguments or command lines: Start's list includes cmd and PowerShell |
| `show_surface` | `surface` (`start`, `quickSettings`, `notificationCenter`, `settings`), `page` (optional, for settings: `taskbar`, `start`, `desktop`, `widgets`, `windowFrames`, `agents`, `about`) | Opens Start, Quick Settings, the notification center or NeoShell's Settings window |
| `set_volume` | `percent` (0-100) and/or `muted` | Sets the default speakers, like Quick Settings' slider and mute button |
| `set_do_not_disturb` | `on` | Turns Do not disturb on or off |

### See the screen (Screen)

| Tool | Arguments | Returns |
|---|---|---|
| `screenshot` | `handle` or `monitor` (neither: the primary monitor), `maxSize` (64-7680, default 1568) | A PNG as image content, scaled down so its longest side is at most `maxSize`, plus the sizes. A monitor is captured as it looks on screen. A window is drawn on its own, as if nothing covered it. Minimized windows can't be captured |

### Show what they're doing (Presence)

| Tool | Arguments | Does |
|---|---|---|
| `set_agent_status` | `status` (`working`, `progress`, `paused`, `error`, `attention`, `clear`), `percent` (required for `progress`), `handle` (optional) | Shows the agent's state on its own window's taskbar button: a moving bar, a percentage, or a paused or red bar, over the app's own progress. `attention` flashes the button until you bring the window to the front. Without a handle, NeoShell finds the agent's window from the processes that started it, such as the terminal Claude Code runs in |
| `notify` | `title` (required), `text`, `buttons` (up to 3, 40 characters each), `waitSeconds` (1-600, default 120) | Shows a notification above the taskbar, labelled as coming from the agent. With buttons, it stays until you pick one or close it, and the call answers with the button you picked (or `null`, or `timedOut`). It follows Do not disturb |

Everything an agent shows (its status and any notification still waiting for an answer) goes away as soon as it
disconnects or you disconnect it.

## Security

- Off until you turn it on, and each kind of access is a switch of its own. Acting in the shell and seeing the
  screen are off until you turn them on.
- Only your own programs can connect. The pipe accepts only your user account, and low-integrity processes (such as
  sandboxed browser tabs) are refused. No network port is opened.
- Agents can only do what you could do with a click: they act on windows with taskbar buttons and apps in Start,
  with no elevation and no command lines.
- Every tool call is written to NeoShell's log (`%LOCALAPPDATA%\NeoShell\logs`) with the agent's name, the tool and
  the outcome.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| The client shows NeoShell with no tools | NeoShell isn't running, or **Let AI agents connect** is off. The tools appear as soon as NeoShell starts with access on |
| A tool answers "NeoShell's settings don't let agents use … tools" | That group's switch is off in Settings > Agents |
| "The user disconnected this agent" | Disconnect was pressed for this session. Start a new session of the agent |
| `list_tray_icons` says the tray is Explorer's | NeoShell runs alongside Explorer. Only as the shell does NeoShell own the tray |
| `notify` says the notification didn't show | Do not disturb is on, the notification center is open, or banners are turned off for `NeoShell.Agent` in Windows' notification settings |
| `set_agent_status` finds no window | The agent's processes have no window with a taskbar button (some background hosts). Pass a `handle` from `list_windows` |
| Calls answer "NeoShell is busy" | NeoShell's UI thread didn't answer within 10 seconds. Try again; if it persists, check the log |
