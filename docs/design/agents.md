# Agent access (MCP)

An MCP server through which AI agents (Claude Code, Claude Desktop, any MCP client) can see the shell and act in it:
what windows are open, what the tray and notifications say, launching and arranging apps, and showing their own state
on the taskbar and in notifications. Part of the [NeoShell design](../design.md). Status: the transport and protocol
(A1 in plan.md, "Agent access (MCP)"), the Read tools (A2) and the Agents settings page with its permissions (A3) are
built; Control, Screen and Presence have their switches but no tools yet.

## Goals

- **Agents use the shell as a tool:** "what's open?", "put the browser left and the terminal right", "start Spotify",
  "turn on Do not disturb", "what did that toast say?".
- **Agents show up in the shell:** an agent that is working, waiting for an answer or done shows it where people look
  (a task button's progress and attention, a notification with buttons), instead of in a terminal behind other windows.
- **A test aid for NeoShell itself:** shell state (task list, tray, work areas, theme) read directly, next to UI
  Automation.

Not goals: a general desktop automation server (clicking and typing into other apps is computer use's job), running
an agent inside NeoShell, or an LLM in the 2088 concept's ask line (a possible later client of this, below).

## Process model

```
MCP client ──stdio──▶ NeoShell.exe /mcp (bridge) ──named pipe──▶ NeoShell (running shell) ──▶ UI thread
```

- **The client starts `NeoShell.exe /mcp`**, the standard stdio transport: `claude mcp add neoshell --
  "<path>\NeoShell.exe" /mcp`. `Program.Main` branches on `/mcp` before the single-instance mutex, as it does on
  `/watch`, and never starts WinUI. A WinExe's standard handles work when the client redirects them, so no console
  project is needed.
- **The bridge relays bytes**, newline-delimited JSON-RPC both ways, between stdio and the pipe
  `\\.\pipe\NeoShell.Agents.<session id>`. It knows MCP only enough to survive NeoShell restarting (every `/exit`
  during development): it keeps the client's `initialize` request, and when the pipe breaks it answers requests with
  a JSON-RPC error ("NeoShell isn't running") and on the next request reconnects and replays `initialize` first, so
  the client's session outlives NeoShell's.
- **NeoShell speaks MCP.** The protocol lives in one place, inside the process that has the state, and the bridge
  stays about a page of code.
- **Why not Streamable HTTP from NeoShell itself:** a localhost port is open to every local process and to web pages
  (DNS rebinding), so it would need tokens and Origin checks; a pipe gets the operating system's access checks.

### The pipe

- Created only while agent access is on (the setting below), with `PipeOptions.CurrentUserOnly` and a DACL for the
  current user: other users, services and other sessions can't connect. The pipe carries the default medium
  mandatory label, so low-integrity and AppContainer processes (browsers' sandboxes) can't write to it. An elevated
  client may connect (high to medium is allowed).
- Several clients at once (each connection its own MCP session), a few at most; each connection read on the thread
  pool.
- Lives in `src/NeoShell/Agents/`: `System.IO.Pipes` is BCL, so no interop is needed.

## The MCP side

Hand-written over `System.Text.Json` (`JsonNode` for requests, records for results). MCP's server side is small:
`initialize` (protocol version, `capabilities.tools`, server name and version), `notifications/initialized`, `ping`,
`tools/list`, `tools/call`, and `notifications/tools/list_changed` when access changes. Results are `content` arrays
(text, or image for screenshots) with `structuredContent` and an `outputSchema` where the result is data; failures
are tool results with `isError: true` and a sentence saying why, not protocol errors.

The official C# SDK (`ModelContextProtocol`) isn't an approved package and pulls in `Microsoft.Extensions.*`
hosting; the protocol surface used here is a few hundred lines, so it's written by hand. (Owner's call if that
changes.)

### Threading

A `tools/call` arrives on a pipe thread. Its handler starts on the UI thread through `DispatcherQueue.TryEnqueue` and
a `TaskCompletionSource`, and may await work elsewhere (`search` loads the app catalog and asks the index on the
thread pool), with a timeout of 10 s for the whole call: long enough for an index search, which gives up after 4 s
and still returns the apps, and short enough that a hung UI thread is reported as busy. Handlers only read
the state the shell already keeps (the `WindowTracker`'s list, the tray's icons, the notification center's list) or
call what a click would call; nothing slow runs on the UI thread (screenshots and searches finish on the thread pool).

## Tools

Each tool belongs to a permission group (below). Names are snake_case verbs, grouped by prefix in the list.

| Tool | Group | What |
|---|---|---|
| `shell_state` | Read | Version, run mode, theme, monitors with work areas and DPI, taskbar edge and auto-hide, sidebar |
| `list_windows` | Read | The task list as the taskbar has it: handle, title, app (AUMID, name), process, monitor, bounds, minimized, foreground, flashing, progress, badge; in the order the windows appeared (the taskbar's own order isn't followed) |
| `list_tray_icons` | Read | Tray icons: app, tooltip, shown or behind the chevron |
| `indicators` | Read | Network, volume and mute, battery, Do not disturb and focus session, apps using the microphone or location |
| `list_notifications` | Read | The notification center's notifications: app, time, texts |
| `search` | Read | Start's search: apps from the app catalog with the id that starts them, ranked as Start ranks them, and indexed files and folders in Start's groups |
| `screenshot` | Screen | A monitor or a window as PNG (image content), scaled down to a size given |
| `activate_window` | Control | Bring a window to the front (as a task button click), restore if minimized |
| `arrange_window` | Control | Minimize, maximize, restore, or snap to a layout zone (Snap's zones and its window arranging) |
| `close_window` | Control | `WM_CLOSE`, as the thumbnail's close button; the app may still ask to save |
| `launch` | Control | Start an app from the app catalog (by AUMID or name, as Start), optionally with a file or URL argument; not arbitrary command lines |
| `show_surface` | Control | Open Start, Quick Settings, the notification center, the Settings window on a page |
| `set_volume`, `set_do_not_disturb` | Control | As their Quick Settings controls |
| `notify` | Presence | A notification from the agent: title, text, up to three buttons; returns the button chosen (or dismissed / timed out) |
| `set_agent_status` | Presence | Show the agent's state on a window's task button: working (indeterminate progress), progress %, needs attention (flash), done, cleared |

Deliberately left out: settings writes beyond these, power (sign out, shut down), Explorer switching, ending tasks,
reading other apps' contents.

### Presence

- **`set_agent_status`** targets the agent's own window, normally its terminal or app; the client names it by
  handle or by a process ID the bridge knows (the bridge's parent process chain, so "my terminal" works without the
  agent finding its own handle). NeoShell shows it as it shows `ITaskbarList3` progress and flashing, layered over
  the app's own state and cleared when the connection closes.
- **`notify`** is a NeoShell notification (toast, then the notification center) under the client's name from
  `initialize` (`clientInfo.name`), with NeoShell's toast look. Its buttons are answered through the open tool call;
  the notification says which agent sent it, so a page can't pose as the system.
- Later, if useful: MCP elicitation (`elicitation/create`) the other way round is the client's business, not
  NeoShell's.

## Permissions and safety

- **Off by default.** Settings > Agents: "Let AI agents connect to NeoShell" (`AgentAccessEnabled`; the pipe exists
  only while on), and one switch per group (`AgentToolGroup`): See the shell (Read, `AgentReadAllowed`, on), Act in the
  shell (Control, off), See the screen (Screen, off), Show what they're doing (Presence, on). A group that is off isn't
  in `tools/list`, and a call to one of its tools is a tool error naming the setting. Changing a group tells every
  connected client (`notifications/tools/list_changed`), so it asks for its tools again.
- **Who is connected:** the page lists each connection: the client's name from `initialize`, the agent's process
  and when it connected. The process comes from the pipe's client (`GetNamedPipeClientProcessId`, the bridge) and the
  processes that started it (a ToolHelp snapshot): the first that isn't NeoShell.exe, cmd.exe or conhost.exe, so
  Claude Code shows as its own process. A parent that has exited may have its id reused; the chain is a guide.
- **Disconnect** takes the agent's tools away (`McpSession.Revoke`): it gets `list_changed`, an empty list, and a
  tool error for every call, and its row stays, marked disconnected, until its bridge leaves. The pipe is left open on
  purpose: closed, the bridge would connect again two seconds later. A NeoShell restart forgets it.
- **The agent indicator**, beside the privacy indicator on the Windows 11 taskbar (`AgentIndicator`, the robot glyph
  in the accent's text colour): shown while an agent that isn't disconnected is connected and Control or Screen is
  allowed, its tooltip naming the agents; a click opens Settings > Agents. Read alone shows nothing, as an app
  reading a window's title lights no privacy indicator. The other themes have no privacy indicator, and no agent
  indicator either.
- **Data is data.** Window titles, notification texts and tooltips come from other apps and may hold text aimed at
  the agent; tool descriptions say so, and results put them in fields, never in prose NeoShell writes.
- **Every call is logged** (tool, client, result, time), arguments without text payloads.
- Nothing an agent does goes past what the user could click: no elevation, UIPI as for NeoShell's own clicks.
- Both run modes. Alongside Explorer the tray, toasts and Snap are Explorer's, so those tools say what isn't
  available instead of failing quietly (as `list_tray_icons` is best-effort there).

## Code layout

```
src/NeoShell/Agents/
  AgentServer.cs       The pipe: listening while on, connections, the allowed groups, Disconnect; created by App
  AgentConnection.cs   A connection: its session, the processes behind it, when it came
  McpSession.cs        One connection's JSON-RPC: initialize, ping, tools/list, tools/call; no UI types
  McpProtocol.cs       What the session and the bridge both build: versions, results, errors, parsing
  McpBridge.cs         NeoShell.exe /mcp: stdio to the pipe, answering for NeoShell while it isn't there
  AgentTool.cs         A tool: name, description, group, input/output schema, handler; ToolArguments
  AgentData.cs         The JSON agents read: windows, monitors, network, battery, notifications, search results
  ShellTools.cs        The tools above, over Taskbars, Tray, NotificationCenter, StartMenu search, Snap, Capture
  AgentPresence.cs     Agent status on task buttons and agent notifications, cleared per connection
src/NeoShell/Settings/AgentSettingsPage.xaml(.cs)  The Settings page
src/NeoShell.Interop/Shell/Processes.cs  A pipe's client process and the processes that started it
```

`McpSession` takes the tool list and a "run on the UI thread" function, so the protocol is unit-tested without WinUI.

## Testing

- Unit tests: framing (lines split across reads, several in one), `initialize` and version negotiation, unknown
  methods and malformed JSON, `tools/list` per enabled group, argument validation against each tool's schema,
  `isError` results, the bridge's replay of `initialize` after a reconnect, timeouts.
- Live: a scripted client (Python over stdio, in `E:\NeoShellWork\mcp`) for every tool in both run modes; Claude Code
  with `claude mcp add` for a real session (list, arrange two windows, a notify with buttons, status on its own
  terminal); restarting NeoShell under a connected client; a second Windows user's process refused by the pipe; a
  low-integrity process refused.
