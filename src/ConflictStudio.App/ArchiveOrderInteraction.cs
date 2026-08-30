using ConflictStudio.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConflictStudio.App;

public static class ArchiveDragAutoScroll
{
    public static int LinesAt(double pointerY, double viewportHeight, double edgeSize = 64, int maximumLines = 16)
    {
        if (viewportHeight <= 0 || pointerY < 0 || pointerY > viewportHeight) return 0;
        double edge = Math.Min(edgeSize, viewportHeight / 2);
        if (pointerY < edge) return -Speed((edge - pointerY) / edge, maximumLines);
        return pointerY > viewportHeight - edge ? Speed((pointerY - (viewportHeight - edge)) / edge, maximumLines) : 0;
    }

    public static int ConsumeWheelDelta(ref int remainder, int delta)
    {
        remainder += delta;
        int notches = remainder / 120;
        remainder -= notches * 120;
        return -Math.Clamp(notches * 6, -24, 24);
    }

    public static void Scroll(ScrollViewer viewer, int lines)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        for (int index = 0; index < Math.Abs(lines); index++)
        {
            if (lines < 0) viewer.LineUp();
            else viewer.LineDown();
        }
    }

    private static int Speed(double depth, int maximumLines) => Math.Clamp(1 + (int)(Math.Clamp(depth, 0, 1) * Math.Max(0, maximumLines - 1)), 1, Math.Max(1, maximumLines));
}

public static class ArchiveConflictPreview
{
    public static ArchiveConflictSummary[] Build(IReadOnlyList<ResourceProvider> resources, IReadOnlyList<Mo2Archive> archives, IReadOnlyList<string> order, IReadOnlyList<RdarArchiveFailure> failures)
        => ArchiveResourceIndexBuilder.Build(resources, archives, order, failures);

    public static ResourceProvider[] RehydrateResources(IReadOnlyList<ArchiveConflictSummary> summaries)
    {
        return summaries.SelectMany(summary => summary.Winning.Concat(summary.Losing).Concat(summary.Unresolved).Concat(summary.Unique).Where(outcome => outcome.ResourceHash != 0).Select(outcome => new ResourceProvider(summary.ArchiveName, outcome.ResourceHash, outcome.DisplayName, outcome.PayloadFingerprint, ResourceType: outcome.ResourceType, PathConfidence: outcome.PathConfidence, ProviderName: summary.Provider)))
            .DistinctBy(value => (value.ArchiveName.ToUpperInvariant(), value.ResourceHash))
            .ToArray();
    }
}

public sealed class ArchiveOrderCloseDialog : Window
{
    private readonly List<string> _actionLabels = [];

    public ArchiveOrderCloseAction Action { get; private set; } = ArchiveOrderCloseAction.Cancel;
    internal IReadOnlyList<string> ActionLabelsForTesting => _actionLabels;

    public ArchiveOrderCloseDialog(bool canApply)
    {
        Title = "Unapplied archive order";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#101719");
        Foreground = Brush("#F2F6F7");
        FontFamily = new FontFamily("Segoe UI");
        Border body = new() { Padding = new Thickness(20), BorderBrush = Brush("#8C8500"), BorderThickness = new Thickness(1) };
        StackPanel stack = new();
        stack.Children.Add(new TextBlock { Text = canApply ? "The proposed archive order has not been applied." : "The proposed archive order cannot be applied with the current evidence.", FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = canApply ? "Apply and verify it before exit, discard it, or keep editing." : "Discard it to exit, or keep editing.", Margin = new Thickness(0, 8, 0, 18), Foreground = Brush("#B7C6C4"), TextWrapping = TextWrapping.Wrap });
        StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (canApply) actions.Children.Add(ActionButton("Apply and exit", ArchiveOrderCloseAction.ApplyAndClose, "#1D4B46", "#3D8C7E"));
        actions.Children.Add(ActionButton("Discard", ArchiveOrderCloseAction.DiscardAndClose, "#321701", "#8A4200"));
        Button keepEditing = ActionButton("Keep editing", ArchiveOrderCloseAction.Cancel, "#182326", "#405359");
        keepEditing.IsCancel = true;
        actions.Children.Add(keepEditing);
        stack.Children.Add(actions);
        body.Child = stack;
        Content = body;
    }

    private Button ActionButton(string label, ArchiveOrderCloseAction action, string background, string border)
    {
        _actionLabels.Add(label);
        Button button = new() { Content = label, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7), Background = Brush(background), BorderBrush = Brush(border), Foreground = Foreground };
        button.Click += (_, _) =>
        {
            Action = action;
            DialogResult = true;
        };
        return button;
    }

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}

public enum ArchiveOrderCloseAction { ApplyAndClose, DiscardAndClose, Cancel }
