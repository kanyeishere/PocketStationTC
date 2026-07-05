using System.Numerics;
using ImGuiNET;
using PocketStation.Domain;

namespace PocketStation.Host;

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
        ImGui.TextUnformatted("快捷指令");
        ImGui.Spacing();

        if (!ImGui.BeginTable("ShortcutTable", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            return;

        ImGui.TableSetupColumn("标签", ImGuiTableColumnFlags.WidthStretch, 0.30f);
        ImGui.TableSetupColumn("指令", ImGuiTableColumnFlags.WidthStretch, 0.40f);
        ImGui.TableSetupColumn("排序", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 120f);
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

            // ── Reorder column ──
            ImGui.TableSetColumnIndex(2);
            // Move Up
            if (i > 0)
            {
                if (ImGui.ArrowButton("##up", ImGuiDir.Up))
                {
                    (shortcuts[i - 1], shortcuts[i]) = (shortcuts[i], shortcuts[i - 1]);
                    save();
                    ImGui.PopID();
                    break; // list mutated, exit loop
                }
            }

            ImGui.SameLine();
            // Move Down
            if (i < shortcuts.Count - 1)
            {
                if (ImGui.ArrowButton("##dn", ImGuiDir.Down))
                {
                    (shortcuts[i + 1], shortcuts[i]) = (shortcuts[i], shortcuts[i + 1]);
                    save();
                    ImGui.PopID();
                    break; // list mutated, exit loop
                }
            }

            // ── Actions column ──
            ImGui.TableSetColumnIndex(3);
            if (isEditing)
            {
                if (ImGui.SmallButton("保存##save"))
                    SaveEdit(shortcuts, save);

                ImGui.SameLine();
                if (ImGui.SmallButton("取消##cancel"))
                    CancelEdit();
            }
            else
            {
                if (ImGui.SmallButton("编辑##edit"))
                    BeginEdit(sc, isNew: false);

                ImGui.SameLine();
                if (ImGui.SmallButton("删除##del"))
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
            if (ImGui.Button("+ 添加快捷指令"))
                BeginEdit(new CommandShortcut(Guid.NewGuid().ToString("N")[..12], "", ""), isNew: true);
        }
        else
        {
            ImGui.TextUnformatted("新快捷指令：");
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("标签##newlabel", ref _state.LabelInput, 64);
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("指令##newcmd", ref _state.CommandInput, 256);

            if (ImGui.Button("添加##addnew"))
                SaveEdit(shortcuts, save);

            ImGui.SameLine();
            if (ImGui.Button("取消##cancelnew"))
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
            _state.Error = "标签不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            _state.Error = "指令不能为空。";
            return;
        }

        _state.Error = null;
        var editing = _state.Editing;
        if (editing == null)
        {
            _state.Error = "没有正在编辑的快捷指令。";
            return;
        }

        if (_state.IsNew)
        {
            shortcuts.Add(new CommandShortcut(editing.Id, label, command));
        }
        else
        {
            // Replace the existing shortcut
            var index = shortcuts.FindIndex(s => s.Id == editing.Id);
            if (index >= 0)
                shortcuts[index] = new CommandShortcut(editing.Id, label, command);
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
