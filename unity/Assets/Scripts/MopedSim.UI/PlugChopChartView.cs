using UnityEngine;
using UnityEngine.UI;
using MopedSim.Core;

namespace MopedSim.UI
{
    /// <summary>
    /// Plug chop color chart. Highlights the zone Core reported for the inspected spark plug.
    /// </summary>
    public sealed class PlugChopChartView : MonoBehaviour
    {
        [SerializeField] private Image[] _zoneSwatches;
        [SerializeField] private Image _insulatorPreview;
        [SerializeField] private Text _zoneLabel;
        [SerializeField] private Text _summaryLabel;
        [SerializeField] private Text _gapLabel;
        [SerializeField] private RectTransform _indexMarker;
        [SerializeField] private float _chartWidth = 400f;
        [SerializeField] private Color _chalkWhiteColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        [SerializeField] private Color _tanLeanColor = new Color(0.85f, 0.75f, 0.55f, 1f);
        [SerializeField] private Color _chocolateBrownColor = new Color(0.45f, 0.28f, 0.15f, 1f);
        [SerializeField] private Color _darkRichColor = new Color(0.2f, 0.15f, 0.12f, 1f);
        [SerializeField] private Color _wetBlackColor = new Color(0.05f, 0.05f, 0.05f, 1f);

        private PlugColorZone _displayedZone = PlugColorZone.ChocolateBrown;

        /// <summary>Zone shown by the last Render call.</summary>
        public PlugColorZone DisplayedZone { get { return _displayedZone; } }

        /// <summary>Readable chart label for a color zone.</summary>
        public static string GetZoneLabel(PlugColorZone zone)
        {
            switch (zone)
            {
                case PlugColorZone.ChalkWhite: return "Chalk White — LEAN, jet up!";
                case PlugColorZone.TanLean: return "Tan — slightly lean";
                case PlugColorZone.ChocolateBrown: return "Chocolate Brown — perfect";
                case PlugColorZone.DarkRich: return "Dark — rich";
                case PlugColorZone.WetBlack: return "Wet Black — fouled/rich, jet down";
                default: return string.Empty;
            }
        }

        /// <summary>Displays an inspected plug reading (event-driven).</summary>
        public void Render(in SparkPlugReading reading)
        {
            _displayedZone = reading.ColorZone;
            int zoneIndex = (int)reading.ColorZone;

            if (_zoneSwatches != null)
            {
                for (int i = 0; i < _zoneSwatches.Length; i++)
                {
                    Image swatch = _zoneSwatches[i];
                    if (swatch == null) { continue; }
                    float scale = (reading.IsInstalled && i == zoneIndex) ? 1.15f : 1f;
                    swatch.rectTransform.localScale = new Vector3(scale, scale, 1f);
                }
            }

            if (!reading.IsInstalled)
            {
                if (_zoneLabel != null) { _zoneLabel.text = "No plug installed"; }
                if (_summaryLabel != null) { _summaryLabel.text = string.Empty; }
                if (_gapLabel != null) { _gapLabel.text = string.Empty; }
                return;
            }

            if (_insulatorPreview != null) { _insulatorPreview.color = ZoneColor(reading.ColorZone); }
            if (_zoneLabel != null) { _zoneLabel.text = GetZoneLabel(reading.ColorZone); }
            if (_summaryLabel != null) { _summaryLabel.text = reading.DiagnosticSummary; }
            if (_gapLabel != null) { _gapLabel.text = "Gap: " + reading.ElectrodeGapMm.ToString("0.00") + " mm"; }
            if (_indexMarker != null)
            {
                Vector2 pos = _indexMarker.anchoredPosition;
                pos.x = reading.InsulatorColorIndex * _chartWidth;
                _indexMarker.anchoredPosition = pos;
            }
        }

        private Color ZoneColor(PlugColorZone zone)
        {
            switch (zone)
            {
                case PlugColorZone.ChalkWhite: return _chalkWhiteColor;
                case PlugColorZone.TanLean: return _tanLeanColor;
                case PlugColorZone.ChocolateBrown: return _chocolateBrownColor;
                case PlugColorZone.DarkRich: return _darkRichColor;
                default: return _wetBlackColor;
            }
        }
    }
}
