using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Collections.Generic;
using OptiscalerClient.Helpers;
using OptiscalerClient.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace OptiscalerClient.Views
{
    public partial class CacheManagementWindow : Window
    {
        private readonly ComponentManagementService _componentService;

        public CacheManagementWindow()
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();
        }

        public CacheManagementWindow(Window owner)
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();

            // Flicker-free startup strategy
            this.Opacity = 0;

            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
            {
                titleBar.PointerPressed += (s, e) => this.BeginMoveDrag(e);
            }

            this.Opened += (s, e) =>
            {
                this.Opacity = 1;
                var rootPanel = this.FindControl<Panel>("RootPanel");
                if (rootPanel != null)
                {
                    AnimationHelper.SetupPanelTransition(rootPanel);
                    rootPanel.Opacity = 1;
                }
            };

            LoadCacheItems();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadCacheItems()
        {
            var pnlVersions = this.FindControl<StackPanel>("PnlVersions");
            var pnlExtras = this.FindControl<StackPanel>("PnlExtrasVersions");
            var pnlCustom = this.FindControl<StackPanel>("PnlCustomVersions");
            if (pnlVersions == null || pnlExtras == null) return;

            pnlVersions.Children.Clear();
            pnlExtras.Children.Clear();
            pnlCustom?.Children.Clear();

            var versions = _componentService.GetDownloadedOptiScalerVersions();
            var extras = _componentService.GetDownloadedExtrasVersions();
            var customSet = _componentService.CustomVersions;

            // Separate official vs custom
            var officialVersions = versions.Where(v => !customSet.Contains(v)).ToList();
            var customVersions = versions.Where(v => customSet.Contains(v)).ToList();

            var txtCacheInfo = this.FindControl<TextBlock>("TxtCacheInfo");
            if (txtCacheInfo != null)
            {
                int totalCount = versions.Count + extras.Count;
                txtCacheInfo.Text = $"{totalCount} items stored locally (OptiScaler & FSR4 Extras).";
            }

            // Populate OptiScaler Versions (official only)
            if (!officialVersions.Any())
            {
                pnlVersions.Children.Add(new TextBlock
                {
                    Text = "No OptiScaler versions cached.",
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10)
                });
            }
            else
            {
                foreach (var ver in officialVersions)
                {
                    var card = CreateVersionCard(ver, isExtras: false);
                    pnlVersions.Children.Add(card);
                }
            }

            // Populate Custom Versions
            if (pnlCustom != null)
            {
                if (!customVersions.Any())
                {
                    pnlCustom.Children.Add(new TextBlock
                    {
                        Text = Application.Current?.FindResource("TxtNoCustomVersions") as string ?? "No custom versions imported.",
                        Foreground = Brushes.Gray,
                        FontSize = 11,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 10)
                    });
                }
                else
                {
                    foreach (var ver in customVersions)
                    {
                        var card = CreateVersionCard(ver, isExtras: false);
                        pnlCustom.Children.Add(card);
                    }
                }
            }

            // Populate Extras Versions
            if (!extras.Any())
            {
                pnlExtras.Children.Add(new TextBlock
                {
                    Text = "No FSR4 Extras cached.",
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10)
                });
            }
            else
            {
                foreach (var ver in extras)
                {
                    var card = CreateVersionCard(ver, isExtras: true);
                    pnlExtras.Children.Add(card);
                }
            }
        }

        private Border CreateVersionCard(string version, bool isExtras)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*, Auto"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var stack = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = version,
                FontWeight = FontWeight.Bold,
                Foreground = Application.Current?.FindResource("BrTextPrimary") as IBrush ?? Brushes.White
            });

            // Check if it's the currently selected main OptiScaler version
            if (!isExtras && version == _componentService.OptiScalerVersion)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = Application.Current?.FindResource("TxtCurrentSelection") as string ?? "Currently selected",
                    FontSize = 10,
                    Foreground = Application.Current?.FindResource("BrAccent") as IBrush ?? Brushes.DeepSkyBlue
                });
            }

            grid.Children.Add(stack);
            Grid.SetColumn(stack, 0);

            var btnDelete = new Button
            {
                Content = Application.Current?.FindResource("TxtDeletePlain") as string ?? "Delete",
                Classes = { "BtnSecondary" },
                Padding = new Thickness(12, 4),
                FontSize = 11,
                Tag = new VersionDeleteInfo { Version = version, IsExtras = isExtras }
            };
            btnDelete.Click += BtnDelete_Click;

            grid.Children.Add(btnDelete);
            Grid.SetColumn(btnDelete, 1);

            return new Border
            {
                Background = Application.Current?.FindResource("BrBgCard") as IBrush ?? Brushes.Transparent,
                BorderBrush = Application.Current?.FindResource("BrBorderSubtle") as IBrush ?? Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10),
                Child = grid
            };
        }

        private class VersionDeleteInfo
        {
            public string Version { get; set; } = "";
            public bool IsExtras { get; set; }
        }

        private async void BtnDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is VersionDeleteInfo info)
            {
                var title = info.IsExtras ? "Delete FSR4 Extra" : "Delete OptiScaler Version";
                var msg = info.IsExtras 
                    ? $"Are you sure you want to delete FSR4 INT8 Extra {info.Version}?"
                    : $"Are you sure you want to delete OptiScaler {info.Version} from cache?";

                var dialog = new ConfirmDialog(this, title, msg, false);
                var result = await dialog.ShowDialog<bool>(this);

                if (result)
                {
                    try
                    {
                        if (info.IsExtras)
                            _componentService.DeleteExtrasCache(info.Version);
                        else
                            _componentService.DeleteOptiScalerCache(info.Version);
                        
                        LoadCacheItems();
                    }
                    catch (Exception ex)
                    {
                        await new ConfirmDialog(this, "Error", $"Failed to delete version: {ex.Message}").ShowDialog<object>(this);
                    }
                }
            }
        }

        private bool _isAnimatingClose = false;

        private async void BtnImportCustom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select OptiScaler Archive",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Archives (7z, zip, rar)")
                        {
                            Patterns = new[] { "*.7z", "*.zip", "*.rar" }
                        }
                    }
                });

                if (files == null || files.Count == 0) return;

                var filePath = files[0].Path.IsAbsoluteUri
                    ? files[0].Path.LocalPath
                    : files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(filePath)) return;

                // Show importing overlay
                var overlay = this.FindControl<Grid>("OverlayImporting");
                var btnImport = this.FindControl<Button>("BtnImportCustom");
                var txtStatus = this.FindControl<TextBlock>("TxtImportStatus");
                if (overlay != null) overlay.IsVisible = true;
                if (btnImport != null) btnImport.IsEnabled = false;
                if (txtStatus != null) txtStatus.Text = "";

                var versionName = await _componentService.ImportCustomOptiScalerVersionAsync(filePath);
                DebugWindow.Log($"[Cache] Custom version imported: {versionName}");

                if (overlay != null) overlay.IsVisible = false;
                if (txtStatus != null) txtStatus.Text = $"✓ {versionName}";
                if (btnImport != null) btnImport.IsEnabled = true;
                LoadCacheItems();
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[Cache] Import custom version failed: {ex}");
                var overlay = this.FindControl<Grid>("OverlayImporting");
                var btnImport = this.FindControl<Button>("BtnImportCustom");
                var txtStatus = this.FindControl<TextBlock>("TxtImportStatus");
                if (overlay != null) overlay.IsVisible = false;
                if (btnImport != null) btnImport.IsEnabled = true;
                if (txtStatus != null) txtStatus.Text = "";
                var innerMsg = ex.InnerException != null ? $"\n{ex.InnerException.Message}" : "";
                await new ConfirmDialog(this, "Import Error", $"Failed to import custom version:\n{ex.Message}{innerMsg}").ShowDialog<object>(this);
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => _ = CloseAnimated();

        private async Task CloseAnimated()
        {
            if (_isAnimatingClose) return;
            _isAnimatingClose = true;
            var rootPanel = this.FindControl<Panel>("RootPanel");
            if (rootPanel != null) rootPanel.Opacity = 0;
            await Task.Delay(220);
            this.Close();
        }
    }
}
