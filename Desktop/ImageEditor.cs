using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SilverpineSilcEditor.Core;
using System.Buffers.Binary;

namespace SilverpineSilcEditor.Desktop;

internal sealed class ImageEditor : UserControl, IDisposable
{
    private readonly PlayerDefinitionModel _model;
    private readonly EditState _state;
    private readonly ListBox _list = new();
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(18) };
    private readonly TextBlock _info = Ui.Note("");
    private readonly CheckBox _combined = new() { Content = "Preview expression on portrait", IsChecked = true };
    private Bitmap? _bitmap;
    internal ImageEditor(PlayerDefinitionModel model, EditState state)
    {
        _model = model; _state = state;
        _list.SelectionChanged += (_, _) => Preview(); _combined.IsCheckedChanged += (_, _) => Preview();
        var buttons = Ui.Row(Ui.AsyncButton("Replace PNG", Replace), Ui.AsyncButton("Export PNG", Export), Ui.AsyncButton("Add expression", AddExpression), Ui.AsyncButton("Rename", Rename), Ui.Button("Remove expression", Remove), Ui.AsyncButton("Add small nude", AddNude));
        Content = Ui.Dock(buttons, Ui.Split(_list, Ui.Dock(Ui.Stack(_info, _combined), new Border { Background = Brush.Parse("#0C1216"), Child = _preview })), Ui.Note("PNG imports retain their original bytes. Expression previews replace the top strip of the portrait, just like the game.")); Refresh(model.Images.FirstOrDefault());
    }
    private PlayerImageAsset? Selected => _list.SelectedItem as PlayerImageAsset;
    private void Refresh(PlayerImageAsset? item) { _list.ItemsSource = null; _list.ItemsSource = _model.Images.ToArray(); _list.SelectedItem = item; }
    internal static PixelSize PngSize(byte[] bytes)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Select a PNG image.");
        int w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384 || (long)w * h > 64_000_000) throw new InvalidDataException("Image dimensions exceed the preview safety limit.");
        return new(w, h);
    }
    internal static Bitmap Decode(byte[] bytes) { PngSize(bytes); return new Bitmap(new MemoryStream(bytes, false)); }
    internal static void ValidateExpression(byte[] expression, byte[] portrait)
    {
        PixelSize a = PngSize(expression), b = PngSize(portrait);
        if (a.Width != b.Width || a.Height > b.Height) throw new InvalidDataException("Like the game editor, expressions must have the same width as the Dialog portrait and cannot be taller than it.");
    }
    internal static RenderTargetBitmap Combine(byte[] expression, byte[] portrait)
    {
        ValidateExpression(expression, portrait); using Bitmap face = Decode(expression); using Bitmap body = Decode(portrait);
        var result = new RenderTargetBitmap(body.PixelSize, new Vector(96, 96));
        using (DrawingContext dc = result.CreateDrawingContext())
        {
            int h = face.PixelSize.Height; int width = body.PixelSize.Width, height = body.PixelSize.Height;
            // Draw only the remaining bottom portion; transparency in the expression erases the old face.
            if (height > h) dc.DrawImage(body, new Rect(0, h, width, height - h), new Rect(0, h, width, height - h));
            dc.DrawImage(face, new Rect(0, 0, width, h));
        }
        return result;
    }
    private void Preview()
    {
        _preview.Source = null; _bitmap?.Dispose(); _bitmap = null;
        if (Selected is not { } image) return;
        try
        {
            PixelSize size = PngSize(image.Data); byte[]? portrait = _model.Images.FirstOrDefault(i => i.Category == PlayerImageCategory.Fixed && i.Name == "Dialog")?.Data;
            _bitmap = image.Category == PlayerImageCategory.Expression && _combined.IsChecked == true && portrait is not null ? Combine(image.Data, portrait) : Decode(image.Data);
            _preview.Source = _bitmap; _info.Text = $"{image.DisplayName} · {size.Width} × {size.Height} · {image.Data.Length:N0} bytes";
        }
        catch (Exception e) { _info.Text = "Preview unavailable: " + e.Message + " Existing data is unchanged."; }
    }
    private async Task<byte[]?> Import()
    {
        string? file = await Ui.PickFile(Ui.CurrentWindow!, "png", "Import PNG"); if (file is null) return null;
        if (new FileInfo(file).Length > 128 * 1024 * 1024) throw new InvalidDataException("PNG exceeds the 128 MiB import limit.");
        byte[] bytes = await File.ReadAllBytesAsync(file); using Bitmap validation = Decode(bytes); return bytes;
    }
    private async Task Replace()
    {
        if (Selected is not { } image || await Import() is not { } bytes) return;
        if (image.Category == PlayerImageCategory.Expression) ValidateExpression(bytes, _model.Images.Single(i => i.Category == PlayerImageCategory.Fixed && i.Name == "Dialog").Data);
        if (image.Category == PlayerImageCategory.Fixed && image.Name == "Dialog") foreach (var expression in _model.Images.Where(i => i.Category == PlayerImageCategory.Expression)) ValidateExpression(expression.Data, bytes);
        image.Data = bytes; _state.Changed(); Preview();
    }
    private async Task Export()
    {
        if (Selected is not { } image) return;
        string name = string.Concat(image.Name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        string? path = await Ui.SaveFile(Ui.CurrentWindow!, name + ".png", "png"); if (path is not null) await File.WriteAllBytesAsync(path, image.Data);
    }
    private async Task AddExpression()
    {
        if (_model.Images.Count(i => i.Category == PlayerImageCategory.Expression) >= 16) throw new InvalidDataException("The game editor allows up to 16 expressions.");
        if (await Import() is not { } bytes) return;
        ValidateExpression(bytes, _model.Images.Single(i => i.Category == PlayerImageCategory.Fixed && i.Name == "Dialog").Data);
        string? name = await Ui.Prompt(Ui.CurrentWindow!, "Expression name"); if (string.IsNullOrWhiteSpace(name)) return;
        if (_model.Images.Any(i => i.Category == PlayerImageCategory.Expression && i.Name == name)) throw new InvalidDataException("An expression with that name already exists.");
        var item = new PlayerImageAsset(PlayerImageCategory.Expression, name, _model.Images.Count, bytes); _model.Images.Add(item); Refresh(item); _state.Changed();
    }
    private async Task Rename()
    {
        if (Selected is not { Category: PlayerImageCategory.Expression } image) return;
        string? name = await Ui.Prompt(Ui.CurrentWindow!, "Expression name", image.Name); if (string.IsNullOrWhiteSpace(name) || name == image.Name) return;
        if (_model.Images.Any(i => i != image && i.Category == PlayerImageCategory.Expression && i.Name == name)) throw new InvalidDataException("An expression with that name already exists.");
        image.Name = name; Refresh(image); _state.Changed();
    }
    private void Remove() { if (Selected is not { Category: PlayerImageCategory.Expression } image) return; _model.Images.Remove(image); Refresh(_model.Images.FirstOrDefault()); _state.Changed(); }
    private async Task AddNude()
    {
        string? answer = await Ui.Choose(Ui.CurrentWindow!, "Which sex is this small nude sprite for?", "Male", "Female", "Intersex", "Cancel");
        int sex = answer switch { "Male" => 0, "Female" => 1, "Intersex" => 2, _ => -1 }; if (sex < 0 || await Import() is not { } bytes) return;
        PlayerImageAsset? item = _model.Images.FirstOrDefault(i => i.Category == PlayerImageCategory.SmallNude && i.NumericKey == sex);
        if (item is null) { item = new(PlayerImageCategory.SmallNude, $"Small nude ({answer})", sex, bytes); _model.Images.Add(item); } else item.Data = bytes;
        Refresh(item); _state.Changed();
    }
    public void Dispose() { _preview.Source = null; _bitmap?.Dispose(); _bitmap = null; }
}
