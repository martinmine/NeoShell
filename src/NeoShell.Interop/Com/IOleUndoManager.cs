using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NeoShell.Interop.Com;

// The shell's undo history (oleidl.h's undo interfaces, and shobjidl's IExplorerCommand). The methods after the last
// called one are left out.

[GeneratedComInterface]
[Guid("d001f200-ef97-11ce-9bc9-00aa00608e01")]
internal unsafe partial interface IOleUndoManager
{
    [PreserveSig] int Open(nint parentUnit);
    [PreserveSig] int Close(nint parentUnit, int commit);
    [PreserveSig] int Add(nint unit);
    [PreserveSig] int GetOpenParentState(uint* state);
    [PreserveSig] int DiscardFrom(nint unit);
    [PreserveSig] int UndoTo(nint unit);
    [PreserveSig] int RedoTo(nint unit);
    /// <summary>The units to undo, the latest first.</summary>
    [PreserveSig] int EnumUndoable(nint* units);
    [PreserveSig] int EnumRedoable(nint* units);
}

[GeneratedComInterface]
[Guid("b3e7c340-ef97-11ce-9bc9-00aa00608e01")]
internal unsafe partial interface IEnumOleUndoUnits
{
    [PreserveSig] int Next(uint count, nint* units, uint* fetched);
}

[GeneratedComInterface]
[Guid("894ad3b0-ef97-11ce-9bc9-00aa00608e01")]
internal unsafe partial interface IOleUndoUnit
{
    [PreserveSig] int Do(nint undoManager);
    [PreserveSig] int GetDescription(nint* description);
    [PreserveSig] int GetUnitType(Guid* classId, int* id);
    [PreserveSig] int OnNextAdd();
}

/// <summary>
/// shell32's own undo units (<c>CCommonParentUndoUnit</c>, windows.storage's file operation units): undocumented, from
/// shell32's symbols (Windows 11 25H2). Its menu text is what Explorer's desktop shows ("&amp;Undo Delete\tCtrl+Z").
/// </summary>
[GeneratedComInterface]
[Guid("33747358-8a56-4a62-9342-f4b86d97a8e4")]
internal unsafe partial interface IShellUndoUnit : IOleUndoUnit
{
    /// <param name="flags">1 for the menu's text, as shell32's <c>GetUndoRedoText</c> asks for it.</param>
    [PreserveSig] int GetUndoText(char* text, uint length, int flags);
}

[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal unsafe partial interface IExplorerCommand
{
    [PreserveSig] int GetTitle(nint items, char** title);
    [PreserveSig] int GetIcon(nint items, char** icon);
    [PreserveSig] int GetToolTip(nint items, char** toolTip);
    [PreserveSig] int GetCanonicalName(Guid* name);
    /// <param name="state">ECS_ENABLED (0), ECS_DISABLED (1), ECS_HIDDEN (2)...</param>
    [PreserveSig] int GetState(nint items, int okToBeSlow, uint* state);
    [PreserveSig] int Invoke(nint items, nint bindContext);
}
