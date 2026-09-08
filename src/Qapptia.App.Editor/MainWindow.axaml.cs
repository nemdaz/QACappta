using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Qapptia.App.Editor.ViewModels;
using Qapptia.App.Editor.Services;
using Qapptia.App.Editor.Common;
using Qapptia.Editor.Models.Navigation;

namespace Qapptia.App.Editor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InitializeLayoutHandlers();
        InitializeWindowEvents();
        InitializeScrollHandlers();
    }

    private void InitializeLayoutHandlers()
    {
        bool isInitialSizeSet = false;
        this.SizeChanged += (s, e) =>
        {
            var grid = this.FindControl<Grid>("MainGrid");
            if (grid == null || grid.ColumnDefinitions.Count < 3) return;

            var sidebarCol = grid.ColumnDefinitions[2];
            var width = e.NewSize.Width;
            sidebarCol.MinWidth = width * 0.10; // Mínimo 10%
            sidebarCol.MaxWidth = width * 0.50; // Máximo 50%

            if (!isInitialSizeSet && width > 0)
            {
                sidebarCol.Width = new GridLength(width * 0.30);
                isInitialSizeSet = true;
            }
            else
            {
                if (sidebarCol.Width.Value < sidebarCol.MinWidth)
                    sidebarCol.Width = new GridLength(sidebarCol.MinWidth);
                else if (sidebarCol.Width.Value > sidebarCol.MaxWidth)
                    sidebarCol.Width = new GridLength(sidebarCol.MaxWidth);
            }
        };
    }

    private void InitializeWindowEvents()
    {
        // Guardar edición pendiente al cerrar ventana
        this.Closing += (s, e) =>
        {
            if (DataContext is EditorViewModel currentVm && currentVm.IsEditingText)
            {
                currentVm.CommitCurrentState();
            }
        };
    }

    private void InitializeScrollHandlers()
    {
        // Evitar desplazamientos no deseados del ScrollViewer al hacer foco o clic en el lienzo
        var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
        scrollViewer?.AddHandler(Control.RequestBringIntoViewEvent, (s, e) =>
        {
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble);

        // Registrar evento de rueda del ratón con Tunnel para interceptarlo antes que el ScrollViewer
        this.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, EditorScrollViewer_PointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public void InitializeWithViewModel(EditorViewModel vm)
    {
        DataContext = vm;

        SubscribeToViewModelEvents(vm);
        InitializeKeyBindings(vm);

        vm.LoadSidebarImagesCommand.Execute(null);
    }

    private void SubscribeToViewModelEvents(EditorViewModel vm)
    {
        vm.CopyRequested += Vm_CopyRequested;
        vm.RotateRequested += Vm_RotateRequested;
        vm.SaveRequested += Vm_SaveRequested;
        vm.FitImageRequested += Vm_FitImageRequested;
    }

    private void InitializeKeyBindings(EditorViewModel vm)
    {
        this.KeyBindings.Add(new Avalonia.Input.KeyBinding
        {
            Gesture = Avalonia.Input.KeyGesture.Parse(Qapptia.Core.Constants.ShortcutCopyClipboard),
            Command = vm.CopyCommand
        });

        this.KeyBindings.Add(new Avalonia.Input.KeyBinding
        {
            Gesture = Avalonia.Input.KeyGesture.Parse(Qapptia.Core.Constants.ShortcutCopyFile),
            Command = vm.CopyFileCommand
        });

        this.KeyBindings.Add(new Avalonia.Input.KeyBinding
        {
            Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Delete),
            Command = vm.DeleteSelectedCommand
        });
    }

    private void Vm_FitImageRequested(object? sender, EventArgs e)
    {
        if (DataContext is EditorViewModel vm && vm.HasImage)
        {
            var scrollViewer = this.FindControl<ScrollViewer>("EditorScrollViewer");
            if (scrollViewer != null && vm.ImageWidth > 0 && vm.ImageHeight > 0)
            {
                // Dejamos un pequeño margen para que no choque con los bordes (por ejemplo, restar 64px del layout)
                double availableWidth = scrollViewer.Bounds.Width - 64;
                double availableHeight = scrollViewer.Bounds.Height - 64;

                if (availableWidth <= 0) availableWidth = 100;
                if (availableHeight <= 0) availableHeight = 100;

                double scaleX = availableWidth / vm.ImageWidth;
                double scaleY = availableHeight / vm.ImageHeight;

                float fitZoom = (float)Math.Min(scaleX, scaleY);
                if (fitZoom <= 0) fitZoom = 0.1f;
                if (fitZoom > 99.99f) fitZoom = 99.99f; // Max 9999%

                vm.ZoomLevel = fitZoom;
            }
        }
    }

    private void EditorScrollViewer_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && DataContext is EditorViewModel vm)
        {
            float currentZoom = vm.ZoomLevel;
            float step = currentZoom < 1.0f ? 0.1f : 0.25f;
            int direction = e.Delta.Y > 0 ? 1 : -1;

            float newZoom = currentZoom + (step * direction);

            // Clamp between min and max (0.1 to 99.99)
            newZoom = Math.Max(0.1f, Math.Min(newZoom, 99.99f));

            if (Math.Abs(newZoom - currentZoom) > 0.01f)
            {
                vm.ZoomLevel = newZoom;
            }

            // Mark as handled to prevent the ScrollViewer from scrolling up/down
            e.Handled = true;
        }
    }

    private void Vm_CopyRequested(object? sender, EventArgs e)
    {
        if (DataContext is not EditorViewModel vm || vm.BackgroundImage == null || vm.ImageWidth <= 0 || vm.ImageHeight <= 0)
        {
            return;
        }

        Serilog.Log.Information("Copiando contenido del tablero al portapapeles...");

        vm.CommitCurrentState();

        var (rawPixels, width, height, pngBytes) = BoardImageExporter.ExportBurnedImage(
            vm.BackgroundImage,
            vm.Shapes.ToList(),
            vm.ActiveCropRect,
            vm.ImageWidth,
            vm.ImageHeight);

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await vm.CopyImageToClipboardAsync(rawPixels, width, height, pngBytes);
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error(ex, "Error al serializar imagen para portapapeles.");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    vm.ShowToast(Constants.ToastCopyError, Qapptia.Editor.Models.NotificationType.Error);
                });
            }
        });
    }

    private void Vm_RotateRequested(object? sender, EventArgs e)
    {
        if (DataContext is EditorViewModel vm)
        {
            vm.RotateImage();
        }
    }

    private void Vm_SaveRequested(object? sender, EventArgs e)
    {
        if (DataContext is not EditorViewModel vm || vm.SelectedNode is not FileItem fileNode || vm.BackgroundImage == null || vm.ImageWidth <= 0 || vm.ImageHeight <= 0)
        {
            return;
        }

        string filePath = fileNode.FullPath;
        string? mediaId = vm.CurrentImageId;
        Serilog.Log.Information("Guardando imagen del tablero en {Path}...", filePath);

        if (string.IsNullOrEmpty(mediaId))
        {
            var (newId, _, _) = Qapptia.Core.Services.ImageMetadataService.EnsureImageMetadata(filePath);
            mediaId = newId;
        }

        vm.CommitCurrentState();

        var (_, _, _, pngBytes) = BoardImageExporter.ExportBurnedImage(
            vm.BackgroundImage,
            vm.Shapes.ToList(),
            vm.ActiveCropRect,
            vm.ImageWidth,
            vm.ImageHeight);

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                // 1. Crear backup comprimido seguro (.bak.gz)
                await Qapptia.Core.Services.ImageBurnService.CreateCompressedBackupAsync(filePath, mediaId);

                // 2. Persistir imagen quemada y preservar metadatos de medio
                await Qapptia.Core.Services.ImageBurnService.SaveBurnedImageAsync(filePath, pngBytes, mediaId);

                // 3. Limpiar UI y notificar
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    vm.OnBurnCompleted();
                    vm.ShowToast(Constants.ToastImageSaved, Qapptia.Editor.Models.NotificationType.Success);
                });
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error(ex, "Error al guardar la imagen quemada.");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    vm.ShowToast($"{Constants.ToastSaveErrorPrefix}{ex.Message}", Qapptia.Editor.Models.NotificationType.Error);
                });
            }
        });
    }

    private void FileItem_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && sender is Avalonia.Controls.Control { DataContext: FileItem fileItem })
        {
            if (DataContext is EditorViewModel vm && vm.SelectedNode != fileItem)
            {
                vm.SelectedNode = fileItem;
            }
        }
    }
}
