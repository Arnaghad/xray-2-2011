using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using xray.editor.avalonia_controls.Shell.Docking;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.Ide.Panels;

/// <summary>
/// Interactive Delaunay triangulation canvas rendered via Avalonia DrawingContext.
/// </summary>
public sealed class SdkTriangulationCanvas : Control
{
    public List<Point> Vertices { get; } = new();
    public bool ConstrainedMode { get; set; }
    public bool ShowBoundingTriangle { get; set; }
    public bool ShowErrors { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        var pos = e.GetPosition(this);
        if (props.IsLeftButtonPressed)
        {
            Vertices.Add(pos);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (props.IsRightButtonPressed && Vertices.Count > 0)
        {
            int nearest = -1;
            double bestDistSq = 100.0;
            for (int i = 0; i < Vertices.Count; i++)
            {
                double dx = Vertices[i].X - pos.X;
                double dy = Vertices[i].Y - pos.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 <= bestDistSq)
                {
                    bestDistSq = d2;
                    nearest = i;
                }
            }

            if (nearest >= 0)
            {
                Vertices.RemoveAt(nearest);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(SdkWorkspaceTheme.Canvas, new Rect(Bounds.Size));

        if (Vertices.Count < 3)
        {
            var hint = new FormattedText(
                "Left-click to add vertices (Right-click removes nearest).",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                12,
                SdkWorkspaceTheme.MutedText);
            context.DrawText(hint, new Point(8, 8));
        }

        if (Vertices.Count >= 3)
        {
            var tris = SdkTriangulationTesterPanel.ComputeDelaunay(Vertices, out var superTri);
            var edgePen = new Pen(SdkWorkspaceTheme.Accent, 1.2);
            foreach (var t in tris)
            {
                context.DrawLine(edgePen, Vertices[t.A], Vertices[t.B]);
                context.DrawLine(edgePen, Vertices[t.B], Vertices[t.C]);
                context.DrawLine(edgePen, Vertices[t.C], Vertices[t.A]);
            }

            if (ConstrainedMode && Vertices.Count >= 2)
            {
                var chainPen = new Pen(SdkWorkspaceTheme.Success, 1.8);
                for (int i = 0; i < Vertices.Count - 1; i++)
                    context.DrawLine(chainPen, Vertices[i], Vertices[i + 1]);
            }

            if (ShowBoundingTriangle)
            {
                var boundPen = new Pen(Brushes.Orange, 1.0, DashStyle.Dash);
                context.DrawLine(boundPen, superTri[0], superTri[1]);
                context.DrawLine(boundPen, superTri[1], superTri[2]);
                context.DrawLine(boundPen, superTri[2], superTri[0]);
            }
        }

        for (int i = 0; i < Vertices.Count; i++)
        {
            var pt = Vertices[i];
            bool dup = ShowErrors && Enumerable.Range(0, Vertices.Count).Any(j =>
                j != i && Math.Abs(Vertices[j].X - pt.X) < 1.0 && Math.Abs(Vertices[j].Y - pt.Y) < 1.0);
            context.DrawEllipse(dup ? SdkWorkspaceTheme.Warning : SdkWorkspaceTheme.Text, null, pt, 3.0, 3.0);
        }
    }
}

/// <summary>
/// Avalonia triangulation tester dock panel replacing WinForms TriangulationTester.
/// </summary>
public partial class SdkTriangulationTesterPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.TriangulationTester;

    private bool m_fileDialogPending;

    public SdkTriangulationTesterPanel()
    {
        InitializeComponent();

        m_triangulation_type.SelectionChanged += (_, _) =>
        {
            m_canvas.ConstrainedMode = m_triangulation_type.SelectedIndex == 1;
            m_canvas.InvalidateVisual();
        };
        m_show_bounding_triangle.IsCheckedChanged += (_, _) =>
        {
            m_canvas.ShowBoundingTriangle = m_show_bounding_triangle.IsChecked == true;
            m_canvas.InvalidateVisual();
        };
        m_show_errors.IsCheckedChanged += (_, _) =>
        {
            m_canvas.ShowErrors = m_show_errors.IsChecked == true;
            m_canvas.InvalidateVisual();
        };
        m_clear_vertices_button.Click += (_, _) => ClearVertices();
        m_open_button.Click += async (_, _) => await RunFileOperationAsync(BrowseAndLoadVerticesAsync);
        m_save_button.Click += async (_, _) => await RunFileOperationAsync(BrowseAndSaveVerticesAsync);
    }

    public List<Point> Vertices => m_canvas.Vertices;
    public SdkTriangulationCanvas CanvasControl => m_canvas;
    public Func<string?>? ChooseVertexFileToOpen { get; set; }
    public Func<string?>? ChooseVertexFileToSave { get; set; }
    public event Action<string>? FileOperationFailed;

    public void AddVertex(Point pt)
    {
        m_canvas.Vertices.Add(pt);
        m_canvas.InvalidateVisual();
    }

    public void ClearVertices()
    {
        m_canvas.Vertices.Clear();
        m_canvas.InvalidateVisual();
    }

    public void LoadVerticesFromFile(string filePath)
    {
        var loaded = new List<Point>();
        foreach (string line in File.ReadLines(filePath))
        {
            string[] parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                double.IsFinite(x) && double.IsFinite(y))
            {
                loaded.Add(new Point(x, y));
            }
        }

        m_canvas.Vertices.Clear();
        m_canvas.Vertices.AddRange(loaded);
        m_canvas.InvalidateVisual();
    }

