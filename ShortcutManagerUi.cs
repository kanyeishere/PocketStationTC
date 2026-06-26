using System.Numerics;
using Dalamud.Bindings.ImGui;
using PocketStation.Protocol;

namespace PocketStation;

/// <summary>
/// Modular ImGui UI for managing command shortcuts — add, edit, delete.
/// Keeps editing state across frames via a static editor context.
/// </summary>
public static class ShortcutManagerUi
{
    private sealed class EditorState
    {
        /// <summary>Shortcut being edited (null = adding new).</summary>
        public CommandShortcut? Editing;
        public string LabelInput = string.Empty;
        public string CommandInput = string.Empty;
        public bool IsNew;
        public string? Error;
    }

    private static readonly EditorState _state = new();

    /// <summary>
    /// Draw the shortcut manager table with add/edit/delete actions.
    /// </summary>
    /// <param name="shortcuts">The mutable shortcut list (from configuration).</param>
    /// <param name="save">Called after any mutation to persist changes.</param>
    public static void Draw(List<CommandShortcut> shortcuts, Action save)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Command Shortcuts");
        ImGui.Spacing();

        if (!ImGui.BeginTable("ShortcutTable", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120f);
        ImGui.TableHeadersRow();

        for (int i = 0; i < shortcuts.Count; i++)
        {
            var sc = shortcuts[i];
            bool isEditing = _state.Editing?.Id == sc.Id && !_state.IsNew;

            ImGui.PushID(sc.Id);
            ImGui.TableNextRow();

            // ── Label column ──
            ImGui.TableSetColumnIndex(0);
            if (isEditing)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##label", ref _state.LabelInput, 64);
            }
            else
            {
                ImGui.TextUnformatted(sc.Label);
            }

            // ── Command column ──
            ImGui.TableSetColumnIndex(1);
            if (isEditing)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##cmd", ref _state.CommandInput, 256);
            }
            else
            {
                ImGui.TextUnformatted(sc.Command);
            }

            // ── Actions column ──
            ImGui.TableSetColumnIndex(2);
            if (isEditing)
            {
                if (ImGui.SmallButton("Save##save"))
                    SaveEdit(shortcuts, save);

                ImGui.SameLine();
                if (ImGui.SmallButton("Cancel##cancel"))
                    CancelEdit();
            }
            else
            {
                if (ImGui.SmallButton("Edit##edit"))
                    BeginEdit(sc, isNew: false);

                ImGui.SameLine();
                if (ImGui.SmallButton("Delete##del"))
                {
                    shortcuts.RemoveAt(i);
                    save();
                    ImGui.PopID();
                    break; // list mutated, exit loop
                }
            }

            ImGui.PopID();
        }

        ImGui.EndTable();

        // ── Validation error ──
        if (_state.Error != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
            ImGui.TextWrapped(_state.Error);
            ImGui.PopStyleColor();
        }

        // ── Add new section ──
        ImGui.Spacing();
        bool isAdding = _state.IsNew;

        if (!isAdding)
        {
            if (ImGui.Button("+ Add new shortcut"))
                BeginEdit(new CommandShortcut(Guid.NewGuid().ToString("N")[..12], "", ""), isNew: true);
        }
        else
        {
            ImGui.TextUnformatted("New shortcut:");
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Label##newlabel", ref _state.LabelInput, 64);
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("Command##newcmd", ref _state.CommandInput, 256);

            if (ImGui.Button("Add##addnew"))
                SaveEdit(shortcuts, save);

            ImGui.SameLine();
            if (ImGui.Button("Cancel##cancelnew"))
                CancelEdit();
        }
    }

    private static void BeginEdit(CommandShortcut sc, bool isNew)
    {
        _state.Editing = sc;
        _state.LabelInput = sc.Label;
        _state.CommandInput = sc.Command;
        _state.IsNew = isNew;
        _state.Error = null;
    }

    private static void SaveEdit(List<CommandShortcut> shortcuts, Action save)
    {
        var label = _state.LabelInput.Trim();
        var command = _state.CommandInput.Trim();

        if (string.IsNullOrWhiteSpace(label))
        {
            _state.Error = "Label cannot be empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            _state.Error = "Command cannot be empty.";
            return;
        }

        _state.Error = null;

        if (_state.IsNew)
        {
            shortcuts.Add(new CommandShortcut(_state.Editing!.Id, label, command));
        }
        else
        {
            // Replace the existing shortcut
            var index = shortcuts.FindIndex(s => s.Id == _state.Editing!.Id);
            if (index >= 0)
                shortcuts[index] = new CommandShortcut(_state.Editing.Id, label, command);
        }

        CancelEdit();
        save();
    }

    private static void CancelEdit()
    {
        _state.Editing = null;
        _state.LabelInput = string.Empty;
        _state.CommandInput = string.Empty;
        _state.IsNew = false;
        _state.Error = null;
    }
}
