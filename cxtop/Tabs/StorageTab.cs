using cxtop.Helpers;
using cxtop.Stats;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace cxtop.Tabs;

internal sealed class StorageTab : BaseResponsiveTab
{
    private readonly KeyedHistoryTracker<string> _readHistory = new();
    private readonly KeyedHistoryTracker<string> _writeHistory = new();

    private int _selectedDiskIndex;
    private readonly List<PanelControl> _diskCards = new();
    private string? _lastSelectedDeviceKey;

    public StorageTab(ConsoleWindowSystem windowSystem, ISystemStatsProvider stats)
        : base(windowSystem, stats) { }

    public override string Name => "Storage";
    public override string PanelControlName => "storagePanel";
    protected override int LayoutThresholdWidth => UIConstants.StorageLayoutThresholdWidth;

    protected override List<string> BuildTextContent(SystemSnapshot snapshot) => new();

    #region Panel Building

    public override IWindowControl BuildPanel(SystemSnapshot initialSnapshot, int windowWidth)
    {
        _currentLayout = windowWidth >= LayoutThresholdWidth
            ? ResponsiveLayoutMode.Wide
            : ResponsiveLayoutMode.Narrow;

        if (_currentLayout == ResponsiveLayoutMode.Wide)
        {
            var grid = Controls.HorizontalGrid()
                .WithName(PanelControlName)
                .WithVerticalAlignment(VerticalAlignment.Fill)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithMargin(1, 0, 1, 1)
                .Visible(false)
                .Column(col =>
                {
                    col.Width(UIConstants.FixedTextColumnWidth);
                    var leftPanel = BuildScrollablePanel();
                    BuildLeftPanelCards(leftPanel, initialSnapshot);
                    col.Add(leftPanel);
                })
                .Column(col =>
                {
                    col.Width(UIConstants.SeparatorColumnWidth);
                    col.Add(new SeparatorControl
                    {
                        ForegroundColor = UIConstants.SeparatorColor,
                        VerticalAlignment = VerticalAlignment.Fill
                    });
                })
                .Column(col =>
                {
                    var rightPanel = BuildRightPanel();
                    BuildGraphsContent(rightPanel, initialSnapshot);
                    col.Add(rightPanel);
                })
                .Build();

            grid.BackgroundColor = UIConstants.BaseBg;
            grid.ForegroundColor = UIConstants.PrimaryText;
            return grid;
        }
        else
        {
            var grid = Controls.HorizontalGrid()
                .WithName(PanelControlName)
                .WithVerticalAlignment(VerticalAlignment.Fill)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithMargin(1, 0, 1, 1)
                .Visible(false)
                .Column(col =>
                {
                    var scrollPanel = BuildScrollablePanel();
                    BuildLeftPanelCards(scrollPanel, initialSnapshot);
                    AddNarrowSeparator(scrollPanel);
                    BuildGraphsContent(scrollPanel, initialSnapshot);
                    col.Add(scrollPanel);
                })
                .Build();

            grid.BackgroundColor = UIConstants.BaseBg;
            grid.ForegroundColor = UIConstants.PrimaryText;
            return grid;
        }
    }

    #endregion

    #region Left Panel Cards

    private void BuildLeftPanelCards(ScrollablePanelControl panel, SystemSnapshot snapshot)
    {
        var storage = snapshot.Storage;
        _diskCards.Clear();

        // Total Storage aggregate card
        var totalCard = BuildCard("Total Storage");
        totalCard.AddControl(
            new BarGraphBuilder()
                .WithName("leftStorageTotalBar")
                .WithLabel("Used")
                .WithLabelWidth(UIConstants.StorageBarLabelWidth)
                .WithValue(storage.TotalUsedPercent)
                .WithMaxValue(100)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithUnfilledColor(UIConstants.BarUnfilledColor)
                .ShowLabel().ShowValue()
                .WithValueFormat("F1")
                .WithMargin(0, 0, 0, 0)
                .WithSmoothGradient(UIConstants.GradientHealthy)
                .Build()
        );
        totalCard.AddControl(
            Controls.Markup()
                .WithName("storageAggStats")
                .AddLine(BuildAggStatsContent(storage))
                .WithAlignment(HorizontalAlignment.Left)
                .Build()
        );
        panel.AddControl(totalCard);

        // Per-disk selectable cards (PanelControl for click support)
        for (int i = 0; i < storage.Disks.Count; i++)
        {
            var disk = storage.Disks[i];
            var cardIndex = i;
            var mountIcon = disk.IsRemovable ? "📀" : "💾";

            var diskCard = PanelControl.Create()
                .WithContent(BuildDiskCardContent(disk))
                .Rounded()
                .WithBorderColor(UIConstants.SeparatorColor)
                .WithBackgroundColor(UIConstants.CardBg)
                .WithPadding(1, 0, 1, 0)
                .WithHeader($"{mountIcon} {disk.MountPoint}")
                .HeaderLeft()
                .StretchHorizontal()
                .WithName($"diskCard_{disk.DeviceName}")
                .Build();

            diskCard.ForegroundColor = UIConstants.PrimaryText;
            diskCard.MouseClick += (_, _) => SelectDisk(cardIndex);

            _diskCards.Add(diskCard);
            panel.AddControl(diskCard);
        }

        // Restore selection by device key or default to first
        RestoreSelection(storage);
        ApplySelectionVisuals();
    }