    public void SaveVerticesToFile(string filePath)
    {
        File.WriteAllLines(
            filePath,
            m_canvas.Vertices.Select(v => string.Create(CultureInfo.InvariantCulture, $"{v.X} {v.Y}")));
    }

    public static List<(int A, int B, int C)> ComputeDelaunay(IReadOnlyList<Point> pts, out Point[] superTri)
    {
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
        double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double dMax = Math.Max(maxX - minX, maxY - minY);
        if (dMax < 1.0)
            dMax = 100.0;
        double midX = (minX + maxX) * 0.5, midY = (minY + maxY) * 0.5;
        superTri = new[]
        {
            new Point(midX - 20.0 * dMax, midY - dMax),
            new Point(midX, midY + 20.0 * dMax),
            new Point(midX + 20.0 * dMax, midY - dMax)
        };

        int n = pts.Count;
        var all = new List<Point>(n + 3);
        all.AddRange(pts);
        all.AddRange(superTri);
        var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };
        for (int i = 0; i < n; i++)
        {
            Point p = all[i];
            var bad = new List<(int A, int B, int C)>();
            foreach (var tri in triangles)
            {
                if (CircumcircleContains(all[tri.A], all[tri.B], all[tri.C], p))
                    bad.Add(tri);
            }

            var polygon = new List<(int U, int V)>();
            foreach (var tri in bad)
            {
                (int u, int v)[] triEdges = { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) };
                foreach (var edge in triEdges)
                {
                    bool shared = false;
                    foreach (var other in bad)
                    {
                        if (other == tri)
                            continue;
                        if ((other.A == edge.u && other.B == edge.v) || (other.B == edge.u && other.A == edge.v) ||
                            (other.B == edge.u && other.C == edge.v) || (other.C == edge.u && other.B == edge.v) ||
                            (other.C == edge.u && other.A == edge.v) || (other.A == edge.u && other.C == edge.v))
                        {
                            shared = true;
                            break;
                        }
                    }
                    if (!shared)
                        polygon.Add(edge);
                }
            }

            foreach (var tri in bad)
                triangles.Remove(tri);
            foreach (var edge in polygon)
                triangles.Add((edge.U, edge.V, i));
        }

        triangles.RemoveAll(t => t.A >= n || t.B >= n || t.C >= n);
        return triangles;
    }

    private static bool CircumcircleContains(Point a, Point b, Point c, Point p)
    {
        double ax = a.X - p.X, ay = a.Y - p.Y;
        double bx = b.X - p.X, by = b.Y - p.Y;
        double cx = c.X - p.X, cy = c.Y - p.Y;
        double orient = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                   - (bx * bx + by * by) * (ax * cy - cx * ay)
                   + (cx * cx + cy * cy) * (ax * by - bx * ay);
        return orient > 0 ? det > 1e-6 : det < -1e-6;
    }

    private async Task RunFileOperationAsync(Func<Task> operation)
    {
        if (m_fileDialogPending)
            return;

        m_fileDialogPending = true;
        try { await operation(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            FileOperationFailed?.Invoke(error.Message);
        }
        finally { m_fileDialogPending = false; }
    }

    private async Task BrowseAndLoadVerticesAsync()
    {
        string? path = ChooseVertexFileToOpen is { } choose ? choose() : await PickFileToOpenAsync();
        if (!string.IsNullOrEmpty(path))
            LoadVerticesFromFile(path);
    }

    private async Task<string?> PickFileToOpenAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage?.CanOpen != true)
            throw new InvalidOperationException("The vertex file picker is unavailable.");

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Vertices",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Vertex lists") { Patterns = new[] { "*.txt", "*.pts" } },
                FilePickerFileTypes.All
            }
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task BrowseAndSaveVerticesAsync()
    {
        string? path = ChooseVertexFileToSave is { } choose ? choose() : await PickFileToSaveAsync();
        if (!string.IsNullOrEmpty(path))
            SaveVerticesToFile(path);
    }

    private async Task<string?> PickFileToSaveAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage?.CanSave != true)
            throw new InvalidOperationException("The vertex file picker is unavailable.");

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Vertices",
            DefaultExtension = "txt",
            SuggestedFileName = "vertices.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Vertex lists") { Patterns = new[] { "*.txt" } }
            }
        });
        return file?.TryGetLocalPath();
    }
}
