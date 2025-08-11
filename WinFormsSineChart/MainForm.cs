using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public class MainForm : Form
{
    private readonly List<int> values = new List<int>();
    private string? loadedPath;

    private readonly MenuStrip menuStrip = new MenuStrip();
    private readonly ToolStripMenuItem fileMenu = new ToolStripMenuItem("File");
    private readonly ToolStripMenuItem openItem = new ToolStripMenuItem("Open...");
    private readonly ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");

    public MainForm(string? initialPath)
    {
        Text = "Sine Values Chart";
        Width = 1000;
        Height = 600;
        DoubleBuffered = true;
        BackColor = Color.White;
        AllowDrop = true;

        openItem.Click += (_, __) => OpenFile();
        exitItem.Click += (_, __) => Close();
        fileMenu.DropDownItems.Add(openItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exitItem);
        menuStrip.Items.Add(fileMenu);
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Resize += (_, __) => Invalidate();
        Shown += (_, __) =>
        {
            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            {
                TryLoadFile(initialPath);
            }
            else
            {
                // Prompt to open on first run
                OpenFile();
            }
        };
    }

    private void OpenFile()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Open values file",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            RestoreDirectory = true
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            TryLoadFile(ofd.FileName);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                TryLoadFile(files[0]);
            }
        }
    }

    private void TryLoadFile(string path)
    {
        try
        {
            var loaded = LoadIntegersFromFile(path);
            if (loaded.Count == 0)
            {
                MessageBox.Show(this, "No integer values found in file.", "Load File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            values.Clear();
            values.AddRange(loaded);
            loadedPath = path;
            Text = $"Sine Values Chart - {Path.GetFileName(path)} ({values.Count} points)";
            Invalidate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static List<int> LoadIntegersFromFile(string path)
    {
        // Accepts JSON-like array with commas and brackets, one-per-line or CSV
        string text = File.ReadAllText(path);
        var matches = Regex.Matches(text, @"-?\d+");
        return matches.Select(m => int.Parse(m.Value)).ToList();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        Rectangle client = ClientRectangle;
        client.Y += menuStrip.Height;
        client.Height -= menuStrip.Height;

        if (values.Count < 2)
        {
            DrawCenteredMessage(g, client, "Open a values file (File > Open)\nOr drag-and-drop the file onto this window.");
            return;
        }

        int leftMargin = 60;
        int rightMargin = 20;
        int topMargin = 20;
        int bottomMargin = 40;
        Rectangle plotArea = new Rectangle(
            client.Left + leftMargin,
            client.Top + topMargin,
            Math.Max(1, client.Width - leftMargin - rightMargin),
            Math.Max(1, client.Height - topMargin - bottomMargin)
        );

        using var axisPen = new Pen(Color.Black, 1.5f);
        using var gridPen = new Pen(Color.LightGray, 1f) { DashStyle = DashStyle.Dot };
        using var linePen = new Pen(Color.RoyalBlue, 2f);
        using var textBrush = new SolidBrush(Color.Black);
        using var font = new Font("Segoe UI", 9f);

        // Draw axes box
        g.DrawRectangle(axisPen, plotArea);

        int minY = values.Min();
        int maxY = values.Max();
        if (minY == maxY) { minY -= 1; maxY += 1; }

        // Grid lines and labels (Y: 5 divisions)
        int divisions = 5;
        for (int i = 0; i <= divisions; i++)
        {
            float y = plotArea.Top + i * (plotArea.Height / (float)divisions);
            g.DrawLine(gridPen, plotArea.Left, y, plotArea.Right, y);
            int labelValue = (int)Math.Round(maxY - i * (maxY - minY) / (double)divisions);
            string label = labelValue.ToString();
            SizeF sz = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, plotArea.Left - 8 - sz.Width, y - sz.Height / 2f);
        }

        // X grid (every 24 samples if present)
        int samplesPerCycleGuess = 24; // heuristic for visual grid
        if (values.Count > 1)
        {
            for (int i = 0; i < values.Count; i += samplesPerCycleGuess)
            {
                float x = plotArea.Left + i * (plotArea.Width - 1) / (float)(values.Count - 1);
                g.DrawLine(gridPen, x, plotArea.Top, x, plotArea.Bottom);
            }
        }

        // Map function from data to screen
        float ScaleX(int index)
            => plotArea.Left + index * (plotArea.Width - 1) / (float)(values.Count - 1);
        float ScaleY(int value)
            => plotArea.Top + (maxY - value) * (plotArea.Height - 1) / (float)(maxY - minY);

        // Draw polyline
        if (values.Count >= 2)
        {
            PointF[] points = new PointF[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                points[i] = new PointF(ScaleX(i), ScaleY(values[i]));
            }
            g.DrawLines(linePen, points);
        }

        // Title
        string title = loadedPath != null ? $"{Path.GetFileName(loadedPath)}" : "(no file)";
        using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        SizeF titleSize = g.MeasureString(title, titleFont);
        g.DrawString(title, titleFont, textBrush, plotArea.Left + (plotArea.Width - titleSize.Width) / 2f, plotArea.Top - titleSize.Height - 2);
    }

    private static void DrawCenteredMessage(Graphics g, Rectangle area, string message)
    {
        using var font = new Font("Segoe UI", 10f);
        using var brush = new SolidBrush(Color.Gray);
        StringFormat fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(message, font, brush, area, fmt);
    }
}