    private static string BuildAggStatsContent(StorageSample storage)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        return $"  [{muted}]Capacity:[/] [{accent}]{storage.TotalCapacityGb:F1} GB[/]\n" +
               $"  [{muted}]Used:[/]     [{accent}]{storage.TotalUsedGb:F1} GB[/] [{muted}]({storage.TotalUsedPercent:F1}%)[/]\n" +
               $"  [{muted}]Free:[/]     [{accent}]{storage.TotalFreeGb:F1} GB[/]";
    }

    private static string BuildDiskCardContent(DiskSample disk)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        return $"  [{muted}]Device:[/] [{accent}]{System.IO.Path.GetFileName(disk.DeviceName)}[/]\n" +
               $"  [{muted}]Type:[/]   [{accent}]{disk.FileSystemType}[/]\n" +
               $"  [{muted}]Size:[/]   [{accent}]{disk.TotalGb:F1} GB[/]\n" +
               $"  [{muted}]Used:[/]   [{accent}]{disk.UsedPercent:F1}%[/]";
    }

    #endregion

    #region Selection

    public bool HandleKey(ConsoleKey key)
    {
        switch (key)
        {
            case ConsoleKey.UpArrow:
                CycleDiskSelection(forward: false);
                return true;
            case ConsoleKey.DownArrow:
                CycleDiskSelection(forward: true);
                return true;
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
            case ConsoleKey.Home:
            case ConsoleKey.End:
                return HandleScrollKey(key);
            default:
                return false;
        }
    }

    private bool HandleScrollKey(ConsoleKey key)
    {
        var grid = FindMainWindow()?.FindControl<HorizontalGridControl>(PanelControlName);
        if (grid == null || grid.Columns.Count == 0) return false;

        var leftPanel = grid.Columns[0].Contents.FirstOrDefault() as ScrollablePanelControl;
        if (leftPanel == null) return false;

        switch (key)
        {
            case ConsoleKey.PageUp:     leftPanel.ScrollVerticalBy(-leftPanel.ViewportHeight); return true;
            case ConsoleKey.PageDown:   leftPanel.ScrollVerticalBy(leftPanel.ViewportHeight); return true;
            case ConsoleKey.Home:       leftPanel.ScrollToTop(); return true;
            case ConsoleKey.End:        leftPanel.ScrollToBottom(); return true;
            default: return false;
        }
    }

    private void CycleDiskSelection(bool forward)
    {
        if (_diskCards.Count == 0) return;

        int next = forward
            ? (_selectedDiskIndex + 1) % _diskCards.Count
            : (_selectedDiskIndex - 1 + _diskCards.Count) % _diskCards.Count;

        SelectDisk(next);
    }

    private void SelectDisk(int index)
    {
        if (index < 0 || index >= _diskCards.Count || index == _selectedDiskIndex)
            return;

        _selectedDiskIndex = index;
        ApplySelectionVisuals();
        ScrollSelectedCardIntoView();

        var snapshot = Stats.ReadSnapshot();
        if (index < snapshot.Storage.Disks.Count)
        {
            _lastSelectedDeviceKey = snapshot.Storage.Disks[index].DeviceName;
            RebuildRightPanel(snapshot);
        }
    }

    private void ScrollSelectedCardIntoView()
    {
        if (_selectedDiskIndex < 0 || _selectedDiskIndex >= _diskCards.Count) return;

        var grid = FindMainWindow()?.FindControl<HorizontalGridControl>(PanelControlName);
        if (grid == null || grid.Columns.Count == 0) return;

        var leftPanel = grid.Columns[0].Contents.FirstOrDefault() as ScrollablePanelControl;
        leftPanel?.ScrollChildIntoView(_diskCards[_selectedDiskIndex]);
    }

    private void ApplySelectionVisuals()
    {
        for (int i = 0; i < _diskCards.Count; i++)
        {
            _diskCards[i].BorderColor = i == _selectedDiskIndex
                ? UIConstants.Accent
                : UIConstants.SeparatorColor;
        }
    }

    private void RestoreSelection(StorageSample storage)
    {
        if (_lastSelectedDeviceKey != null)
        {
            for (int i = 0; i < storage.Disks.Count; i++)
            {
                if (storage.Disks[i].DeviceName == _lastSelectedDeviceKey)
                {
                    _selectedDiskIndex = i;
                    return;
                }
            }
        }

        _selectedDiskIndex = 0;
        if (storage.Disks.Count > 0)
            _lastSelectedDeviceKey = storage.Disks[0].DeviceName;
    }

    #endregion

    #region Right Panel — Selected Disk Detail

    protected override void BuildGraphsContent(ScrollablePanelControl panel, SystemSnapshot snapshot)
    {
        var storage = snapshot.Storage;
        if (storage.Disks.Count == 0)
        {
            var muted = UIConstants.MutedText.ToMarkup();
            panel.AddControl(
                Controls.Markup()
                    .AddLine($"[{muted}]No storage devices[/]")
                    .WithMargin(1, 1, 1, 0)
                    .Build()
            );
            return;
        }

        var idx = Math.Clamp(_selectedDiskIndex, 0, storage.Disks.Count - 1);
        BuildSelectedDiskDetail(panel, snapshot, storage.Disks[idx]);
    }

    private void BuildSelectedDiskDetail(ScrollablePanelControl panel, SystemSnapshot snapshot, DiskSample disk)
    {
        var accent = UIConstants.Accent.ToMarkup();
        var muted = UIConstants.MutedText.ToMarkup();
        var deviceKey = disk.DeviceName;
        var deviceShort = System.IO.Path.GetFileName(disk.DeviceName);

        // Header label
        var headerParts = $"{disk.MountPoint}  [{muted}]{deviceShort} · {disk.FileSystemType}";
        if (!string.IsNullOrEmpty(disk.Label))
            headerParts += $" · {disk.Label}";
        headerParts += "[/]";
        AddFluentSectionLabel(panel, headerParts);

        // Capacity section
        panel.AddControl(
            new BarGraphBuilder()
                .WithName("detailUsageBar")
                .WithLabel("Used")
                .WithLabelWidth(UIConstants.StorageBarLabelWidth)
                .WithValue(disk.UsedPercent)
                .WithMaxValue(100)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithUnfilledColor(UIConstants.BarUnfilledColor)
                .ShowLabel().ShowValue()
                .WithValueFormat("F1")
                .WithMargin(1, 0, 1, 0)
                .WithSmoothGradient(UIConstants.GradientHealthy)
                .Build()
        );

        var capacityLines = $"  [{muted}]Total:[/]  [{accent}]{disk.TotalGb:F1} GB[/]\n" +
                            $"  [{muted}]Used:[/]   [{accent}]{disk.UsedGb:F1} GB[/]  [{muted}]Free:[/] [{accent}]{disk.FreeGb:F1} GB[/]";

        var detailLines = new System.Text.StringBuilder();
        detailLines.Append($"  [{muted}]Type:[/]   [{accent}]{(disk.IsRemovable ? "Removable" : "Fixed")}[/]");
        if (!string.IsNullOrEmpty(disk.MountOptions))
            detailLines.Append($"\n  [{muted}]Mount:[/]  [{accent}]{disk.MountOptions}[/]");

        panel.AddControl(
            Controls.Markup()
                .WithName("detailCapacityInfo")
                .AddLine(capacityLines)
                .WithAlignment(HorizontalAlignment.Left)
                .WithMargin(1, 0, 1, 0)
                .Build()
        );
        panel.AddControl(
            Controls.Markup()
                .WithName("detailMountInfo")
                .AddLine(detailLines.ToString())
                .WithAlignment(HorizontalAlignment.Left)
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

        // I/O Throughput section
        AddFluentSectionLabel(panel, "I/O Throughput");

        double maxRead = Math.Max(10, _readHistory.GetMutable(deviceKey).DefaultIfEmpty(0).Max());
        double maxWrite = Math.Max(10, _writeHistory.GetMutable(deviceKey).DefaultIfEmpty(0).Max());

        panel.AddControl(
            new BarGraphBuilder()
                .WithName("detailReadBar")
                .WithLabel("Read")
                .WithLabelWidth(UIConstants.StorageBarLabelWidth)
                .WithValue(disk.ReadMbps)
                .WithMaxValue(maxRead)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithUnfilledColor(UIConstants.BarUnfilledColor)
                .ShowLabel().ShowValue()
                .WithValueFormat("F1")
                .WithMargin(1, 0, 1, 0)
                .WithSmoothGradient(UIConstants.GradientIoRead)
                .Build()
        );

        panel.AddControl(
            new BarGraphBuilder()
                .WithName("detailWriteBar")
                .WithLabel("Write")
                .WithLabelWidth(UIConstants.StorageBarLabelWidth)
                .WithValue(disk.WriteMbps)
                .WithMaxValue(maxWrite)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithUnfilledColor(UIConstants.BarUnfilledColor)
                .ShowLabel().ShowValue()
                .WithValueFormat("F1")
                .WithMargin(1, 0, 1, 0)
                .WithSmoothGradient(UIConstants.GradientIoWrite)
                .Build()
        );

        // I/O History sparkline
        AddFluentSectionLabel(panel, "I/O History");

        panel.AddControl(
            new SparklineBuilder()
                .WithName("detailIoSparkline")
                .WithTitle("↑ Read  ↓ Write")
                .WithTitleColor(UIConstants.MutedText)
                .WithTitlePosition(TitlePosition.Bottom)
                .WithHeight(UIConstants.StorageIoSparklineHeight)
                .WithMaxValue(maxRead).WithSecondaryMaxValue(maxWrite)
                .WithGradient(UIConstants.SparkStorageRead)
                .WithSecondaryGradient(UIConstants.SparkStorageWrite)
                .WithBackgroundColor(UIConstants.PanelBg)
                .WithBorder(BorderStyle.None)
                .WithMode(SparklineMode.BidirectionalBraille)
                .WithBaseline(true, position: TitlePosition.Bottom)
                .WithInlineTitleBaseline(true)
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithMargin(1, 0, 1, 0)
                .WithBidirectionalData(_readHistory.GetMutable(deviceKey), _writeHistory.GetMutable(deviceKey))
                .Build()
        );
    }

    private void RebuildRightPanel(SystemSnapshot snapshot)
    {
        var grid = FindMainWindow()?.FindControl<HorizontalGridControl>(PanelControlName);
        if (grid == null) return;

        var rightPanel = FindGraphPanel(grid);
        if (rightPanel == null) return;

        rightPanel.ClearContents();
        BuildGraphsContent(rightPanel, snapshot);

        FindMainWindow()?.ForceRebuildLayout();
        FindMainWindow()?.Invalidate(true);
    }

    #endregion

    #region Update

    protected override void UpdateHistory(SystemSnapshot snapshot)
    {
        foreach (var disk in snapshot.Storage.Disks)
        {
            _readHistory.Add(disk.DeviceName, disk.ReadMbps);
            _writeHistory.Add(disk.DeviceName, disk.WriteMbps);
        }
    }

    protected override void UpdateGraphControls(HorizontalGridControl grid, SystemSnapshot snapshot)
    {
        var storage = snapshot.Storage;
        if (storage.Disks.Count == 0) return;

        var idx = Math.Clamp(_selectedDiskIndex, 0, storage.Disks.Count - 1);
        var disk = storage.Disks[idx];
        var deviceKey = disk.DeviceName;

        var rightPanel = FindGraphPanel(grid);
        if (rightPanel == null) return;

        // Update capacity bar
        var usageBar = rightPanel.Children.FirstOrDefault(c => c.Name == "detailUsageBar") as BarGraphControl;
        if (usageBar != null) usageBar.Value = disk.UsedPercent;

        // Update I/O bars
        double readMax = Math.Max(10, _readHistory.Get(deviceKey).DefaultIfEmpty(0).Max());
        double writeMax = Math.Max(10, _writeHistory.Get(deviceKey).DefaultIfEmpty(0).Max());

        var readBar = rightPanel.Children.FirstOrDefault(c => c.Name == "detailReadBar") as BarGraphControl;
        if (readBar != null) { readBar.Value = disk.ReadMbps; readBar.MaxValue = readMax; }

        var writeBar = rightPanel.Children.FirstOrDefault(c => c.Name == "detailWriteBar") as BarGraphControl;
        if (writeBar != null) { writeBar.Value = disk.WriteMbps; writeBar.MaxValue = writeMax; }

        // Update sparkline
        var ioSparkline = rightPanel.Children.FirstOrDefault(c => c.Name == "detailIoSparkline") as SparklineControl;
        if (ioSparkline != null)
        {
            ioSparkline.SetBidirectionalData(
                _readHistory.GetMutable(deviceKey),
                _writeHistory.GetMutable(deviceKey));
        }

        // Update capacity info text
        var capacityInfo = rightPanel.Children.FirstOrDefault(c => c.Name == "detailCapacityInfo") as MarkupControl;
        if (capacityInfo != null)
        {
            var accent = UIConstants.Accent.ToMarkup();
            var muted = UIConstants.MutedText.ToMarkup();
            capacityInfo.SetContent(new List<string>
            {
                $"  [{muted}]Total:[/]  [{accent}]{disk.TotalGb:F1} GB[/]\n" +
                $"  [{muted}]Used:[/]   [{accent}]{disk.UsedGb:F1} GB[/]  [{muted}]Free:[/] [{accent}]{disk.FreeGb:F1} GB[/]"
            });
        }
    }

    protected override void UpdateLeftColumnText(HorizontalGridControl grid, SystemSnapshot snapshot)
    {
        var storage = snapshot.Storage;

        var aggStats = FindMainWindow()?.FindControl<MarkupControl>("storageAggStats");
        aggStats?.SetContent(new List<string> { BuildAggStatsContent(storage) });

        var totalBar = FindMainWindow()?.FindControl<BarGraphControl>("leftStorageTotalBar");
        if (totalBar != null)
            totalBar.Value = storage.TotalUsedPercent;

        foreach (var disk in storage.Disks)
        {
            var diskCard = FindMainWindow()?.FindControl<PanelControl>($"diskCard_{disk.DeviceName}");
            if (diskCard != null)
                diskCard.Content = BuildDiskCardContent(disk);
        }
    }

    public new void HandleResize(int newWidth, int newHeight)
    {
        var grid = FindMainWindow()?.FindControl<HorizontalGridControl>(PanelControlName);
        if (grid == null || !grid.Visible)
            return;

        var desired = newWidth >= LayoutThresholdWidth
            ? ResponsiveLayoutMode.Wide
            : ResponsiveLayoutMode.Narrow;

        if (desired == _currentLayout)
            return;

        _currentLayout = desired;
        RebuildCardColumns(grid);
    }

    private void RebuildCardColumns(HorizontalGridControl grid)
    {
        var snapshot = Stats.ReadSnapshot();

        for (int i = grid.Columns.Count - 1; i >= 0; i--)
            grid.RemoveColumn(grid.Columns[i]);

        if (_currentLayout == ResponsiveLayoutMode.Wide)
        {
            var leftPanel = BuildScrollablePanel();
            BuildLeftPanelCards(leftPanel, snapshot);

            var leftCol = new ColumnContainer(grid) { Width = UIConstants.FixedTextColumnWidth };
            leftCol.AddContent(leftPanel);
            grid.AddColumn(leftCol);

            var sepCol = new ColumnContainer(grid) { Width = UIConstants.SeparatorColumnWidth };
            sepCol.AddContent(new SeparatorControl
            {
                ForegroundColor = UIConstants.SeparatorColor,
                VerticalAlignment = VerticalAlignment.Fill
            });
            grid.AddColumn(sepCol);

            var rightPanel = BuildRightPanel();
            BuildGraphsContent(rightPanel, snapshot);

            var rightCol = new ColumnContainer(grid);
            rightCol.AddContent(rightPanel);
            grid.AddColumn(rightCol);
        }
        else
        {
            var scrollPanel = BuildScrollablePanel();
            BuildLeftPanelCards(scrollPanel, snapshot);
            AddNarrowSeparator(scrollPanel);
            BuildGraphsContent(scrollPanel, snapshot);

            var col = new ColumnContainer(grid);
            col.AddContent(scrollPanel);
            grid.AddColumn(col);
        }

        FindMainWindow()?.ForceRebuildLayout();
        FindMainWindow()?.Invalidate(true);
    }

    #endregion
}
