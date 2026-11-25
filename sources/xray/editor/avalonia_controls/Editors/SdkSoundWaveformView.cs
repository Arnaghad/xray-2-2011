using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

/// <summary>
/// Pure Avalonia DrawingContext control rendering PCM/float mono WAV amplitude envelopes, playhead, and RMS meter.
/// </summary>
public sealed class SdkSoundWaveformView : Control
{
    private SdkWavAudioData? m_audioData;
    private double m_playheadSeconds;
    private float m_currentRms;
    private string m_label = "No audio loaded";

    public SdkSoundWaveformView()
    {
        ClipToBounds = true;
        MinHeight = 72;
    }

    public SdkWavAudioData? AudioData
    {
        get => m_audioData;
        set
        {
            m_audioData = value;
            InvalidateVisual();
        }
    }

    public double PlayheadSeconds
    {
        get => m_playheadSeconds;
        set
        {
            if (Math.Abs(m_playheadSeconds - value) < 1e-5)
                return;
            m_playheadSeconds = value;
            InvalidateVisual();
        }
    }

    public float CurrentRms
    {
        get => m_currentRms;
        set
        {
            if (MathF.Abs(m_currentRms - value) < 1e-4f)
                return;
            m_currentRms = value;
            InvalidateVisual();
        }
    }

    public string Label
    {
        get => m_label;
        set
        {
            m_label = value ?? string.Empty;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 2 || rect.Height <= 2)
            return;

        context.FillRectangle(SdkWorkspaceTheme.Surface, rect);
        context.DrawRectangle(new Pen(SdkWorkspaceTheme.Divider, 1), rect.Deflate(0.5));

        double waveHeight = Math.Max(16, rect.Height - 14);
        double midY = waveHeight * 0.5;
        double amplitudeScale = Math.Max(4.0, midY - 6.0);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
        context.DrawLine(gridPen, new Point(0, midY), new Point(rect.Width, midY));
        context.DrawLine(gridPen, new Point(0, midY - amplitudeScale * 0.5), new Point(rect.Width, midY - amplitudeScale * 0.5));
        context.DrawLine(gridPen, new Point(0, midY + amplitudeScale * 0.5), new Point(rect.Width, midY + amplitudeScale * 0.5));

        int widthPx = Math.Max(1, (int)Math.Floor(rect.Width));
        if (m_audioData != null && m_audioData.MonoSamples.Length > 0)
        {
            var samples = m_audioData.MonoSamples;
            int total = samples.Length;
            var wavePen = new Pen(new SolidColorBrush(Color.FromRgb(88, 208, 158)), 1);
            var peakPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 122, 232, 190)), 1);

            for (int x = 0; x < widthPx; x++)
            {
                int i0 = (int)((long)x * total / widthPx);
                int i1 = (int)((long)(x + 1) * total / widthPx);
                if (i1 <= i0) i1 = i0 + 1;
                if (i1 > total) i1 = total;

                float minS = 1f;
                float maxS = -1f;
                for (int i = i0; i < i1; i++)
                {
                    float s = samples[i];
                    if (s < minS) minS = s;
                    if (s > maxS) maxS = s;
                }

                double yTop = midY - Math.Clamp(maxS, -1f, 1f) * amplitudeScale;
                double yBot = midY - Math.Clamp(minS, -1f, 1f) * amplitudeScale;
                if (Math.Abs(yBot - yTop) < 1.0)
                    context.DrawLine(peakPen, new Point(x + 0.5, midY - 0.5), new Point(x + 0.5, midY + 0.5));
                else
                    context.DrawLine(wavePen, new Point(x + 0.5, yTop), new Point(x + 0.5, yBot));
            }

            if (m_audioData.DurationSeconds > 0.0)
            {
                double frac = Math.Clamp(m_playheadSeconds / m_audioData.DurationSeconds, 0.0, 1.0);
                double px = frac * rect.Width;
                context.DrawLine(new Pen(SdkWorkspaceTheme.Accent, 1.5), new Point(px, 0), new Point(px, waveHeight));
            }
        }

        // Bottom RMS level meter strip
        var meterTrack = new Rect(4, rect.Height - 10, Math.Max(0, rect.Width - 8), 6);
        context.FillRectangle(SdkWorkspaceTheme.Chrome, meterTrack);
        double rmsWidth = Math.Clamp(m_currentRms, 0f, 1f) * meterTrack.Width;
        if (rmsWidth > 0.5)
        {
            context.FillRectangle(
                new SolidColorBrush(Color.FromRgb(95, 210, 140)),
                new Rect(meterTrack.X, meterTrack.Y, rmsWidth, meterTrack.Height));
        }

        var labelText = new FormattedText(
            m_label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(SdkWorkspaceTheme.FontFamilyName),
            11,
            SdkWorkspaceTheme.MutedText);
        context.DrawText(labelText, new Point(6, 4));
    }
}
