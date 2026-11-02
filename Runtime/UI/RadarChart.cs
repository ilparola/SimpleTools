using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimpleTools.UI
{
    // Radar ("spider") chart for UI Toolkit: one axis per value, starting at the top and going clockwise,
    // values from 0 to 1, concentric grid rings and a filled polygon. Axis labels are child Labels placed
    // just outside the chart, so they use the normal text styling.
    //
    // Styling (USS custom properties):
    //   --radar-grid-color, --radar-fill-color, --radar-line-color (colors)
    //   --radar-rings (int, grid rings), --radar-line-width (px), --radar-label-space (px kept for labels)
    // Labels have the class "radar-chart__label".
    [UxmlElement]
    public partial class RadarChart : VisualElement
    {
        public const string UssClassName = "radar-chart";
        public const string LabelUssClassName = "radar-chart__label";

        private static readonly CustomStyleProperty<Color> GridColorProperty = new CustomStyleProperty<Color>("--radar-grid-color");
        private static readonly CustomStyleProperty<Color> FillColorProperty = new CustomStyleProperty<Color>("--radar-fill-color");
        private static readonly CustomStyleProperty<Color> LineColorProperty = new CustomStyleProperty<Color>("--radar-line-color");
        private static readonly CustomStyleProperty<int> RingsProperty = new CustomStyleProperty<int>("--radar-rings");
        private static readonly CustomStyleProperty<float> LineWidthProperty = new CustomStyleProperty<float>("--radar-line-width");
        private static readonly CustomStyleProperty<float> LabelSpaceProperty = new CustomStyleProperty<float>("--radar-label-space");

        private readonly List<float> _values = new List<float>();
        private readonly List<Label> _labels = new List<Label>();

        private Color _gridColor = new Color(1f, 1f, 1f, 0.2f);
        private Color _fillColor = new Color(1f, 0.8f, 0.2f, 0.35f);
        private Color _lineColor = new Color(1f, 0.8f, 0.2f, 1f);
        private int _rings = 4;
        private float _lineWidth = 3f;
        private float _labelSpace = 40f;

        public RadarChart()
        {
            AddToClassList(UssClassName);
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
        }

        public IReadOnlyList<float> Values => _values;

        // Values from 0 to 1, one per axis
        public void SetValues(IReadOnlyList<float> values)
        {
            _values.Clear();
            foreach (float value in values)
            {
                _values.Add(Mathf.Clamp01(value));
            }

            MarkDirtyRepaint();
            PlaceLabels();
        }

        // One label per axis, in the same order as the values
        public void SetLabels(IReadOnlyList<string> labels)
        {
            foreach (Label label in _labels)
            {
                label.RemoveFromHierarchy();
            }

            _labels.Clear();
            foreach (string text in labels)
            {
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(LabelUssClassName);
                label.style.position = Position.Absolute;
                label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
                _labels.Add(label);
                Add(label);
            }

            PlaceLabels();
        }

        private int Axes => Mathf.Max(_values.Count, _labels.Count);

        private float Radius => Mathf.Max(0f, Mathf.Min(contentRect.width, contentRect.height) * 0.5f - _labelSpace);

        // Unit direction of an axis on screen (y down): the first axis points up
        private Vector2 Direction(int axis)
        {
            float angle = (-90f + 360f * axis / Axes) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            if (style.TryGetValue(GridColorProperty, out Color grid))
            {
                _gridColor = grid;
            }

            if (style.TryGetValue(FillColorProperty, out Color fill))
            {
                _fillColor = fill;
            }

            if (style.TryGetValue(LineColorProperty, out Color line))
            {
                _lineColor = line;
            }

            if (style.TryGetValue(RingsProperty, out int rings))
            {
                _rings = Mathf.Max(1, rings);
            }

            if (style.TryGetValue(LineWidthProperty, out float lineWidth))
            {
                _lineWidth = lineWidth;
            }

            if (style.TryGetValue(LabelSpaceProperty, out float labelSpace))
            {
                _labelSpace = labelSpace;
            }

            MarkDirtyRepaint();
            PlaceLabels();
        }

        private void PlaceLabels()
        {
            Vector2 center = contentRect.center;
            float distance = Radius + _labelSpace * 0.5f;
            for (int axis = 0; axis < _labels.Count; axis++)
            {
                Vector2 point = center + Direction(axis) * distance;
                _labels[axis].style.left = point.x;
                _labels[axis].style.top = point.y;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            int axes = Axes;
            float radius = Radius;
            if (axes < 3 || radius <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            Vector2 center = contentRect.center;
            painter.lineJoin = LineJoin.Round;

            // Grid: rings and spokes
            painter.strokeColor = _gridColor;
            painter.lineWidth = 1f;
            for (int ring = 1; ring <= _rings; ring++)
            {
                Polygon(painter, center, axis => radius * ring / _rings, axes);
                painter.Stroke();
            }

            for (int axis = 0; axis < axes; axis++)
            {
                painter.BeginPath();
                painter.MoveTo(center);
                painter.LineTo(center + Direction(axis) * radius);
                painter.Stroke();
            }

            if (_values.Count < axes)
            {
                return;
            }

            // Values
            Polygon(painter, center, axis => radius * _values[axis], axes);
            painter.fillColor = _fillColor;
            painter.Fill();
            painter.strokeColor = _lineColor;
            painter.lineWidth = _lineWidth;
            painter.Stroke();
        }

        private void Polygon(Painter2D painter, Vector2 center, System.Func<int, float> distance, int axes)
        {
            painter.BeginPath();
            for (int axis = 0; axis < axes; axis++)
            {
                Vector2 point = center + Direction(axis) * distance(axis);
                if (axis == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            painter.ClosePath();
        }
    }
}
