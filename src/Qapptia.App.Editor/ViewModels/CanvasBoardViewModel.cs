using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qapptia.App.Editor.ViewModels.Shapes;
using Qapptia.Core.Services;
using Qapptia.Editor.Models;
using Qapptia.Editor.Models.Navigation;
using Qapptia.Editor.Services;
using Qapptia.Editor.Tools;

namespace Qapptia.App.Editor.ViewModels;

public partial class CanvasBoardViewModel : ObservableObject, IDisposable
{
    private readonly ICanvasStateService _canvasStateService;
    private readonly IEditorStateService _stateService;

    private string? _currentImagePath;
    private List<double>? _currentCrop;
    private int _currentRotation;

    [ObservableProperty]
    private Bitmap? _backgroundImage;

    public ObservableCollection<VectorShape> Shapes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoImage))]
    private bool _hasImage;

    public bool HasNoImage => !HasImage;

    [ObservableProperty]
    private double _imageWidth = 800;

    [ObservableProperty]
    private double _imageHeight = 600;

    [ObservableProperty]
    private Rect? _activeCropRect;

    [ObservableProperty]
    private bool _isEditingText;

    [ObservableProperty]
    private Rect _currentTextBounds;

    public ITextInputShape? ActiveTextInputShape { get; private set; }

    public float ActiveTextSize { get; private set; } = 24f;

    public string? CurrentImagePath => _currentImagePath;

    public string? CurrentImageId { get; private set; }

    public event EventHandler? ImageLoaded;
    public event EventHandler? RequestRedraw;
    public event EventHandler? TextInputFocusRequested;

    public CanvasBoardViewModel(
        ICanvasStateService canvasStateService,
        IEditorStateService stateService)
    {
        _canvasStateService = canvasStateService;
        _stateService = stateService;

        var state = _stateService.Load();
        ActiveTextSize = state.Tools.TextToolSize;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    public void LoadImage(FileItem file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!string.IsNullOrEmpty(_currentImagePath))
        {
            SaveCurrentAnnotations();
            _currentImagePath = null;
        }

        ActiveCropRect = null;

        try
        {
            byte[] fileBytes = File.ReadAllBytes(file.FullPath);
            var ms = new MemoryStream(fileBytes);
            var baseBitmap = new Bitmap(ms);

            var (mediaId, _, _) = ImageMetadataService.GetImageMetadata(file.FullPath);
            CurrentImageId = mediaId;

            var canvasState = _canvasStateService.Load(file.FullPath, mediaId);
            _currentRotation = canvasState.Rotation;
            _currentCrop = canvasState.Crop;

            Bitmap processedBitmap = baseBitmap;

            // 1. Restaurar rotación persistida si existe
            if (_currentRotation % 360 != 0)
            {
                processedBitmap = RotateBitmap(baseBitmap, _currentRotation);
                baseBitmap.Dispose();
            }

            // 2. Restaurar recorte persistido no destructivo si existe
            if (_currentCrop != null && _currentCrop.Count >= 4)
            {
                ActiveCropRect = new Rect(_currentCrop[0], _currentCrop[1], _currentCrop[2], _currentCrop[3]);
            }
            else
            {
                ActiveCropRect = null;
            }

            BackgroundImage?.Dispose();
            BackgroundImage = processedBitmap;

            Shapes.Clear();
            var loadedShapes = _canvasStateService.CreateShapes(canvasState.Shapes);
            foreach (var geometry in loadedShapes)
            {
                Shapes.Add(ShapeViewFactory.Wrap(geometry));
            }

            _currentImagePath = file.FullPath;

            ImageWidth = processedBitmap.Size.Width;
            ImageHeight = processedBitmap.Size.Height;
            HasImage = true;

            var state = _stateService.Load();
            state.Session.LastSelectedFile = NormalizePath(file.FullPath);
            _stateService.Save(state);

            ImageLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            ClearImage();
        }
    }

    public void ClearImage()
    {
        Shapes.Clear();
        _currentCrop = null;
        _currentRotation = 0;
        _currentImagePath = null;
        CurrentImageId = null;
        ActiveCropRect = null;
        BackgroundImage?.Dispose();
        BackgroundImage = null;
        HasImage = false;
    }

    public void SaveCurrentAnnotations()
    {
        if (string.IsNullOrEmpty(_currentImagePath)) return;

        var state = new CanvasState
        {
            MediaId = CurrentImageId,
            MediaType = Qapptia.Core.Constants.ResolveMediaType(_currentImagePath),
            Crop = _currentCrop,
            Rotation = _currentRotation,
            Shapes = _canvasStateService.CreateDtos(Shapes.Select(s => s.Geometry))
        };

        _canvasStateService.Save(state, _currentImagePath);
    }

    partial void OnActiveCropRectChanged(Rect? value)
    {
        if (value.HasValue)
        {
            var r = value.Value;
            _currentCrop = new List<double> { r.X, r.Y, r.Width, r.Height };
        }
        else
        {
            _currentCrop = null;
        }
        SaveCurrentAnnotations();
    }

    public void RotateImage()
    {
        if (BackgroundImage == null) return;

        CommitCurrentState();

        var oldBmp = BackgroundImage;
        int w = oldBmp.PixelSize.Width;
        int h = oldBmp.PixelSize.Height;

        var rtb = new RenderTargetBitmap(new PixelSize(h, w), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var transform = Matrix.CreateTranslation(0, 0) * Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(h, 0);
            using (ctx.PushTransform(transform))
            {
                ctx.DrawImage(oldBmp, new Rect(0, 0, w, h));
            }
        }

        BackgroundImage?.Dispose();
        BackgroundImage = rtb;
        ImageWidth = h;
        ImageHeight = w;

        _currentRotation = (_currentRotation + 90) % 360;

        RotateTool.RotateScene90Clockwise(Shapes.Select(s => s.Geometry), h);

        SaveCurrentAnnotations();
        TriggerRedraw();
    }

    private static Bitmap RotateBitmap(Bitmap src, int degrees)
    {
        int times = (degrees % 360) / 90;
        var current = src;
        for (int i = 0; i < times; i++)
        {
            int w = current.PixelSize.Width;
            int h = current.PixelSize.Height;
            var rtb = new RenderTargetBitmap(new PixelSize(h, w), new Vector(96, 96));
            using (var ctx = rtb.CreateDrawingContext())
            {
                var transform = Matrix.CreateTranslation(0, 0) * Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateTranslation(h, 0);
                using (ctx.PushTransform(transform))
                {
                    ctx.DrawImage(current, new Rect(0, 0, w, h));
                }
            }
            if (current != src) current.Dispose();
            current = rtb;
        }
        return current;
    }

    public void TriggerRedraw()
    {
        RequestRedraw?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        foreach (var shape in Shapes)
        {
            shape.IsSelected = false;
        }
    }

    public void SetBurningMode(bool isBurning)
    {
        foreach (var shape in Shapes)
        {
            shape.IsBurning = isBurning;
        }
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (IsEditingText) return;

        var selected = Shapes.Where(s => s.IsSelected).ToList();
        if (selected.Count > 0)
        {
            foreach (var shape in selected)
            {
                Shapes.Remove(shape);
            }
            SaveCurrentAnnotations();
            RequestRedraw?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StartTextInput(ITextInputShape shape)
    {
        if (IsEditingText)
        {
            CommitCurrentState();
        }

        ActiveTextInputShape = shape;
        ActiveTextInputShape.FocusRequested += OnActiveShapeFocusRequested;
        shape.IsEditing = true;
        shape.CaretIndex = shape.Text.Length;
        shape.IsCaretVisible = true;

        CurrentTextBounds = shape.TextBounds;
        IsEditingText = true;
        RequestRedraw?.Invoke(this, EventArgs.Empty);
        shape.RequestFocus();
    }

    [RelayCommand]
    public void CommitCurrentState()
    {
        if (IsEditingText && ActiveTextInputShape != null)
        {
            ActiveTextInputShape.FocusRequested -= OnActiveShapeFocusRequested;
            ActiveTextInputShape.IsEditing = false;

            if (ActiveTextInputShape.IsEmpty && ActiveTextInputShape is VectorShape vectorShape)
            {
                Shapes.Remove(vectorShape);
            }

            IsEditingText = false;

            if (ActiveTextInputShape.TextSize != ActiveTextSize)
            {
                ActiveTextSize = ActiveTextInputShape.TextSize;
                var state = _stateService.Load();
                state.Tools.TextToolSize = ActiveTextSize;
                _stateService.Save(state);
            }

            ActiveTextInputShape = null;
            ClearSelection();
            SaveCurrentAnnotations();
            RequestRedraw?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ClearSelection();
            RequestRedraw?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnActiveShapeFocusRequested(object? sender, EventArgs e)
    {
        TextInputFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void OnBurnCompleted(SidebarViewModel sidebar)
    {
        if (string.IsNullOrEmpty(_currentImagePath)) return;

        Shapes.Clear();
        ActiveCropRect = null;
        _currentRotation = 0;

        _canvasStateService.Save(new CanvasState
        {
            MediaId = CurrentImageId,
            MediaType = Qapptia.Core.Constants.ResolveMediaType(_currentImagePath)
        }, _currentImagePath);

        string path = _currentImagePath;
        sidebar.SelectedNode = null;

        var nodeToSelect = sidebar.FindNodeByPath(path);
        if (nodeToSelect != null)
        {
            sidebar.SelectedNode = nodeToSelect;
        }
    }

    public void Dispose()
    {
        BackgroundImage?.Dispose();
        BackgroundImage = null;
        GC.SuppressFinalize(this);
    }
}
