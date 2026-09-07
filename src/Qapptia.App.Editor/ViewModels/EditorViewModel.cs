using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qapptia.App.Editor.ViewModels.Shapes;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Services;
using IFontProvider = Qapptia.Editor.Core.IFontProvider;
using Qapptia.App.Editor.Common;
using Qapptia.Editor.Models;
using Qapptia.Editor.Models.Navigation;
using Qapptia.Editor.Services;
using Qapptia.Editor.Tools;
using Qapptia.UI.Components.Controls;
using Serilog;

namespace Qapptia.App.Editor.ViewModels;

public partial class EditorViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush s_captureActiveBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush s_captureInactiveBrush = new SolidColorBrush(Color.Parse("#E53935"));

    private readonly IClipboardService? _clipboardService;
    private readonly ICaptureAppService _captureAppService;
    private CancellationTokenSource? _toastCts;

    public SidebarViewModel Sidebar { get; }
    public ToolbarViewModel Toolbar { get; }
    public CanvasViewportViewModel Viewport { get; }
    public CanvasBoardViewModel Board { get; }

    // --- Estado del Capturador en segundo plano ---
    [ObservableProperty]
    private bool _isCaptureActive;

    public IBrush CaptureStatusBorderBrush => IsCaptureActive ? s_captureActiveBrush : s_captureInactiveBrush;
    public IBrush CaptureStatusBackgroundBrush => IsCaptureActive ? s_captureActiveBrush : s_captureInactiveBrush;
    public string CaptureStatusToolTip => IsCaptureActive ? Constants.ToolTipCaptureActive : Constants.ToolTipCaptureInactive;

    partial void OnIsCaptureActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CaptureStatusBorderBrush));
        OnPropertyChanged(nameof(CaptureStatusBackgroundBrush));
        OnPropertyChanged(nameof(CaptureStatusToolTip));
    }

    // --- Notificaciones Toast ---
    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private ToastNotificationType _toastType = ToastNotificationType.Success;

    [ObservableProperty]
    private HorizontalAlignment _toastAlignment = HorizontalAlignment.Left;

    // --- Eventos Globales ---
    public event EventHandler? SaveRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? RotateRequested;
    public event EventHandler? FitImageRequested;
    public event EventHandler? ImageLoaded;
    public event EventHandler? RequestRedraw;
    public event EventHandler? TextInputFocusRequested;

    private readonly Action<Action> _dispatcher;

    public EditorViewModel(
        IEditorStateService stateService,
        string savePath,
        IFontProvider fontProvider,
        IClipboardService? clipboardService = null,
        INavigationService? navigationService = null,
        ICanvasStateService? canvasStateService = null,
        IShellService? shellService = null,
        ICaptureAppService? captureAppService = null,
        Action<Action>? uiDispatcher = null)
    {
        _clipboardService = clipboardService;
        _dispatcher = uiDispatcher ?? (action =>
        {
            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    action();
                }
                else
                {
                    Dispatcher.UIThread.Post(action);
                }
            }
            catch
            {
                action();
            }
        });

        var navService = navigationService ?? new NavigationService(Log.Logger.ForContext<NavigationService>());
        var canvasService = canvasStateService ?? new CanvasStateService(Log.Logger.ForContext<CanvasStateService>());
        var shell = shellService ?? NullShellService.Instance;

        _captureAppService = captureAppService ?? NullCaptureAppService.Instance;
        _isCaptureActive = _captureAppService.IsRunning;
        _captureAppService.StatusChanged += OnCaptureStatusChanged;
        _captureAppService.StartMonitoring(TimeSpan.FromSeconds(2.5));

        Sidebar = new SidebarViewModel(navService, stateService, savePath, shell);
        Toolbar = new ToolbarViewModel(stateService);
        Viewport = new CanvasViewportViewModel();
        Board = new CanvasBoardViewModel(canvasService, stateService);

        Sidebar.ToastRequested += (msg, type) => ShowToast(msg, type);

        // Coordinación de eventos entre Sub-ViewModels
        Sidebar.FileSelected += (s, file) =>
        {
            if (file != null)
            {
                Board.LoadImage(file);
            }
            else
            {
                Board.ClearImage();
            }
        };

        Toolbar.ToolChanged += (s, tool) =>
        {
            Board.CommitCurrentState();
        };

        Toolbar.ColorChanged += (s, color) =>
        {
            bool needsRedraw = false;
            foreach (var shape in Board.Shapes)
            {
                if (shape.IsSelected)
                {
                    shape.Color = color;
                    needsRedraw = true;
                }
            }

            if (Board.ActiveTextInputShape is VectorShape textShape)
            {
                textShape.Color = color;
                needsRedraw = true;
            }

            if (needsRedraw)
            {
                Board.SaveCurrentAnnotations();
                Board.TriggerRedraw();
            }
        };

        Viewport.FitImageRequested += (s, e) => FitImageRequested?.Invoke(this, e);
        Board.ImageLoaded += (s, e) => ImageLoaded?.Invoke(this, e);
        Board.RequestRedraw += (s, e) => RequestRedraw?.Invoke(this, e);
        Board.TextInputFocusRequested += (s, e) => TextInputFocusRequested?.Invoke(this, e);

        // Propagar cambios de propiedades de Board a bindings directos
        Board.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };

        Sidebar.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };

        Toolbar.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };

        Viewport.PropertyChanged += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                OnPropertyChanged(e.PropertyName);
                if (e.PropertyName == nameof(CanvasViewportViewModel.ZoomLevel))
                {
                    Board.TriggerRedraw();
                }
            }
        };

        Sidebar.StartWatching(() =>
        {
            Dispatcher.UIThread.Post(async () => await Sidebar.LoadSidebarImagesAsync());
        });
    }

    // --- Propiedades Delegadas (Compatibilidad XAML) ---
    public Bitmap? BackgroundImage => Board.BackgroundImage;
    public ObservableCollection<VectorShape> Shapes => Board.Shapes;
    public bool HasImage => Board.HasImage;
    public bool HasNoImage => Board.HasNoImage;
    public double ImageWidth => Board.ImageWidth;
    public double ImageHeight => Board.ImageHeight;
    public Rect? ActiveCropRect { get => Board.ActiveCropRect; set => Board.ActiveCropRect = value; }
    public bool IsEditingText => Board.IsEditingText;
    public Rect CurrentTextBounds { get => Board.CurrentTextBounds; set => Board.CurrentTextBounds = value; }
    public ITextInputShape? ActiveTextInputShape => Board.ActiveTextInputShape;
    public float ActiveTextSize => Board.ActiveTextSize;
    public string? CurrentImagePath => Board.CurrentImagePath;
    public string? CurrentImageId => Board.CurrentImageId;

    public Tool ActiveTool { get => Toolbar.ActiveTool; set => Toolbar.ActiveTool = value; }
    public Color ActiveColor { get => Toolbar.ActiveColor; set => Toolbar.ActiveColor = value; }
    public SolidColorBrush ActiveBrush => Toolbar.ActiveBrush;
    public static IReadOnlyList<Tool> AvailableTools => ToolbarViewModel.AvailableTools;
    public ObservableCollection<PaletteColorItem> AvailableColors => Toolbar.AvailableColors;
    public bool IsLineToolActive => Toolbar.IsLineToolActive;
    public bool IsArrowToolActive => Toolbar.IsArrowToolActive;
    public bool IsEllipseToolActive => Toolbar.IsEllipseToolActive;
    public bool IsRectangleToolActive => Toolbar.IsRectangleToolActive;
    public bool IsHighlighterToolActive => Toolbar.IsHighlighterToolActive;
    public bool IsTextToolActive => Toolbar.IsTextToolActive;
    public bool IsCropToolActive => Toolbar.IsCropToolActive;

    public float ZoomLevel { get => Viewport.ZoomLevel; set => Viewport.ZoomLevel = value; }
    public ObservableCollection<string> ZoomOptions => Viewport.ZoomOptions;
    public string SelectedZoomString { get => Viewport.SelectedZoomString; set => Viewport.SelectedZoomString = value; }

    public ObservableCollection<FolderItem> SidebarFolders => Sidebar.SidebarFolders;
    public NavigationItem? SelectedNode { get => Sidebar.SelectedNode; set => Sidebar.SelectedNode = value; }
    public IRelayCommand<FileItem?> OpenFileCommand => Sidebar.OpenFileCommand;
    public IRelayCommand<FileItem?> ShowInFolderCommand => Sidebar.ShowInFolderCommand;

    // --- Métodos de Delegación del Tablero y Herramientas ---
    public void StartTextInput(ITextInputShape shape) => Board.StartTextInput(shape);
    public void ClearSelection() => Board.ClearSelection();
    public void TriggerRedraw() => Board.TriggerRedraw();
    public void RotateImage()
    {
        Board.RotateImage();
        ShowToast(Constants.ToastImageRotated90, NotificationType.Info);
    }
    public void SaveCurrentAnnotations() => Board.SaveCurrentAnnotations();
    public void DeactivateCropTool() => Toolbar.DeactivateCropTool();
    public void OnBurnCompleted() => Board.OnBurnCompleted(Sidebar);

    // --- Comandos Globales ---
    [RelayCommand]
    public void DeleteSelected() => Board.DeleteSelected();

    [RelayCommand]
    public void CommitCurrentState() => Board.CommitCurrentState();

    [RelayCommand]
    public void SelectTool(string toolName) => Toolbar.SelectTool(toolName);

    public void SelectTool(Tool tool) => Toolbar.SelectTool(tool);

    [RelayCommand]
    public void SelectColor(PaletteColorItem item) => Toolbar.SelectColor(item);

    [RelayCommand]
    public async Task LoadSidebarImagesAsync() => await Sidebar.LoadSidebarImagesAsync();

    [RelayCommand]
    public void RealSize() => Viewport.RealSize();

    [RelayCommand]
    public void FitImage() => Viewport.FitImage();

    [RelayCommand]
    public void Rotate() => RotateRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void Copy() => CopyRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void Save()
    {
        if (SelectedNode is FileItem)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    public async Task CopyFile()
    {
        string? filePath = (SelectedNode as FileItem)?.FullPath ?? CurrentImagePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        if (_clipboardService != null)
        {
            try
            {
                await _clipboardService.SetFileDropListAsync(new[] { filePath });
                ShowToast(Constants.ToastFileCopied, NotificationType.Success);
            }
            catch
            {
                ShowToast(Constants.ToastFileError, NotificationType.Error);
            }
        }
    }

    public IClipboardService? ClipboardService => _clipboardService;

    public async Task CopyImageToClipboardAsync(byte[] rawPixels, int width, int height, byte[] pngBytes)
    {
        if (_clipboardService != null)
        {
            try
            {
                if (rawPixels.Length > 0 && width > 0 && height > 0)
                {
                    await _clipboardService.SetRawImageAsync(rawPixels, width, height, pngBytes);
                }
                else
                {
                    await _clipboardService.SetImageAsync(pngBytes);
                }
                ShowToast(Constants.ToastImageCopied, NotificationType.Success);
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error(ex, "Error al copiar la imagen al portapapeles.");
                ShowToast(Constants.ToastCopyError, NotificationType.Error);
            }
        }
        else
        {
            ShowToast(Constants.ToastClipboardUnavailable, NotificationType.Warning);
        }
    }

    public Task CopyImageToClipboardAsync(byte[] imageBytes)
        => CopyImageToClipboardAsync(Array.Empty<byte>(), 0, 0, imageBytes);

    [RelayCommand]
    public void OpenConfig()
    {
        try
        {
            var exeName = Qapptia.Core.Constants.ConfigExecutableName;
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
            else
            {
                ShowToast(Constants.ToastConfigNotFound, NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast(Constants.ToastConfigError, NotificationType.Error);
            Log.Error(ex, "Error opening config app from editor");
        }
    }

    private void OnCaptureStatusChanged(object? sender, bool isRunning)
    {
        _dispatcher(() =>
        {
            IsCaptureActive = isRunning;
        });
    }

    [RelayCommand]
    public async Task LaunchOrWakeCaptureAsync()
    {
        if (IsCaptureActive)
        {
            bool isStillActive = await _captureAppService.CheckStatusAsync();
            if (isStillActive)
            {
                ShowToast(Constants.ToastCaptureActive, NotificationType.Info, HorizontalAlignment.Right);
            }
            else
            {
                ShowToast(Constants.ToastCaptureError, NotificationType.Warning, HorizontalAlignment.Right);
            }
        }
        else
        {
            ShowToast(Constants.ToastCaptureLaunching, NotificationType.Info, HorizontalAlignment.Right);
            bool launched = await _captureAppService.LaunchOrWakeAsync();
            if (!launched)
            {
                ShowToast(Constants.ToastCaptureNotFound, NotificationType.Error, HorizontalAlignment.Right);
            }
        }
    }

    public void ShowToast(string message, NotificationType type, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        Serilog.Log.Information("Confirmación visual Toast: {Message} ({Type})", message, type);
        ToastAlignment = alignment;
        ToastMessage = message;
        ToastType = type switch
        {
            NotificationType.Success => ToastNotificationType.Success,
            NotificationType.Error => ToastNotificationType.Error,
            NotificationType.Warning => ToastNotificationType.Warning,
            NotificationType.Info => ToastNotificationType.Info,
            _ => ToastNotificationType.Success
        };

        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        IsToastVisible = true;

        Task.Delay(2500, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                _dispatcher(() =>
                {
                    IsToastVisible = false;
                });
            }
        }, token);
    }

    public void Dispose()
    {
        _captureAppService.StatusChanged -= OnCaptureStatusChanged;
        _captureAppService.Dispose();
        Board.Dispose();
        Sidebar.Dispose();
        _toastCts?.Dispose();
        _toastCts = null;
        GC.SuppressFinalize(this);
    }
}
