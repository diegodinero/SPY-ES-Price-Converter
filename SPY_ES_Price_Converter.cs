// Copyright QUANTOWER LLC. © 2017-2025. All rights reserved.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;

#nullable disable
namespace SPY_ES_Price_Converter
{
    public class SPY_ES_Price_Converter : Indicator
    {
        //── USER INPUTS ─────────────────────────────────────────────────────────
        [InputParameter("ETF Symbol", 0)]
        public string TestSymbolName { get; set; } = "SPY";

        [InputParameter("Strike Half-Width", 1, 1, 100, 1)]
        public int StrikeWidth { get; set; } = 7;

        [InputParameter("Line Colour", 2)]
        public Color LineColor { get; set; } = Color.Transparent;  

        [InputParameter(
            "Line Style", 3,
            -2147483648, 2147483647, 1, 0,
            new object[]{
                "Solid",       0,
                "Dash",        1,
                "Dot",         2,
                "DashDot",     3,
                "DashDotDot",  4
            })]
        public int LineStylePreset { get; set; } = 1;   // Dash

        [InputParameter("Line Thickness", 4, 1, 10, 1)]
        public int LineThickness { get; set; } = 1;   // thickness = 1

        [InputParameter("Label Colour", 5)]
        public Color LabelColor { get; set; } = Color.FromArgb(76, 175, 80);  // #4caf50

        [InputParameter("Label BG Colour", 6)]
        public Color LabelBgColor { get; set; } = Color.Transparent;
        [InputParameter("Horizontal Offset (px)", 7, -500, 500, 1)]
        public int HorizontalOffset { get; set; } = -20;

        //── RESOLVED SYMBOLS & HISTORY ────────────────────────────────────────────────
        private Symbol _testSymbol;
        private List<HistoryItemBar> _historyTest1m;
        private List<HistoryItemBar> _historyFut1m;

        //── FONTS & BRUSHES ────────────────────────────────────────────────────────
        private Font _debugFont;
        private SolidBrush _debugBrush;
        private Font _labelFont;
        private SolidBrush _labelTextBrush;
        private SolidBrush _labelBgBrush;

        public SPY_ES_Price_Converter() : base()
        {
            Name = "SPY_ES_Price_Converter";
            Description = "Maps Test-symbol open onto futures open";
            SeparateWindow = false;
            OnBackGround = true;
        }

        protected override void OnInit()
        {
            base.OnInit();

            // lookup test symbol
            _testSymbol = Core.Instance.Symbols
                .FirstOrDefault(s =>
                    s.Name.Equals(TestSymbolName, StringComparison.OrdinalIgnoreCase)
                );
            if (_testSymbol == null)
                throw new Exception($"Could not find symbol '{TestSymbolName}'.");

            // grab 1-min bars for test and futures
            _historyTest1m = _testSymbol
                .GetHistory(Period.MIN1, HistoryType.Last, DateTime.UtcNow.AddDays(-1))
                .OfType<HistoryItemBar>().ToList();
            _historyFut1m = this.Symbol
                .GetHistory(Period.MIN1, HistoryType.Last, DateTime.UtcNow.AddDays(-1))
                .OfType<HistoryItemBar>().ToList();

            // prepare GDI
            _debugFont = new Font("Arial", 10, FontStyle.Bold);
            _debugBrush = new SolidBrush(Color.White);
            _labelFont = new Font("Arial", 8, FontStyle.Bold);
            _labelTextBrush = new SolidBrush(LabelColor);
            _labelBgBrush = new SolidBrush(LabelBgColor);
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            var gr = args.Graphics;
            var rect = CurrentChart.Windows[args.WindowIndex].ClientRectangle;
            var conv = CurrentChart.Windows[args.WindowIndex].CoordinatesConverter;
            int offX = HorizontalOffset;

            // most-recent 1m OPENs
            double futOpen = _historyFut1m.Any() ? _historyFut1m.Last().Open : double.NaN;
            double testOpen = _historyTest1m.Any() ? _historyTest1m.Last().Open : double.NaN;
            double ratio = (futOpen > 0 && testOpen > 0) ? futOpen / testOpen : double.NaN;

            // debug info
            var debugLines = new[]
            {
                ""
                //$"Test: {_testSymbol.Name}",
                //$"Fut Open: {(double.IsNaN(futOpen)  ? "n/a" : futOpen.ToString("F2"))}",
                //$"Test Open: {(double.IsNaN(testOpen)? "n/a" : testOpen.ToString("F2"))}",
                //$"Bars: {_historyTest1m.Count}",
                //$"Ratio: {(double.IsNaN(ratio)       ? "n/a" : ratio.ToString("F4"))}"
            };
            const int margin = 5;
            float yDebug = rect.Top + margin;
            foreach (var line in debugLines)
            {
                var size = gr.MeasureString(line, _debugFont);
                float x = rect.Right - size.Width - margin;
                gr.DrawString(line, _debugFont, _debugBrush, x, yDebug);
                yDebug += size.Height + 2;
            }

            if (double.IsNaN(ratio))
                return;

            // draw strike lines + labels
            using var pen = new Pen(LineColor, LineThickness);
            pen.DashStyle = (DashStyle)LineStylePreset;
            gr.SetClip(rect);

            int baseStrike = (int)testOpen;
            for (int i = baseStrike - StrikeWidth; i <= baseStrike + StrikeWidth; i++)
            {
                double level = this.Symbol.RoundPriceToTickSize(i * ratio, CurrentChart.TickSize);
                float yLine = (float)conv.GetChartY(level);
                if (yLine < rect.Top || yLine > rect.Bottom)
                    continue;

                // strike line
                gr.DrawLine(pen,
           rect.Left + offX, yLine,
           rect.Right + offX, yLine);

                // label text
                string label = $"{_testSymbol.Name} {i}";
                var lblSz = gr.MeasureString(label, _labelFont);

                // pill shape
                float lblX = rect.Right + offX - lblSz.Width - margin;
                float lblY = yLine - lblSz.Height / 2;
                float pad = 4f;
                float width = lblSz.Width + pad;
                float height = lblSz.Height + pad / 2;
                float radius = height / 2;
                var pill = new RectangleF(lblX - pad / 2, lblY - pad / 4, width, height);

                using var path = CreateRoundedRectanglePath(pill, radius);
                gr.FillPath(_labelBgBrush, path);

                // draw text
                gr.DrawString(label, _labelFont, _labelTextBrush, lblX, lblY);
            }
        }

        // helper: rounded-rect path
        private GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
