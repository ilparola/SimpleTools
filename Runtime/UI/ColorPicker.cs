using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SimpleTools.UI
{
    // Photoshop-style color picker for UI Toolkit: a saturation/brightness square for the current hue, a vertical hue
    // bar, the original and the new color side by side and a hex field.
    //
    // Mouse and touch: click or drag on the square and on the bar. Keyboard and gamepad (navigation events): with the
    // square focused, left/right change the saturation and up/down the brightness; with the bar focused, up/down change
    // the hue. A move past the end of a range is not consumed, so the focus can leave the picker.
    //
    // Works without a style sheet (sizes are set inline); classes for restyling: color-picker, color-picker__square,
    // color-picker__cursor, color-picker__hue, color-picker__handle, color-picker__swatches, color-picker__original,
    // color-picker__new, color-picker__hex.
    [UxmlElement]
    public partial class ColorPicker : VisualElement
    {
        public const string UssClassName = "color-picker";

        private const int SquareTextureSize = 128;
        private const float KeyboardStep = 0.02f;

        private readonly VisualElement _square;
        private readonly VisualElement _cursor;
        private readonly VisualElement _hue;
        private readonly VisualElement _handle;
        private readonly VisualElement _original;
        private readonly VisualElement _new;
        private readonly TextField _hex;
        private Texture2D _squareTexture;
        private Texture2D _hueTexture;

        private float _h;
        private float _s;
        private float _v;
        private float _textureHue = -1f;

        // Raised on every change made by the user (not when Value is set from code)
        public event Action<Color> ColorChanged;

        public ColorPicker()
        {
            AddToClassList(UssClassName);
            style.flexDirection = FlexDirection.Row;

            _square = new VisualElement { focusable = true, tabIndex = 0 };
            _square.AddToClassList(UssClassName + "__square");
            _square.style.width = 260;
            _square.style.height = 260;
            _square.style.flexShrink = 0;
            Add(_square);

            _cursor = Ring(18f);
            _cursor.AddToClassList(UssClassName + "__cursor");
            _square.Add(_cursor);

            _hue = new VisualElement { focusable = true, tabIndex = 0 };
            _hue.AddToClassList(UssClassName + "__hue");
            _hue.style.width = 32;
            _hue.style.height = 260;
            _hue.style.marginLeft = 16;
            _hue.style.flexShrink = 0;
            Add(_hue);

            _handle = new VisualElement { pickingMode = PickingMode.Ignore };
            _handle.AddToClassList(UssClassName + "__handle");
            _handle.style.position = Position.Absolute;
            _handle.style.left = -4;
            _handle.style.right = -4;
            _handle.style.height = 8;
            Border(_handle, Color.white, 2f, 2f);
            _hue.Add(_handle);

            var side = new VisualElement();
            side.AddToClassList(UssClassName + "__swatches");
            side.style.marginLeft = 20;
            side.style.width = 120;
            side.style.flexShrink = 0;
            Add(side);

            _new = Swatch();
            _new.AddToClassList(UssClassName + "__new");
            side.Add(_new);
            _original = Swatch();
            _original.AddToClassList(UssClassName + "__original");
            side.Add(_original);

            _hex = new TextField { maxLength = 7 };
            _hex.AddToClassList(UssClassName + "__hex");
            _hex.style.marginTop = 16;
            _hex.style.marginLeft = 0;
            _hex.style.marginRight = 0;
            side.Add(_hex);

            RegisterDrag(_square, SetFromSquare);
            RegisterDrag(_hue, SetFromHue);
            _square.RegisterCallback<NavigationMoveEvent>(OnSquareMove);
            _hue.RegisterCallback<NavigationMoveEvent>(OnHueMove);
            _square.RegisterCallback<GeometryChangedEvent>(_ => PlaceMarkers());
            _hue.RegisterCallback<GeometryChangedEvent>(_ => PlaceMarkers());
            _hex.RegisterCallback<FocusOutEvent>(_ => ApplyHex());
            _hex.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ApplyHex();
                }
            }, TrickleDown.TrickleDown);
            _hex.RegisterValueChangedCallback(evt =>
            {
                // A full six-digit code applies as soon as it is typed
                string text = evt.newValue.TrimStart('#');
                if (text.Length == 6)
                {
                    ApplyHex();
                }
            });
            RegisterCallback<AttachToPanelEvent>(_ => CreateTextures());
            RegisterCallback<DetachFromPanelEvent>(_ => DestroyTextures());
            CreateTextures();

            Value = Color.white;
            OriginalColor = Color.white;
        }

        // The current color (opaque); setting it does not raise ColorChanged
        public Color Value
        {
            get
            {
                Color color = Color.HSVToRGB(_h, _s, _v);
                color.a = 1f;
                return color;
            }
            set
            {
                Color.RGBToHSV(value, out float h, out _s, out _v);
                // Grays have no hue: keep the one shown so the square does not jump to red
                if (_s > 0.0001f && _v > 0.0001f)
                {
                    _h = h;
                }

                Refresh();
            }
        }

        // The color before the change, shown next to the new one
        public Color OriginalColor
        {
            get => _original.style.backgroundColor.value;
            set => _original.style.backgroundColor = new Color(value.r, value.g, value.b, 1f);
        }

        // The element that should get the focus when the picker opens
        public VisualElement FocusTarget => _square;

        private void SetFromSquare(Vector2 local)
        {
            Rect rect = _square.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            _s = Mathf.Clamp01(local.x / rect.width);
            _v = Mathf.Clamp01(1f - local.y / rect.height);
            Changed();
        }

        private void SetFromHue(Vector2 local)
        {
            Rect rect = _hue.contentRect;
            if (rect.height <= 0f)
            {
                return;
            }

            _h = Mathf.Clamp01(1f - local.y / rect.height);
            Changed();
        }

        private void OnSquareMove(NavigationMoveEvent evt)
        {
            float s = _s;
            float v = _v;
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left: s -= KeyboardStep; break;
                case NavigationMoveEvent.Direction.Right: s += KeyboardStep; break;
                case NavigationMoveEvent.Direction.Up: v += KeyboardStep; break;
                case NavigationMoveEvent.Direction.Down: v -= KeyboardStep; break;
                default: return;
            }

            // Past the end of the range the move goes on to the focus navigation
            if (s < -KeyboardStep * 0.5f || s > 1f + KeyboardStep * 0.5f || v < -KeyboardStep * 0.5f || v > 1f + KeyboardStep * 0.5f)
            {
                return;
            }

            _s = Mathf.Clamp01(s);
            _v = Mathf.Clamp01(v);
            Consume(evt);
            Changed();
        }

        private void OnHueMove(NavigationMoveEvent evt)
        {
            float step = evt.direction == NavigationMoveEvent.Direction.Up ? KeyboardStep
                : evt.direction == NavigationMoveEvent.Direction.Down ? -KeyboardStep
                : 0f;
            if (step == 0f)
            {
                return;
            }

            // The hue wraps around: red is at both ends
            _h = Mathf.Repeat(_h + step, 1f);
            Consume(evt);
            Changed();
        }

        private void Consume(EventBase evt)
        {
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        private void ApplyHex()
        {
            string text = _hex.value.Trim();
            if (!text.StartsWith("#"))
            {
                text = "#" + text;
            }

            if (text.Length == 7 && ColorUtility.TryParseHtmlString(text, out Color color))
            {
                if (color != Value)
                {
                    Value = color;
                    ColorChanged?.Invoke(Value);
                }
            }
            else
            {
                Refresh();
            }
        }

        private void Changed()
        {
            Refresh();
            ColorChanged?.Invoke(Value);
        }

        private void Refresh()
        {
            if (_squareTexture != null && !Mathf.Approximately(_textureHue, _h))
            {
                FillSquare();
            }

            Color color = Value;
            _new.style.backgroundColor = color;
            string hex = "#" + ColorUtility.ToHtmlStringRGB(color);
            if (_hex.focusController?.focusedElement != _hex)
            {
                _hex.SetValueWithoutNotify(hex);
            }

            PlaceMarkers();
        }

        private void PlaceMarkers()
        {
            Rect square = _square.contentRect;
            float half = _cursor.resolvedStyle.width * 0.5f;
            _cursor.style.left = _s * square.width - half;
            _cursor.style.top = (1f - _v) * square.height - half;
            // Dark ring on light colors, light ring on dark ones
            Color ring = _v > 0.6f && _s < 0.5f ? Color.black : Color.white;
            Border(_cursor, ring, 2f, 9f);

            Rect hue = _hue.contentRect;
            _handle.style.top = (1f - _h) * hue.height - 4f;
        }

        private void FillSquare()
        {
            _textureHue = _h;
            var pixels = new Color32[SquareTextureSize * SquareTextureSize];
            for (int y = 0; y < SquareTextureSize; y++)
            {
                float v = y / (SquareTextureSize - 1f);
                for (int x = 0; x < SquareTextureSize; x++)
                {
                    pixels[y * SquareTextureSize + x] = Color.HSVToRGB(_h, x / (SquareTextureSize - 1f), v);
                }
            }

            _squareTexture.SetPixels32(pixels);
            _squareTexture.Apply();
            _square.MarkDirtyRepaint();
        }

        private static Texture2D HueTexture()
        {
            const int Height = 256;
            var texture = new Texture2D(1, Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < Height; y++)
            {
                texture.SetPixel(0, y, Color.HSVToRGB(y / (Height - 1f), 1f, 1f));
            }

            texture.Apply();
            return texture;
        }

        // The textures live while the picker is in a panel
        private void CreateTextures()
        {
            if (_squareTexture != null)
            {
                return;
            }

            _squareTexture = new Texture2D(SquareTextureSize, SquareTextureSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _hueTexture = HueTexture();
            _square.style.backgroundImage = Background.FromTexture2D(_squareTexture);
            _hue.style.backgroundImage = Background.FromTexture2D(_hueTexture);
            _textureHue = -1f;
            Refresh();
        }

        private void DestroyTextures()
        {
            if (_squareTexture == null)
            {
                return;
            }

            _square.style.backgroundImage = StyleKeyword.None;
            _hue.style.backgroundImage = StyleKeyword.None;
            DestroyNow(_squareTexture);
            DestroyNow(_hueTexture);
            _squareTexture = null;
            _hueTexture = null;
        }

        private static void DestroyNow(UnityEngine.Object texture)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void RegisterDrag(VisualElement element, Action<Vector2> set)
        {
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                element.CapturePointer(evt.pointerId);
                element.Focus();
                set(evt.localPosition);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (element.HasPointerCapture(evt.pointerId))
                {
                    set(evt.localPosition);
                }
            });
            element.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (element.HasPointerCapture(evt.pointerId))
                {
                    element.ReleasePointer(evt.pointerId);
                }
            });
        }

        private static VisualElement Ring(float size)
        {
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.style.position = Position.Absolute;
            ring.style.width = size;
            ring.style.height = size;
            Border(ring, Color.white, 2f, size * 0.5f);
            return ring;
        }

        private static VisualElement Swatch()
        {
            var swatch = new VisualElement();
            swatch.style.height = 60;
            Border(swatch, new Color(1f, 1f, 1f, 0.35f), 1f, 0f);
            return swatch;
        }

        private static void Border(VisualElement element, Color color, float width, float radius)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
