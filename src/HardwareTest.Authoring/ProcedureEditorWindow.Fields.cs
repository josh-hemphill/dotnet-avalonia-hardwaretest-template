using Avalonia.Automation;
using Avalonia.Controls;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public partial class ProcedureEditorWindow
{
    private void AddField(ProcedureEditorField field)
    {
        var label = new TextBlock { Text = field.Definition.Label, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(label);
        var editable = _editable && !field.ReadOnly;
        if (field.Definition.Kind == ProcedureFieldKind.Boolean)
        {
            var flag = new CheckBox { Content = field.Definition.Label, IsChecked = bool.TryParse(field.Value, out var value) && value, IsEnabled = editable };
            row.Children.Clear();
            row.Children.Add(flag);
            _fields[field.Definition.Key] = () => flag.IsChecked == true ? "true" : "false";
        }
        else if (field.Definition.Kind == ProcedureFieldKind.Instrument || (field.Definition.ReferenceTable is not null && field.Definition.Kind == ProcedureFieldKind.Text))
        {
            var options = field.Choices.Append(field.Value).Distinct(StringComparer.Ordinal).Select(value => new FieldChoice(value, field.Definition.Kind == ProcedureFieldKind.Instrument ? _session.ChoiceLabel(field, value) : value.Length == 0 ? "None" : value)).ToArray();
            var select = new ComboBox
            {
                ItemsSource = options,
                SelectedItem = options.FirstOrDefault(option => option.Value == field.Value),
                IsEnabled = editable,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(select, field.Definition.Label);
            row.Children.Add(select);
            _fields[field.Definition.Key] = () => (select.SelectedItem as FieldChoice)?.Value ?? field.Value;
            if (field.Definition.Kind == ProcedureFieldKind.Instrument)
            {
                select.SelectionChanged += (_, _) => Attempt(() =>
                {
                    if (_item is not { } item || select.SelectedItem is null) return;
                    CommitFields();
                    Present(item);
                });
            }
        }
        else if (field.Definition.Kind == ProcedureFieldKind.TextList && field.Definition.ReferenceTable is not null && field.Choices.Count > 0)
        {
            if (field.Definition.ReferenceTable == "sections")
            {
                var current = field.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var checks = field.Choices.Concat(current).Distinct(StringComparer.Ordinal).Select(value => (value,
                    check: new CheckBox { Content = _session.ChoiceLabel(field, value), IsChecked = current.Contains(value), IsEnabled = editable })).ToArray();
                foreach (var (_, check) in checks) row.Children.Add(check);
                _fields[field.Definition.Key] = () => string.Join(", ", checks.Where(entry => entry.check.IsChecked == true).Select(entry => entry.value));
            }
            else
            {
                AddText(); // Route order determines the measurement input order.
            }
        }
        else
        {
            AddText();
        }

        ItemFields.Children.Add(row);
        return;

        void AddText()
        {
            var box = new TextBox { Text = field.Value, IsReadOnly = !editable, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            AutomationProperties.SetName(box, field.Definition.Label);
            row.Children.Add(box);
            if (field.Definition.Kind is ProcedureFieldKind.TextList or ProcedureFieldKind.NumberList)
                row.Children.Add(new TextBlock { Text = field.Choices.Count == 0 ? "Separate values with commas." : $"Enter IDs in input order. Available: {string.Join(", ", field.Choices)}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            _fields[field.Definition.Key] = () => box.Text ?? "";
        }
    }

    private sealed record FieldChoice(string Value, string Label) { public override string ToString() => Label; }
}
